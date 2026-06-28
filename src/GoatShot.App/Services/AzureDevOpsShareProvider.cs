using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class AzureDevOpsShareProvider : IShareProvider
{
    private readonly AppSettings _settings;
    private readonly SecretStore _secretStore;
    private readonly HttpClient _httpClient;

    public AzureDevOpsShareProvider(AppSettings settings, SecretStore secretStore)
        : this(settings, secretStore, new HttpClient())
    {
    }

    public AzureDevOpsShareProvider(AppSettings settings, SecretStore secretStore, HttpClient httpClient)
    {
        _settings = settings;
        _secretStore = secretStore;
        _httpClient = httpClient;
    }

    public ShareDestination? Destination => ShareDestination.AzureDevOps;
    public string ProviderName => "Azure DevOps";
    public string AuthType => "Personal access token";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => false;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.AzureDevOpsOrganization) ||
            string.IsNullOrWhiteSpace(_settings.AzureDevOpsProject))
        {
            return Task.FromResult(new ProviderHealth(false, "Azure DevOps organization and project are not configured."));
        }

        if (string.IsNullOrWhiteSpace(_secretStore.ReadAzureDevOpsPat()))
        {
            return Task.FromResult(new ProviderHealth(false, "Azure DevOps personal access token is not saved with Windows DPAPI."));
        }

        return Task.FromResult(new ProviderHealth(true, "Azure DevOps is configured for local work item creation attempts."));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.AzureDevOpsOrganization) ||
            string.IsNullOrWhiteSpace(_settings.AzureDevOpsProject))
        {
            return new ShareUploadResult(false, null, "Azure DevOps work item creation needs organization and project settings.");
        }

        var pat = _secretStore.ReadAzureDevOpsPat();
        if (string.IsNullOrWhiteSpace(pat))
        {
            return new ShareUploadResult(false, null, "Azure DevOps work item creation needs a DPAPI-saved personal access token.");
        }

        var patch = new List<Dictionary<string, object?>>
        {
            JsonPatchAdd("/fields/System.Title", ShareProviderPayloads.ExpandMessage(_settings.AzureDevOpsTitleTemplate, request, "Azure DevOps")),
            JsonPatchAdd("/fields/System.Description", BuildDescriptionHtml(request))
        };

        var tags = BuildTags(_settings.AzureDevOpsTags);
        if (!string.IsNullOrWhiteSpace(tags))
        {
            patch.Add(JsonPatchAdd("/fields/System.Tags", tags));
        }

        if (!string.IsNullOrWhiteSpace(_settings.AzureDevOpsAssignedTo))
        {
            patch.Add(JsonPatchAdd("/fields/System.AssignedTo", _settings.AzureDevOpsAssignedTo.Trim()));
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildCreateWorkItemUri())
        {
            Content = ShareProviderPayloads.JsonContent(patch)
        };
        httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json-patch+json");
        httpRequest.Headers.Accept.ParseAdd("application/json");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($":{pat.Trim()}")));

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var id = response.IsSuccessStatusCode ? ExtractJsonInt(body, "id") : null;
        var workItemUrl = response.IsSuccessStatusCode
            ? ExtractWorkItemUrl(body) ?? BuildBrowseUrl(id)
            : null;
        if (!string.IsNullOrWhiteSpace(workItemUrl))
        {
            ClipboardInterop.SetText(workItemUrl);
        }

        return new ShareUploadResult(
            response.IsSuccessStatusCode,
            workItemUrl,
            response.IsSuccessStatusCode
                ? string.IsNullOrWhiteSpace(workItemUrl)
                    ? $"Azure DevOps work item created: {(int)response.StatusCode} {response.ReasonPhrase}"
                    : $"Azure DevOps work item created and copied URL: {workItemUrl}"
                : $"Azure DevOps work item creation failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
    }

    private Uri BuildCreateWorkItemUri()
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.AzureDevOpsBaseUrl)
            ? "https://dev.azure.com"
            : _settings.AzureDevOpsBaseUrl;
        var builder = new UriBuilder(NormalizeHttpBaseUrl(baseUrl));
        var basePath = builder.Path.Trim('/');
        builder.Path = CombineUriPath(
            basePath,
            EncodePathSegment(_settings.AzureDevOpsOrganization.Trim()),
            EncodePathSegment(_settings.AzureDevOpsProject.Trim()),
            "_apis",
            "wit",
            "workitems",
            $"${EncodePathSegment(string.IsNullOrWhiteSpace(_settings.AzureDevOpsWorkItemType) ? "Bug" : _settings.AzureDevOpsWorkItemType.Trim())}");
        builder.Query = "api-version=7.1";
        return builder.Uri;
    }

    private string? BuildBrowseUrl(int? workItemId)
    {
        if (workItemId is null)
        {
            return null;
        }

        var baseUrl = string.IsNullOrWhiteSpace(_settings.AzureDevOpsBaseUrl)
            ? "https://dev.azure.com"
            : _settings.AzureDevOpsBaseUrl;
        var builder = new UriBuilder(NormalizeHttpBaseUrl(baseUrl));
        var basePath = builder.Path.Trim('/');
        builder.Path = CombineUriPath(
            basePath,
            EncodePathSegment(_settings.AzureDevOpsOrganization.Trim()),
            EncodePathSegment(_settings.AzureDevOpsProject.Trim()),
            "_workitems",
            "edit",
            workItemId.Value.ToString(CultureInfo.InvariantCulture));
        builder.Query = string.Empty;
        return builder.Uri.ToString();
    }

    private static Dictionary<string, object?> JsonPatchAdd(string path, object? value)
    {
        return new Dictionary<string, object?>
        {
            ["op"] = "add",
            ["path"] = path,
            ["value"] = value
        };
    }

    private static string BuildTags(string? value)
    {
        return string.Join("; ", SplitProviderList(value));
    }

    private static string BuildDescriptionHtml(ShareUploadRequest request)
    {
        var lines = BuildIssuePlainLines(request, "Azure DevOps");
        var htmlLines = lines.Select(line => string.IsNullOrWhiteSpace(line)
            ? "<br/>"
            : $"{WebUtility.HtmlEncode(line)}<br/>");
        return string.Concat(htmlLines);
    }

    private static IReadOnlyList<string> BuildIssuePlainLines(ShareUploadRequest request, string destinationName)
    {
        var lines = new List<string>
        {
            "Created from GoatShot.",
            string.Empty,
            $"File: {MetadataValue(request, "fileName", Path.GetFileName(request.FilePath))}",
            $"Capture type: {MetadataValue(request, "captureType", request.CaptureType)}",
            $"Capture ID: {MetadataValue(request, "id")}",
            $"Created: {MetadataValue(request, "createdAt")}",
            $"Size: {FormatBytes(MetadataValue(request, "bytes"))}",
            $"Dimensions: {MetadataValue(request, "width")} x {MetadataValue(request, "height")}"
        };

        var sourceApp = MetadataValue(request, "sourceApp");
        if (!string.IsNullOrWhiteSpace(sourceApp))
        {
            lines.Add($"Source app: {sourceApp}");
        }

        var sourceWindowTitle = MetadataValue(request, "sourceWindowTitle");
        if (!string.IsNullOrWhiteSpace(sourceWindowTitle))
        {
            lines.Add($"Window title: {sourceWindowTitle}");
        }

        lines.Add(string.Empty);
        lines.Add($"{destinationName} issue creation does not upload the local capture file. Upload the media through a file destination when a remote asset is needed.");
        AppendRedactedPlainSection(lines, "Notes", MetadataValue(request, "notes"), 4_000);
        AppendRedactedPlainSection(lines, "OCR", MetadataValue(request, "ocrText"), 4_000);
        return lines;
    }

    private static void AppendRedactedPlainSection(List<string> lines, string heading, string? text, int maxLength)
    {
        var redacted = RedactAndTruncate(text, maxLength);
        if (string.IsNullOrWhiteSpace(redacted))
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add($"{heading}:");
        lines.Add(redacted);
    }

    private static string RedactAndTruncate(string? text, int maxLength)
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

    private static int? ExtractJsonInt(string responseBody, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty(propertyName, out var value))
            {
                return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
                    ? number
                    : null;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string? ExtractWorkItemUrl(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.TryGetProperty("_links", out var links) &&
                links.ValueKind == JsonValueKind.Object &&
                links.TryGetProperty("html", out var html) &&
                html.ValueKind == JsonValueKind.Object)
            {
                var href = ReadString(html, "href");
                if (!string.IsNullOrWhiteSpace(href))
                {
                    return href;
                }
            }

            var url = ReadString(root, "url");
            if (!string.IsNullOrWhiteSpace(url))
            {
                return url;
            }
        }
        catch (JsonException)
        {
            return ShareProviderPayloads.FirstUrl(responseBody);
        }

        return ShareProviderPayloads.FirstUrl(responseBody);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
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
