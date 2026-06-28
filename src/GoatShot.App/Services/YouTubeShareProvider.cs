using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class YouTubeShareProvider : IShareProvider
{
    private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".mov",
        ".m4v",
        ".webm",
        ".avi",
        ".wmv",
        ".mpeg",
        ".mpg"
    };

    private readonly AppSettings _settings;
    private readonly SecretStore _secretStore;
    private readonly HttpClient _httpClient;

    public YouTubeShareProvider(AppSettings settings, SecretStore secretStore)
        : this(settings, secretStore, new HttpClient())
    {
    }

    public YouTubeShareProvider(AppSettings settings, SecretStore secretStore, HttpClient httpClient)
    {
        _settings = settings;
        _secretStore = secretStore;
        _httpClient = httpClient;
    }

    public ShareDestination? Destination => ShareDestination.YouTube;
    public string ProviderName => "YouTube";
    public string AuthType => "OAuth";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => true;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_secretStore.ReadYouTubeAccessToken()))
        {
            return Task.FromResult(new ProviderHealth(false, "YouTube OAuth access token is not saved with Windows DPAPI."));
        }

        return Task.FromResult(new ProviderHealth(true, "YouTube is configured for local video upload attempts."));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!SupportedVideoExtensions.Contains(Path.GetExtension(request.FilePath)))
        {
            return new ShareUploadResult(false, null, "YouTube upload supports video files only: mp4, mov, m4v, webm, avi, wmv, mpeg, or mpg.");
        }

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return new ShareUploadResult(false, null, $"Source file does not exist: {request.FilePath}");
        }

        var token = _secretStore.ReadYouTubeAccessToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return new ShareUploadResult(false, null, "YouTube upload needs a DPAPI-saved OAuth access token.");
        }

        await using var fileStream = File.OpenRead(request.FilePath);
        using var content = new MultipartContent("related");
        content.Add(ShareProviderPayloads.JsonContent(new
        {
            snippet = new
            {
                title = ShareProviderPayloads.ExpandMessage(_settings.YouTubeTitleTemplate, request, ProviderName),
                description = ShareProviderPayloads.ExpandMessage(_settings.YouTubeDescriptionTemplate, request, ProviderName),
                categoryId = NormalizeCategoryId(_settings.YouTubeCategoryId)
            },
            status = new
            {
                privacyStatus = NormalizePrivacyStatus(_settings.YouTubePrivacyStatus)
            }
        }));

        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(ShareProviderPayloads.DetectContentType(request.FilePath));
        content.Add(fileContent);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUploadUri())
        {
            Content = content
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var videoUrl = response.IsSuccessStatusCode ? ExtractVideoUrl(body) : null;
        if (!string.IsNullOrWhiteSpace(videoUrl))
        {
            ClipboardInterop.SetText(videoUrl);
        }

        return new ShareUploadResult(
            response.IsSuccessStatusCode,
            videoUrl,
            response.IsSuccessStatusCode
                ? string.IsNullOrWhiteSpace(videoUrl)
                    ? $"YouTube upload accepted: {(int)response.StatusCode} {response.ReasonPhrase}"
                    : $"YouTube upload accepted and copied URL: {videoUrl}"
                : $"YouTube upload failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
    }

    private Uri BuildUploadUri()
    {
        var baseUrl = NormalizeHttpBaseUrl(string.IsNullOrWhiteSpace(_settings.YouTubeUploadApiBaseUrl)
            ? "https://www.googleapis.com/upload/youtube/v3"
            : _settings.YouTubeUploadApiBaseUrl);
        var builder = new UriBuilder(baseUrl);
        builder.Path = EnsureVideosPath(builder.Path);
        builder.Query = "uploadType=multipart&part=snippet,status";
        return builder.Uri;
    }

    private static string NormalizePrivacyStatus(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "public" => "public",
            "private" => "private",
            _ => "unlisted"
        };
    }

    private static string NormalizeCategoryId(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "22" : value.Trim();
    }

    private static string? ExtractVideoUrl(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(id.GetString()))
            {
                return $"https://youtu.be/{id.GetString()}";
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

    private static string EnsureVideosPath(string path)
    {
        var segments = path
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length > 0 &&
            segments[^1].Equals("videos", StringComparison.OrdinalIgnoreCase)
                ? string.Join("/", segments)
                : CombineUriPath(path, "videos");
    }
}
