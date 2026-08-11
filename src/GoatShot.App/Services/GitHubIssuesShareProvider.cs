using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class GitHubIssuesShareProvider : IShareProvider
{
    private readonly AppSettings _settings;
    private readonly SecretStore _secretStore;
    private readonly HttpClient _httpClient;

    public GitHubIssuesShareProvider(AppSettings settings, SecretStore secretStore)
        : this(settings, secretStore, new HttpClient())
    {
    }

    public GitHubIssuesShareProvider(AppSettings settings, SecretStore secretStore, HttpClient httpClient)
    {
        _settings = settings;
        _secretStore = secretStore;
        _httpClient = httpClient;
    }

    public ShareDestination? Destination => ShareDestination.GitHubIssues;
    public string ProviderName => "GitHub Issues";
    public string AuthType => "Personal access token";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => true;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.GitHubRepository))
        {
            return Task.FromResult(new ProviderHealth(false, "GitHub repository is not configured."));
        }

        if (ParseRepository(_settings.GitHubRepository) is null)
        {
            return Task.FromResult(new ProviderHealth(false, "GitHub repository must be configured as owner/repo."));
        }

        if (string.IsNullOrWhiteSpace(_secretStore.ReadGitHubToken()))
        {
            return Task.FromResult(new ProviderHealth(false, "GitHub personal access token is not saved with Windows DPAPI."));
        }

        return Task.FromResult(new ProviderHealth(true, "GitHub Issues is configured for local issue creation attempts."));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.GitHubRepository))
        {
            return new ShareUploadResult(false, null, "GitHub issue creation needs a repository setting in owner/repo form.");
        }

        var token = _secretStore.ReadGitHubToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return new ShareUploadResult(false, null, "GitHub issue creation needs a DPAPI-saved personal access token.");
        }

        var repository = ParseRepository(_settings.GitHubRepository);
        if (repository is null)
        {
            return new ShareUploadResult(false, null, "GitHub repository must be configured as owner/repo.");
        }

        var requestBody = new Dictionary<string, object?>
        {
            ["title"] = ShareProviderPayloads.ExpandMessage(_settings.GitHubIssueTitleTemplate, request, "GitHub Issues"),
            ["body"] = BuildIssueMarkdownBody(request)
        };

        var labels = SplitProviderList(_settings.GitHubLabels);
        if (labels.Count > 0)
        {
            requestBody["labels"] = labels;
        }

        var assignees = SplitProviderList(_settings.GitHubAssignees);
        if (assignees.Count > 0)
        {
            requestBody["assignees"] = assignees;
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildIssueUri(repository.Value.Owner, repository.Value.Repo))
        {
            Content = ShareProviderPayloads.JsonContent(requestBody)
        };
        httpRequest.Headers.UserAgent.ParseAdd($"Receipts/{BrandIdentity.ReleaseVersion}");
        httpRequest.Headers.Accept.ParseAdd("application/vnd.github+json");
        httpRequest.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var issueUrl = response.IsSuccessStatusCode
            ? ExtractJsonString(body, "html_url") ?? ShareProviderPayloads.FirstUrl(body)
            : null;
        if (!string.IsNullOrWhiteSpace(issueUrl))
        {
            ClipboardInterop.SetText(issueUrl);
        }

        return new ShareUploadResult(
            response.IsSuccessStatusCode,
            issueUrl,
            response.IsSuccessStatusCode
                ? string.IsNullOrWhiteSpace(issueUrl)
                    ? $"GitHub issue created: {(int)response.StatusCode} {response.ReasonPhrase}"
                    : $"GitHub issue created and copied URL: {issueUrl}"
                : $"GitHub issue creation failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
    }

    private Uri BuildIssueUri(string owner, string repo)
    {
        var baseUrl = NormalizeHttpBaseUrl(string.IsNullOrWhiteSpace(_settings.GitHubApiBaseUrl)
            ? "https://api.github.com"
            : _settings.GitHubApiBaseUrl);
        var builder = new UriBuilder(baseUrl);
        var basePath = builder.Path.Trim('/');
        builder.Path = CombineUriPath(basePath, "repos", EncodePathSegment(owner), EncodePathSegment(repo), "issues");
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private static string BuildIssueMarkdownBody(ShareUploadRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Created from Receipts.");
        builder.AppendLine();
        builder.AppendLine("| Field | Value |");
        builder.AppendLine("|---|---|");
        builder.AppendLine($"| File | {EscapeMarkdownTableValue(MetadataValue(request, "fileName", Path.GetFileName(request.FilePath)))} |");
        builder.AppendLine($"| Capture type | {EscapeMarkdownTableValue(MetadataValue(request, "captureType", request.CaptureType))} |");
        builder.AppendLine($"| Capture ID | {EscapeMarkdownTableValue(MetadataValue(request, "id"))} |");
        builder.AppendLine($"| Created | {EscapeMarkdownTableValue(MetadataValue(request, "createdAt"))} |");
        builder.AppendLine($"| Size | {EscapeMarkdownTableValue(FormatBytes(MetadataValue(request, "bytes")))} |");
        builder.AppendLine($"| Dimensions | {EscapeMarkdownTableValue(MetadataValue(request, "width"))} x {EscapeMarkdownTableValue(MetadataValue(request, "height"))} |");

        var sourceApp = MetadataValue(request, "sourceApp");
        if (!string.IsNullOrWhiteSpace(sourceApp))
        {
            builder.AppendLine($"| Source app | {EscapeMarkdownTableValue(sourceApp)} |");
        }

        var sourceWindowTitle = MetadataValue(request, "sourceWindowTitle");
        if (!string.IsNullOrWhiteSpace(sourceWindowTitle))
        {
            builder.AppendLine($"| Window title | {EscapeMarkdownTableValue(sourceWindowTitle)} |");
        }

        builder.AppendLine();
        builder.AppendLine("GitHub Issues issue creation does not upload the local capture file. Upload the media through a file destination such as S3, WebDAV, FTP/FTPS, Dropbox, Google Drive, OneDrive, Cloudinary, Discord, or Linear when a remote asset is needed.");

        AppendRedactedMarkdownSection(builder, "Notes", MetadataValue(request, "notes"), 4_000);
        AppendRedactedMarkdownSection(builder, "OCR", MetadataValue(request, "ocrText"), 4_000);
        return builder.ToString().Trim();
    }

    private static void AppendRedactedMarkdownSection(StringBuilder builder, string title, string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        builder.AppendLine(RedactAndTruncate(text, maxLength));
    }

    private static string RedactAndTruncate(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var redacted = SensitiveTextDetector.Scan(text).RedactedText.Trim();
        return redacted.Length <= maxLength
            ? redacted
            : redacted[..maxLength] + "...";
    }

    private static IReadOnlyList<string> SplitProviderList(string? value)
    {
        return (value ?? string.Empty)
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static (string Owner, string Repo)? ParseRepository(string value)
    {
        var parts = value.Trim().Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 && parts.All(part => !string.IsNullOrWhiteSpace(part))
            ? (parts[0], parts[1])
            : null;
    }

    private static string? ExtractJsonString(string responseBody, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return document.RootElement.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizeHttpBaseUrl(string value)
    {
        var baseUrl = value.Trim();
        return baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"https://{baseUrl}";
    }

    private static string CombineUriPath(params string?[] parts)
    {
        return string.Join(
            "/",
            parts
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim('/')));
    }

    private static string EncodePathSegment(string value)
    {
        return Uri.EscapeDataString(value)
            .Replace("%2D", "-", StringComparison.OrdinalIgnoreCase)
            .Replace("%2E", ".", StringComparison.OrdinalIgnoreCase)
            .Replace("%5F", "_", StringComparison.OrdinalIgnoreCase)
            .Replace("%7E", "~", StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapeMarkdownTableValue(string value)
    {
        return value
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("`", "'", StringComparison.Ordinal);
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
}
