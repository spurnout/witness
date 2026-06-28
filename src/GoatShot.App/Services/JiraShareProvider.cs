using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class JiraShareProvider : IShareProvider
{
    private readonly AppSettings _settings;
    private readonly SecretStore _secretStore;
    private readonly HttpClient _httpClient;

    public JiraShareProvider(AppSettings settings, SecretStore secretStore)
        : this(settings, secretStore, new HttpClient())
    {
    }

    public JiraShareProvider(AppSettings settings, SecretStore secretStore, HttpClient httpClient)
    {
        _settings = settings;
        _secretStore = secretStore;
        _httpClient = httpClient;
    }

    public ShareDestination? Destination => ShareDestination.Jira;
    public string ProviderName => "Jira";
    public string AuthType => "API token";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => false;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.JiraBaseUrl) ||
            string.IsNullOrWhiteSpace(_settings.JiraProjectKey))
        {
            return Task.FromResult(new ProviderHealth(false, "Jira site base URL and project key are not configured."));
        }

        if (string.IsNullOrWhiteSpace(_settings.JiraAccountEmail))
        {
            return Task.FromResult(new ProviderHealth(false, "Jira account email is not configured."));
        }

        if (string.IsNullOrWhiteSpace(_secretStore.ReadJiraApiToken()))
        {
            return Task.FromResult(new ProviderHealth(false, "Jira API token is not saved with Windows DPAPI."));
        }

        return Task.FromResult(new ProviderHealth(true, "Jira is configured for local issue creation attempts."));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.JiraBaseUrl) ||
            string.IsNullOrWhiteSpace(_settings.JiraProjectKey))
        {
            return new ShareUploadResult(false, null, "Jira issue creation needs site base URL and project key settings.");
        }

        if (string.IsNullOrWhiteSpace(_settings.JiraAccountEmail))
        {
            return new ShareUploadResult(false, null, "Jira issue creation needs an account email setting.");
        }

        var apiToken = _secretStore.ReadJiraApiToken();
        if (string.IsNullOrWhiteSpace(apiToken))
        {
            return new ShareUploadResult(false, null, "Jira issue creation needs a DPAPI-saved API token.");
        }

        var fields = new Dictionary<string, object?>
        {
            ["project"] = new { key = _settings.JiraProjectKey.Trim() },
            ["summary"] = ShareProviderPayloads.ExpandMessage(_settings.JiraSummaryTemplate, request, "Jira"),
            ["issuetype"] = new { name = string.IsNullOrWhiteSpace(_settings.JiraIssueType) ? "Bug" : _settings.JiraIssueType.Trim() },
            ["description"] = BuildJiraAdfDescription(request)
        };

        var labels = SplitProviderList(_settings.JiraLabels);
        if (labels.Count > 0)
        {
            fields["labels"] = labels;
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildIssueUri())
        {
            Content = ShareProviderPayloads.JsonContent(new { fields })
        };
        httpRequest.Headers.Accept.ParseAdd("application/json");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.JiraAccountEmail.Trim()}:{apiToken.Trim()}")));

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var issueKey = response.IsSuccessStatusCode ? ExtractJsonString(body, "key") : null;
        var issueUrl = response.IsSuccessStatusCode
            ? BuildBrowseUrl(issueKey) ?? ExtractJsonString(body, "self") ?? ShareProviderPayloads.FirstUrl(body)
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
                    ? $"Jira issue created: {(int)response.StatusCode} {response.ReasonPhrase}"
                    : $"Jira issue created and copied URL: {issueUrl}"
                : $"Jira issue creation failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
    }

    private Uri BuildIssueUri()
    {
        var builder = new UriBuilder(NormalizeHttpBaseUrl(_settings.JiraBaseUrl));
        var basePath = builder.Path.Trim('/');
        builder.Path = basePath.EndsWith("rest/api/3", StringComparison.OrdinalIgnoreCase)
            ? CombineUriPath(basePath, "issue")
            : CombineUriPath(basePath, "rest", "api", "3", "issue");
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private string? BuildBrowseUrl(string? issueKey)
    {
        if (string.IsNullOrWhiteSpace(issueKey))
        {
            return null;
        }

        var builder = new UriBuilder(NormalizeHttpBaseUrl(_settings.JiraBaseUrl));
        var basePath = builder.Path.Trim('/');
        builder.Path = CombineUriPath(basePath, "browse", EncodePathSegment(issueKey.Trim()));
        builder.Query = string.Empty;
        return builder.Uri.ToString();
    }

    private static object BuildJiraAdfDescription(ShareUploadRequest request)
    {
        var lines = BuildIssuePlainLines(request, "Jira");
        return new
        {
            version = 1,
            type = "doc",
            content = lines
                .Select(line => new
                {
                    type = "paragraph",
                    content = string.IsNullOrWhiteSpace(line)
                        ? Array.Empty<object>()
                        : new object[]
                        {
                            new
                            {
                                type = "text",
                                text = line
                            }
                        }
                })
                .ToArray()
        };
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
