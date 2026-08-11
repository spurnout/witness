using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class WebDavShareProvider : IShareProvider
{
    private readonly AppSettings _settings;
    private readonly SecretStore _secretStore;
    private readonly HttpClient _httpClient;

    public WebDavShareProvider(AppSettings settings, SecretStore secretStore)
        : this(settings, secretStore, new HttpClient())
    {
    }

    public WebDavShareProvider(AppSettings settings, SecretStore secretStore, HttpClient httpClient)
    {
        _settings = settings;
        _secretStore = secretStore;
        _httpClient = httpClient;
    }

    public ShareDestination? Destination => ShareDestination.WebDav;
    public string ProviderName => "WebDAV";
    public string AuthType => "Basic";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => true;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.WebDavBaseUrl))
        {
            return Task.FromResult(new ProviderHealth(false, "WebDAV base URL is not configured."));
        }

        if (!UsesSecureTransport(_settings.WebDavBaseUrl))
        {
            return Task.FromResult(new ProviderHealth(false, "WebDAV requires HTTPS; plaintext HTTP is allowed only on loopback."));
        }

        if (!string.IsNullOrWhiteSpace(_settings.WebDavUsername) && !_secretStore.HasWebDavPassword)
        {
            return Task.FromResult(new ProviderHealth(false, "WebDAV username is configured but no DPAPI-saved password exists."));
        }

        return Task.FromResult(new ProviderHealth(true, "WebDAV is configured for local upload attempts."));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.WebDavBaseUrl))
        {
            return new ShareUploadResult(false, null, "WebDAV upload needs a base URL setting.");
        }

        if (!UsesSecureTransport(_settings.WebDavBaseUrl))
        {
            return new ShareUploadResult(false, null, "WebDAV requires HTTPS; plaintext HTTP is allowed only on loopback.");
        }

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return new ShareUploadResult(false, null, $"Source file does not exist: {request.FilePath}");
        }

        var password = _secretStore.ReadWebDavPassword();
        if (!string.IsNullOrWhiteSpace(_settings.WebDavUsername) && string.IsNullOrWhiteSpace(password))
        {
            return new ShareUploadResult(false, null, "WebDAV upload has a username configured but no DPAPI-saved password.");
        }

        var requestUri = BuildUploadUri(request);
        await using var fileStream = File.OpenRead(request.FilePath);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = new StreamContent(fileStream)
        };
        httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(DetectContentType(request.FilePath));
        if (!string.IsNullOrWhiteSpace(_settings.WebDavUsername))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.WebDavUsername.Trim()}:{password!.Trim()}"));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var shareUrl = response.IsSuccessStatusCode ? BuildPublicUrl(Path.GetFileName(requestUri.LocalPath)) : null;
        if (!string.IsNullOrWhiteSpace(shareUrl))
        {
            ClipboardInterop.SetText(shareUrl);
        }

        return new ShareUploadResult(
            response.IsSuccessStatusCode,
            shareUrl,
            response.IsSuccessStatusCode
                ? string.IsNullOrWhiteSpace(shareUrl)
                    ? $"WebDAV upload completed: {requestUri}"
                    : $"WebDAV upload completed and copied URL: {shareUrl}"
                : $"WebDAV upload failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
    }

    private Uri BuildUploadUri(ShareUploadRequest request)
    {
        var baseUrl = NormalizeHttpBaseUrl(_settings.WebDavBaseUrl, "WebDAV base URL");
        var builder = new UriBuilder(baseUrl);
        var basePath = builder.Path.Trim('/');
        var folder = NormalizeCloudFolderPath(_settings.WebDavRemoteDirectory);
        var fileName = EncodeCloudPathSegment(BuildRemoteFileName(request));
        builder.Path = CombineUriPath(basePath, folder, fileName);
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private string? BuildPublicUrl(string remoteFileName)
    {
        if (string.IsNullOrWhiteSpace(_settings.WebDavPublicBaseUrl))
        {
            return null;
        }

        var publicBaseUrl = NormalizeHttpBaseUrl(_settings.WebDavPublicBaseUrl, "WebDAV public base URL");
        var builder = new UriBuilder(publicBaseUrl);
        var basePath = builder.Path.Trim('/');
        builder.Path = CombineUriPath(basePath, EncodeCloudPathSegment(remoteFileName));
        builder.Query = string.Empty;
        return builder.Uri.ToString();
    }

    private static string BuildRemoteFileName(ShareUploadRequest request)
    {
        var fileName = MetadataValue(request, "fileName", Path.GetFileName(request.FilePath));
        var safeFileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return $"{MetadataValue(request, "id", Guid.NewGuid().ToString("N"))}-{safeFileName}";
    }

    private static string MetadataValue(ShareUploadRequest request, string key, string fallback)
    {
        return request.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private static string NormalizeHttpBaseUrl(string value, string settingName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{settingName} is not configured.");
        }

        var baseUrl = value.Trim();
        if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = $"https://{baseUrl}";
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || !UsesSecureTransport(uri))
        {
            throw new InvalidOperationException($"{settingName} must use HTTPS; plaintext HTTP is allowed only on loopback.");
        }

        return uri.ToString();
    }

    private static bool UsesSecureTransport(string value)
    {
        var normalized = value.Trim();
        if (!normalized.Contains("://", StringComparison.Ordinal))
        {
            normalized = $"https://{normalized}";
        }

        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) && UsesSecureTransport(uri);
    }

    private static bool UsesSecureTransport(Uri uri)
    {
        return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback);
    }

    private static string NormalizeCloudFolderPath(string? value)
    {
        var folder = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace('\\', '/').Trim().Trim('/');
        return string.Join(
            "/",
            folder
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(EncodeCloudPathSegment));
    }

    private static string CombineUriPath(params string?[] parts)
    {
        return string.Join(
            "/",
            parts
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim('/')));
    }

    private static string EncodeCloudPathSegment(string value)
    {
        return Uri.EscapeDataString(value)
            .Replace("%2D", "-", StringComparison.OrdinalIgnoreCase)
            .Replace("%2E", ".", StringComparison.OrdinalIgnoreCase)
            .Replace("%5F", "_", StringComparison.OrdinalIgnoreCase)
            .Replace("%7E", "~", StringComparison.OrdinalIgnoreCase);
    }

    private static string DetectContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            ".avi" => "video/x-msvideo",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".json" => "application/json",
            _ => "application/octet-stream"
        };
    }
}
