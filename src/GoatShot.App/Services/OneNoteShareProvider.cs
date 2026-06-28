using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class OneNoteShareProvider : IShareProvider
{
    private readonly AppSettings _settings;
    private readonly SecretStore _secretStore;
    private readonly HttpClient _httpClient;

    public OneNoteShareProvider(AppSettings settings, SecretStore secretStore)
        : this(settings, secretStore, new HttpClient())
    {
    }

    public OneNoteShareProvider(AppSettings settings, SecretStore secretStore, HttpClient httpClient)
    {
        _settings = settings;
        _secretStore = secretStore;
        _httpClient = httpClient;
    }

    public ShareDestination? Destination => ShareDestination.OneNote;
    public string ProviderName => "OneNote";
    public string AuthType => "OAuth";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => false;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_secretStore.ReadOneNoteAccessToken()))
        {
            return Task.FromResult(new ProviderHealth(false, "OneNote OAuth access token is not saved with Windows DPAPI."));
        }

        return Task.FromResult(new ProviderHealth(true, "OneNote is configured for local page creation attempts."));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return new ShareUploadResult(false, null, $"Source file does not exist: {request.FilePath}");
        }

        var token = _secretStore.ReadOneNoteAccessToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return new ShareUploadResult(false, null, "OneNote export needs a DPAPI-saved OAuth access token.");
        }

        await using var fileStream = File.OpenRead(request.FilePath);
        using var content = new MultipartFormDataContent();
        using var presentation = new StringContent(BuildPageHtml(request), Encoding.UTF8, "text/html");
        content.Add(presentation, "Presentation");

        using var fileContent = new StreamContent(fileStream);
        var contentType = ShareProviderPayloads.DetectContentType(request.FilePath);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", FileName(request));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildCreatePageUri())
        {
            Content = content
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var pageUrl = response.IsSuccessStatusCode ? ExtractPageUrl(body) : null;
        if (!string.IsNullOrWhiteSpace(pageUrl))
        {
            ClipboardInterop.SetText(pageUrl);
        }

        return new ShareUploadResult(
            response.IsSuccessStatusCode,
            pageUrl,
            response.IsSuccessStatusCode
                ? string.IsNullOrWhiteSpace(pageUrl)
                    ? $"OneNote page created: {(int)response.StatusCode} {response.ReasonPhrase}"
                    : $"OneNote page created and copied URL: {pageUrl}"
                : $"OneNote page creation failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
    }

    private Uri BuildCreatePageUri()
    {
        var baseUrl = NormalizeHttpBaseUrl(string.IsNullOrWhiteSpace(_settings.OneNoteGraphApiBaseUrl)
            ? "https://graph.microsoft.com/v1.0"
            : _settings.OneNoteGraphApiBaseUrl);
        var builder = new UriBuilder(baseUrl);
        builder.Path = string.IsNullOrWhiteSpace(_settings.OneNoteSectionId)
            ? CombineUriPath(builder.Path, "me", "onenote", "pages")
            : CombineUriPath(builder.Path, "me", "onenote", "sections", EncodePathSegment(_settings.OneNoteSectionId.Trim()), "pages");
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private string BuildPageHtml(ShareUploadRequest request)
    {
        var title = ShareProviderPayloads.ExpandMessage(_settings.OneNotePageTitleTemplate, request, ProviderName);
        var contentType = ShareProviderPayloads.DetectContentType(request.FilePath);
        var attachmentMarkup = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? $"<img src=\"name:file\" alt=\"{EscapeHtml(FileName(request))}\" />"
            : $"<object data-attachment=\"{EscapeHtml(FileName(request))}\" data=\"name:file\" type=\"{EscapeHtml(contentType)}\"></object>";

        return $"""
<!DOCTYPE html>
<html>
  <head>
    <title>{EscapeHtml(title)}</title>
    <meta name="created" content="{EscapeHtml(MetadataValue(request, "createdAt"))}" />
  </head>
  <body>
    <h1>{EscapeHtml(title)}</h1>
    <p>Created from GoatShot.</p>
    <ul>
      <li>Capture ID: {EscapeHtml(MetadataValue(request, "id"))}</li>
      <li>Capture type: {EscapeHtml(MetadataValue(request, "captureType", request.CaptureType))}</li>
      <li>File: {EscapeHtml(FileName(request))}</li>
      <li>Size: {EscapeHtml(MetadataValue(request, "bytes"))} bytes</li>
    </ul>
    {attachmentMarkup}
  </body>
</html>
""";
    }

    private static string? ExtractPageUrl(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.TryGetProperty("links", out var links) &&
                links.TryGetProperty("oneNoteWebUrl", out var oneNoteWebUrl) &&
                oneNoteWebUrl.TryGetProperty("href", out var href) &&
                href.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(href.GetString()))
            {
                return href.GetString();
            }

            if (root.TryGetProperty("webUrl", out var webUrl) &&
                webUrl.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(webUrl.GetString()))
            {
                return webUrl.GetString();
            }
        }
        catch (JsonException)
        {
            return ShareProviderPayloads.FirstUrl(responseBody);
        }

        return ShareProviderPayloads.FirstUrl(responseBody);
    }

    private static string NormalizeHttpBaseUrl(string value)
    {
        var baseUrl = value.Trim().TrimEnd('/');
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

    private static string FileName(ShareUploadRequest request)
    {
        return MetadataValue(request, "fileName", Path.GetFileName(request.FilePath));
    }

    private static string MetadataValue(ShareUploadRequest request, string key, string fallback = "")
    {
        return request.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private static string EscapeHtml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&#39;", StringComparison.Ordinal);
    }
}
