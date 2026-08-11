using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class LinearShareProvider : IShareProvider
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly AppSettings _settings;
    private readonly SecretStore _secretStore;
    private readonly HttpClient _httpClient;

    public LinearShareProvider(AppSettings settings, SecretStore secretStore)
        : this(settings, secretStore, new HttpClient())
    {
    }

    public LinearShareProvider(AppSettings settings, SecretStore secretStore, HttpClient httpClient)
    {
        _settings = settings;
        _secretStore = secretStore;
        _httpClient = httpClient;
    }

    public ShareDestination? Destination => ShareDestination.Linear;
    public string ProviderName => "Linear";
    public string AuthType => "API key or OAuth";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => false;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_secretStore.ReadLinearCredential()))
        {
            return Task.FromResult(new ProviderHealth(false, "Linear personal API key or OAuth access token is not saved with Windows DPAPI."));
        }

        if (string.IsNullOrWhiteSpace(_settings.LinearIssueId) &&
            string.IsNullOrWhiteSpace(_settings.LinearTeamId))
        {
            return Task.FromResult(new ProviderHealth(false, "Linear needs either an existing issue ID or a team ID for issue creation."));
        }

        return Task.FromResult(new ProviderHealth(true, "Linear is configured for local upload and issue/attachment attempts."));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var credential = _secretStore.ReadLinearCredential();
        if (string.IsNullOrWhiteSpace(credential))
        {
            return new ShareUploadResult(false, null, "Linear upload needs a DPAPI-saved personal API key or OAuth access token.");
        }

        var existingIssueId = TrimToNull(_settings.LinearIssueId);
        var teamId = TrimToNull(_settings.LinearTeamId);
        if (string.IsNullOrWhiteSpace(existingIssueId) && string.IsNullOrWhiteSpace(teamId))
        {
            return new ShareUploadResult(false, null, "Linear upload needs either an existing issue ID or a team ID for issue creation.");
        }

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return new ShareUploadResult(false, null, $"Source file does not exist: {request.FilePath}");
        }

        var uploadRequest = await RequestFileUploadAsync(request, credential.Trim(), cancellationToken);
        if (!uploadRequest.Succeeded || uploadRequest.Upload is null)
        {
            return new ShareUploadResult(false, null, $"Linear file upload request failed: {uploadRequest.Message}");
        }

        var fileUpload = await UploadFileContentAsync(request, uploadRequest.Upload, cancellationToken);
        if (!fileUpload.Succeeded)
        {
            return new ShareUploadResult(false, null, $"Linear signed file upload failed: {fileUpload.Message}");
        }

        LinearIssueInfo? issue = null;
        var issueId = existingIssueId;
        var createdIssueMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(issueId))
        {
            var issueCreate = await CreateIssueAsync(request, uploadRequest.Upload.AssetUrl, credential.Trim(), cancellationToken);
            if (!issueCreate.Succeeded || issueCreate.Issue is null)
            {
                return new ShareUploadResult(
                    false,
                    uploadRequest.Upload.AssetUrl,
                    $"Linear file uploaded, but issue creation failed: {issueCreate.Message}");
            }

            issue = issueCreate.Issue;
            issueId = issue.Id;
            createdIssueMessage = string.IsNullOrWhiteSpace(issue.Identifier)
                ? "Linear issue created."
                : $"Linear issue {issue.Identifier} created.";
        }

        var attachmentMessage = string.Empty;
        if (_settings.LinearCreateAttachment)
        {
            var attachment = await CreateAttachmentAsync(
                issueId!,
                request,
                uploadRequest.Upload.AssetUrl,
                credential.Trim(),
                cancellationToken);
            if (!attachment.Succeeded)
            {
                return new ShareUploadResult(
                    false,
                    issue?.Url ?? uploadRequest.Upload.AssetUrl,
                    $"Linear file uploaded, but attachment linking failed: {attachment.Message}");
            }

            attachmentMessage = " Linear attachment was linked to the issue.";
        }

        var shareUrl = issue?.Url ?? uploadRequest.Upload.AssetUrl;
        if (!string.IsNullOrWhiteSpace(shareUrl))
        {
            ClipboardInterop.SetText(shareUrl);
        }

        var targetMessage = issue is null
            ? $"Linear file uploaded and attached to existing issue {issueId}."
            : createdIssueMessage;
        return new ShareUploadResult(
            true,
            shareUrl,
            string.IsNullOrWhiteSpace(shareUrl)
                ? $"{targetMessage}{attachmentMessage} Uploaded file is stored in Linear private file storage."
                : $"{targetMessage}{attachmentMessage} Copied URL: {shareUrl}");
    }

    private async Task<(bool Succeeded, LinearUploadFile? Upload, string Message)> RequestFileUploadAsync(
        ShareUploadRequest request,
        string credential,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(request.FilePath);
        if (fileInfo.Length > int.MaxValue)
        {
            return (false, null, "Linear file upload size is too large for the GraphQL fileUpload request.");
        }

        const string query =
            """
            mutation RequestUpload($filename: String!, $contentType: String!, $size: Int!) {
              fileUpload(filename: $filename, contentType: $contentType, size: $size) {
                success
                uploadFile {
                  uploadUrl
                  assetUrl
                  headers {
                    key
                    value
                  }
                }
              }
            }
            """;
        var variables = new
        {
            filename = BuildRemoteFileName(request),
            contentType = ShareProviderPayloads.DetectContentType(request.FilePath),
            size = (int)fileInfo.Length
        };

        var result = await SendGraphQlAsync(query, variables, credential, cancellationToken);
        if (!result.Succeeded)
        {
            return (false, null, result.Message);
        }

        var upload = ExtractUploadFile(result.Body);
        return upload is null
            ? (false, null, "Linear GraphQL response did not include an upload URL and asset URL.")
            : (true, upload, "Linear upload URL returned.");
    }

    private async Task<(bool Succeeded, string Message)> UploadFileContentAsync(
        ShareUploadRequest request,
        LinearUploadFile upload,
        CancellationToken cancellationToken)
    {
        await using var fileStream = File.OpenRead(request.FilePath);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Put, upload.UploadUrl)
        {
            Content = new StreamContent(fileStream)
        };
        httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(ShareProviderPayloads.DetectContentType(request.FilePath));
        ApplyUploadHeaders(httpRequest, upload.Headers);
        if (upload.Headers.All(header => !header.Key.Equals("Cache-Control", StringComparison.OrdinalIgnoreCase)))
        {
            httpRequest.Headers.TryAddWithoutValidation("Cache-Control", "public, max-age=31536000");
        }

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return response.IsSuccessStatusCode
            ? (true, "Linear signed upload completed.")
            : (false, $"{(int)response.StatusCode} {response.ReasonPhrase} {body}");
    }

    private async Task<(bool Succeeded, LinearIssueInfo? Issue, string Message)> CreateIssueAsync(
        ShareUploadRequest request,
        string assetUrl,
        string credential,
        CancellationToken cancellationToken)
    {
        var teamId = TrimToNull(_settings.LinearTeamId);
        if (string.IsNullOrWhiteSpace(teamId))
        {
            return (false, null, "Linear team ID is required when creating a new issue.");
        }

        const string query =
            """
            mutation CreateIssue($input: IssueCreateInput!) {
              issueCreate(input: $input) {
                success
                issue {
                  id
                  identifier
                  url
                  title
                }
              }
            }
            """;
        var variables = new
        {
            input = new
            {
                teamId,
                title = BuildIssueTitle(request),
                description = BuildIssueDescription(request, assetUrl)
            }
        };

        var result = await SendGraphQlAsync(query, variables, credential, cancellationToken);
        if (!result.Succeeded)
        {
            return (false, null, result.Message);
        }

        var issue = ExtractIssue(result.Body);
        return issue is null
            ? (false, null, "Linear GraphQL response did not include the created issue.")
            : (true, issue, "Linear issue created.");
    }

    private async Task<(bool Succeeded, string Message)> CreateAttachmentAsync(
        string issueId,
        ShareUploadRequest request,
        string assetUrl,
        string credential,
        CancellationToken cancellationToken)
    {
        const string query =
            """
            mutation CreateAttachment($input: AttachmentCreateInput!) {
              attachmentCreate(input: $input) {
                success
                attachment {
                  id
                  url
                }
              }
            }
            """;
        var variables = new
        {
            input = new
            {
                issueId,
                title = $"Receipts capture: {FileName(request)}",
                subtitle = $"{request.CaptureType} capture, {MetadataValue(request, "width")}x{MetadataValue(request, "height")}, {FormatBytes(MetadataValue(request, "bytes"))}",
                url = assetUrl
            }
        };

        var result = await SendGraphQlAsync(query, variables, credential, cancellationToken);
        if (!result.Succeeded)
        {
            return (false, result.Message);
        }

        var attachmentId = ExtractAttachmentId(result.Body);
        return string.IsNullOrWhiteSpace(attachmentId)
            ? (false, "Linear GraphQL response did not include the created attachment.")
            : (true, $"Linear attachment created: {attachmentId}.");
    }

    private async Task<(bool Succeeded, string Body, string Message)> SendGraphQlAsync(
        string query,
        object variables,
        string credential,
        CancellationToken cancellationToken)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildGraphQlUri())
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { query, variables }, CompactJsonOptions),
                Encoding.UTF8,
                "application/json")
        };
        ApplyAuthorization(httpRequest, credential);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (false, body, $"{(int)response.StatusCode} {response.ReasonPhrase} {body}");
        }

        var graphQlErrors = ExtractGraphQlErrors(body);
        return string.IsNullOrWhiteSpace(graphQlErrors)
            ? (true, body, "Linear GraphQL request completed.")
            : (false, body, graphQlErrors);
    }

    private void ApplyAuthorization(HttpRequestMessage request, string credential)
    {
        if (_settings.LinearUseOAuthBearerToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Trim());
            return;
        }

        request.Headers.TryAddWithoutValidation("Authorization", credential.Trim());
    }

    private Uri BuildGraphQlUri()
    {
        var endpoint = string.IsNullOrWhiteSpace(_settings.LinearGraphqlEndpoint)
            ? "https://api.linear.app/graphql"
            : _settings.LinearGraphqlEndpoint.Trim();
        if (!endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            endpoint = $"https://{endpoint}";
        }

        return new Uri(endpoint, UriKind.Absolute);
    }

    private string BuildIssueTitle(ShareUploadRequest request)
    {
        var template = string.IsNullOrWhiteSpace(_settings.LinearIssueTitleTemplate)
            ? "Receipts capture: {file}"
            : _settings.LinearIssueTitleTemplate.Trim();
        var createdAt = ParseCreatedAt(MetadataValue(request, "createdAt"));
        return template
            .Replace("{file}", FileName(request), StringComparison.OrdinalIgnoreCase)
            .Replace("{id}", MetadataValue(request, "id"), StringComparison.OrdinalIgnoreCase)
            .Replace("{capture_type}", request.CaptureType, StringComparison.OrdinalIgnoreCase)
            .Replace("{source_app}", MetadataValue(request, "sourceApp"), StringComparison.OrdinalIgnoreCase)
            .Replace("{width}", MetadataValue(request, "width"), StringComparison.OrdinalIgnoreCase)
            .Replace("{height}", MetadataValue(request, "height"), StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", createdAt?.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", createdAt?.LocalDateTime.ToString("HH-mm-ss", CultureInfo.InvariantCulture) ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static string BuildIssueDescription(ShareUploadRequest request, string assetUrl)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Created from Receipts.");
        builder.AppendLine();
        if (ShareProviderPayloads.DetectContentType(request.FilePath).StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine($"![{EscapeMarkdownLabel(FileName(request))}]({assetUrl})");
        }
        else
        {
            builder.AppendLine($"[{EscapeMarkdownLabel(FileName(request))}]({assetUrl})");
        }

        builder.AppendLine();
        builder.AppendLine("| Field | Value |");
        builder.AppendLine("| --- | --- |");
        builder.AppendLine($"| Capture ID | `{MetadataValue(request, "id")}` |");
        builder.AppendLine($"| File | `{FileName(request)}` |");
        builder.AppendLine($"| Type | `{request.CaptureType}` |");
        builder.AppendLine($"| Size | `{MetadataValue(request, "width")}x{MetadataValue(request, "height")}`, `{FormatBytes(MetadataValue(request, "bytes"))}` |");
        builder.AppendLine($"| Created | `{MetadataValue(request, "createdAt")}` |");

        var sourceApp = MetadataValue(request, "sourceApp");
        if (!string.IsNullOrWhiteSpace(sourceApp))
        {
            builder.AppendLine($"| Source app | `{EscapeMarkdownTableValue(sourceApp)}` |");
        }

        var sourceWindowTitle = MetadataValue(request, "sourceWindowTitle");
        if (!string.IsNullOrWhiteSpace(sourceWindowTitle))
        {
            builder.AppendLine($"| Window | `{EscapeMarkdownTableValue(sourceWindowTitle)}` |");
        }

        var bounds = MetadataValue(request, "bounds");
        if (!string.IsNullOrWhiteSpace(bounds))
        {
            builder.AppendLine($"| Bounds | `{EscapeMarkdownTableValue(bounds)}` |");
        }

        return builder.ToString().Trim();
    }

    private static void ApplyUploadHeaders(HttpRequestMessage request, IReadOnlyList<LinearUploadHeader> headers)
    {
        foreach (var header in headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key) || string.IsNullOrWhiteSpace(header.Value))
            {
                continue;
            }

            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    request.Content!.Headers.ContentType = MediaTypeHeaderValue.Parse(header.Value);
                }
                catch (FormatException)
                {
                    request.Content!.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                continue;
            }

            if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                request.Content!.Headers.TryAddWithoutValidation(header.Key, header.Value);
                continue;
            }

            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private static LinearUploadFile? ExtractUploadFile(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!TryGetDataObject(document.RootElement, "fileUpload", out var upload) ||
                !ReadSuccess(upload) ||
                !upload.TryGetProperty("uploadFile", out var uploadFile) ||
                uploadFile.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var uploadUrl = ReadString(uploadFile, "uploadUrl");
            var assetUrl = ReadString(uploadFile, "assetUrl");
            if (string.IsNullOrWhiteSpace(uploadUrl) || string.IsNullOrWhiteSpace(assetUrl))
            {
                return null;
            }

            var headers = new List<LinearUploadHeader>();
            if (uploadFile.TryGetProperty("headers", out var headerArray) &&
                headerArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var header in headerArray.EnumerateArray())
                {
                    var key = ReadString(header, "key");
                    var value = ReadString(header, "value");
                    if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                    {
                        headers.Add(new LinearUploadHeader(key, value));
                    }
                }
            }

            return new LinearUploadFile(uploadUrl, assetUrl, headers);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LinearIssueInfo? ExtractIssue(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!TryGetDataObject(document.RootElement, "issueCreate", out var issueCreate) ||
                !ReadSuccess(issueCreate) ||
                !issueCreate.TryGetProperty("issue", out var issue) ||
                issue.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var id = ReadString(issue, "id");
            return string.IsNullOrWhiteSpace(id)
                ? null
                : new LinearIssueInfo(
                    id,
                    ReadString(issue, "identifier"),
                    ReadString(issue, "url"),
                    ReadString(issue, "title"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractAttachmentId(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!TryGetDataObject(document.RootElement, "attachmentCreate", out var attachmentCreate) ||
                !ReadSuccess(attachmentCreate) ||
                !attachmentCreate.TryGetProperty("attachment", out var attachment) ||
                attachment.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return ReadString(attachment, "id");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractGraphQlErrors(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("errors", out var errors) ||
                errors.ValueKind != JsonValueKind.Array ||
                errors.GetArrayLength() == 0)
            {
                return null;
            }

            var messages = errors
                .EnumerateArray()
                .Select(error => ReadString(error, "message"))
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .ToList();
            return messages.Count == 0
                ? "Linear GraphQL returned one or more errors."
                : $"Linear GraphQL error: {string.Join("; ", messages)}";
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetDataObject(JsonElement root, string propertyName, out JsonElement value)
    {
        value = default;
        return root.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty(propertyName, out value) &&
            value.ValueKind == JsonValueKind.Object;
    }

    private static bool ReadSuccess(JsonElement element)
    {
        return !element.TryGetProperty("success", out var success) ||
            success.ValueKind != JsonValueKind.False;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string BuildRemoteFileName(ShareUploadRequest request)
    {
        var fileName = string.Join("_", FileName(request).Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return $"{MetadataValue(request, "id", Guid.NewGuid().ToString("N"))}-{fileName}";
    }

    private static string FileName(ShareUploadRequest request)
    {
        return MetadataValue(request, "fileName", Path.GetFileName(request.FilePath));
    }

    private static string EscapeMarkdownLabel(string value)
    {
        return value
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
    }

    private static string EscapeMarkdownTableValue(string value)
    {
        return value
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("`", "'", StringComparison.Ordinal);
    }

    private static DateTimeOffset? ParseCreatedAt(string value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatBytes(string value)
    {
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes)
            ? $"{bytes:N0} bytes"
            : value;
    }

    private static string MetadataValue(ShareUploadRequest request, string key, string fallback = "")
    {
        return request.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record LinearUploadFile(
        string UploadUrl,
        string AssetUrl,
        IReadOnlyList<LinearUploadHeader> Headers);

    private sealed record LinearUploadHeader(string Key, string Value);

    private sealed record LinearIssueInfo(
        string Id,
        string? Identifier,
        string? Url,
        string? Title);
}
