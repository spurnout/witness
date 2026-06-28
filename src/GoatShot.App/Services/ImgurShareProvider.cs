using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class ImgurShareProvider : IShareProvider
{
    private readonly AppSettings _settings;
    private readonly SecretStore _secretStore;
    private readonly HttpClient _httpClient;

    public ImgurShareProvider(AppSettings settings, SecretStore secretStore)
        : this(settings, secretStore, new HttpClient())
    {
    }

    public ImgurShareProvider(AppSettings settings, SecretStore secretStore, HttpClient httpClient)
    {
        _settings = settings;
        _secretStore = secretStore;
        _httpClient = httpClient;
    }

    public ShareDestination? Destination => ShareDestination.Imgur;
    public string ProviderName => "Imgur";
    public string AuthType => "Client ID";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => true;
    public bool SupportsPrivateLinks => false;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_secretStore.ReadImgurClientId()))
        {
            return Task.FromResult(new ProviderHealth(false, "Imgur DPAPI-saved Client ID is missing."));
        }

        return Task.FromResult(new ProviderHealth(true, "Imgur Client ID is configured for local upload attempts."));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsSupportedImage(request.FilePath))
        {
            return new ShareUploadResult(false, null, "Imgur upload currently supports image files: png, jpg, jpeg, gif, bmp, or webp.");
        }

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return new ShareUploadResult(false, null, $"Source file does not exist: {request.FilePath}");
        }

        var clientId = _secretStore.ReadImgurClientId();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return new ShareUploadResult(false, null, "Imgur upload needs a DPAPI-saved Client ID.");
        }

        var endpoint = NormalizeHttpBaseUrl(string.IsNullOrWhiteSpace(_settings.ImgurApiEndpoint)
            ? "https://api.imgur.com/3/image"
            : _settings.ImgurApiEndpoint);

        await using var fileStream = File.OpenRead(request.FilePath);
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "image", FileName(request));
        content.Add(new StringContent("file"), "type");
        content.Add(new StringContent(Path.GetFileNameWithoutExtension(FileName(request))), "title");
        content.Add(new StringContent($"GoatShot capture {MetadataValue(request, "id", string.Empty)}"), "description");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = content
        };
        httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Client-ID {clientId.Trim()}");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var shareUrl = response.IsSuccessStatusCode ? ExtractLink(body) : null;
        if (!string.IsNullOrWhiteSpace(shareUrl))
        {
            ClipboardInterop.SetText(shareUrl);
        }

        return new ShareUploadResult(
            response.IsSuccessStatusCode,
            shareUrl,
            response.IsSuccessStatusCode
                ? string.IsNullOrWhiteSpace(shareUrl)
                    ? $"Imgur upload completed: {(int)response.StatusCode} {response.ReasonPhrase}"
                    : $"Imgur upload completed and copied URL: {shareUrl}"
                : $"Imgur upload failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
    }

    private static bool IsSupportedImage(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp";
    }

    private static string? ExtractLink(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.TryGetProperty("data", out var data) &&
                data.TryGetProperty("link", out var link) &&
                link.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(link.GetString()))
            {
                return link.GetString();
            }

            if (root.TryGetProperty("link", out var topLink) &&
                topLink.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(topLink.GetString()))
            {
                return topLink.GetString();
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
        var trimmed = value.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"https://{trimmed}";
    }

    private static string FileName(ShareUploadRequest request)
    {
        return MetadataValue(request, "fileName", Path.GetFileName(request.FilePath));
    }

    private static string MetadataValue(ShareUploadRequest request, string key, string fallback)
    {
        return request.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }
}
