using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class GooglePhotosShareProvider : IShareProvider
{
    private readonly AppSettings _settings;
    private readonly SecretStore _secretStore;
    private readonly HttpClient _httpClient;

    public GooglePhotosShareProvider(AppSettings settings, SecretStore secretStore)
        : this(settings, secretStore, new HttpClient())
    {
    }

    public GooglePhotosShareProvider(AppSettings settings, SecretStore secretStore, HttpClient httpClient)
    {
        _settings = settings;
        _secretStore = secretStore;
        _httpClient = httpClient;
    }

    public ShareDestination? Destination => ShareDestination.GooglePhotos;
    public string ProviderName => "Google Photos";
    public string AuthType => "OAuth";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => true;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_secretStore.ReadGooglePhotosAccessToken()))
        {
            return Task.FromResult(new ProviderHealth(false, "Google Photos OAuth access token is not saved with Windows DPAPI."));
        }

        return Task.FromResult(new ProviderHealth(true, "Google Photos is configured for local media upload attempts."));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return new ShareUploadResult(false, null, $"Source file does not exist: {request.FilePath}");
        }

        var contentType = ShareProviderPayloads.DetectContentType(request.FilePath);
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
            !contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return new ShareUploadResult(false, null, "Google Photos upload supports image and video files only.");
        }

        var token = _secretStore.ReadGooglePhotosAccessToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return new ShareUploadResult(false, null, "Google Photos upload needs a DPAPI-saved OAuth access token.");
        }

        var uploadTokenResult = await UploadBytesAsync(request, contentType, token.Trim(), cancellationToken);
        if (!uploadTokenResult.Succeeded)
        {
            return new ShareUploadResult(false, null, uploadTokenResult.Message);
        }

        var createResult = await CreateMediaItemAsync(request, uploadTokenResult.UploadToken!, token.Trim(), cancellationToken);
        if (!string.IsNullOrWhiteSpace(createResult.ShareUrl))
        {
            ClipboardInterop.SetText(createResult.ShareUrl);
        }

        return createResult;
    }

    private async Task<(bool Succeeded, string? UploadToken, string Message)> UploadBytesAsync(
        ShareUploadRequest request,
        string contentType,
        string accessToken,
        CancellationToken cancellationToken)
    {
        await using var fileStream = File.OpenRead(request.FilePath);
        using var content = new StreamContent(fileStream);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUploadUri())
        {
            Content = content
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        httpRequest.Headers.Add("X-Goog-Upload-File-Name", FileName(request));
        httpRequest.Headers.Add("X-Goog-Upload-Protocol", "raw");

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (false, null, $"Google Photos media upload failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
        }

        var uploadToken = body.Trim();
        return string.IsNullOrWhiteSpace(uploadToken)
            ? (false, null, "Google Photos media upload did not return an upload token.")
            : (true, uploadToken, "Google Photos media bytes uploaded.");
    }

    private async Task<ShareUploadResult> CreateMediaItemAsync(
        ShareUploadRequest request,
        string uploadToken,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var content = ShareProviderPayloads.JsonContent(BuildCreatePayload(request, uploadToken));
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildCreateMediaItemUri())
        {
            Content = content
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var mediaUrl = response.IsSuccessStatusCode ? ExtractMediaUrl(body) : null;

        return new ShareUploadResult(
            response.IsSuccessStatusCode,
            mediaUrl,
            response.IsSuccessStatusCode
                ? string.IsNullOrWhiteSpace(mediaUrl)
                    ? $"Google Photos media item created: {(int)response.StatusCode} {response.ReasonPhrase}"
                    : $"Google Photos media item created and copied URL: {mediaUrl}"
                : $"Google Photos media item creation failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
    }

    private object BuildCreatePayload(ShareUploadRequest request, string uploadToken)
    {
        var mediaItem = new
        {
            description = ShareProviderPayloads.ExpandMessage(_settings.GooglePhotosDescriptionTemplate, request, ProviderName),
            simpleMediaItem = new
            {
                uploadToken
            }
        };

        return string.IsNullOrWhiteSpace(_settings.GooglePhotosAlbumId)
            ? new
            {
                newMediaItems = new[] { mediaItem }
            }
            : new
            {
                albumId = _settings.GooglePhotosAlbumId.Trim(),
                newMediaItems = new[] { mediaItem }
            };
    }

    private Uri BuildUploadUri()
    {
        var baseUrl = NormalizeHttpBaseUrl(string.IsNullOrWhiteSpace(_settings.GooglePhotosUploadApiBaseUrl)
            ? "https://photoslibrary.googleapis.com/v1/uploads"
            : _settings.GooglePhotosUploadApiBaseUrl);
        var builder = new UriBuilder(baseUrl);
        builder.Path = EnsureUploadsPath(builder.Path);
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private Uri BuildCreateMediaItemUri()
    {
        var baseUrl = NormalizeHttpBaseUrl(string.IsNullOrWhiteSpace(_settings.GooglePhotosApiBaseUrl)
            ? "https://photoslibrary.googleapis.com/v1"
            : _settings.GooglePhotosApiBaseUrl);
        var builder = new UriBuilder(baseUrl);
        builder.Path = EnsureBatchCreatePath(builder.Path);
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private static string? ExtractMediaUrl(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.TryGetProperty("newMediaItemResults", out var results) &&
                results.ValueKind == JsonValueKind.Array)
            {
                foreach (var result in results.EnumerateArray())
                {
                    if (result.TryGetProperty("mediaItem", out var mediaItem) &&
                        mediaItem.TryGetProperty("productUrl", out var productUrl) &&
                        productUrl.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(productUrl.GetString()))
                    {
                        return productUrl.GetString();
                    }
                }
            }

            if (root.TryGetProperty("productUrl", out var rootProductUrl) &&
                rootProductUrl.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(rootProductUrl.GetString()))
            {
                return rootProductUrl.GetString();
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

    private static string EnsureUploadsPath(string path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim('/');
        return normalized.EndsWith("uploads", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : CombineUriPath(path, "uploads");
    }

    private static string EnsureBatchCreatePath(string path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim('/');
        return normalized.EndsWith("mediaItems:batchCreate", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : CombineUriPath(path, "mediaItems:batchCreate");
    }

    private static string CombineUriPath(params string?[] parts)
    {
        return string.Join(
            "/",
            parts
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim('/')));
    }

    private static string FileName(ShareUploadRequest request)
    {
        return request.Metadata.TryGetValue("fileName", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : Path.GetFileName(request.FilePath);
    }
}
