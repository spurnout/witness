using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class CloudinaryShareProvider : IShareProvider
{
    private readonly AppSettings _settings;
    private readonly SecretStore _secretStore;
    private readonly HttpClient _httpClient;

    public CloudinaryShareProvider(AppSettings settings, SecretStore secretStore)
        : this(settings, secretStore, new HttpClient())
    {
    }

    public CloudinaryShareProvider(AppSettings settings, SecretStore secretStore, HttpClient httpClient)
    {
        _settings = settings;
        _secretStore = secretStore;
        _httpClient = httpClient;
    }

    public ShareDestination? Destination => ShareDestination.Cloudinary;
    public string ProviderName => "Cloudinary";
    public string AuthType => "Upload preset or API key";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => true;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.CloudinaryCloudName))
        {
            return Task.FromResult(new ProviderHealth(false, "Cloudinary cloud name is not configured."));
        }

        if (_secretStore.ReadCloudinaryCredentials() is null &&
            string.IsNullOrWhiteSpace(_settings.CloudinaryUploadPreset))
        {
            return Task.FromResult(new ProviderHealth(false, "Cloudinary needs an upload preset or DPAPI-saved API credentials."));
        }

        return Task.FromResult(new ProviderHealth(true, "Cloudinary is configured for local upload attempts."));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.CloudinaryCloudName))
        {
            return new ShareUploadResult(false, null, "Cloudinary upload needs a cloud name setting.");
        }

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return new ShareUploadResult(false, null, $"Source file does not exist: {request.FilePath}");
        }

        var credentials = _secretStore.ReadCloudinaryCredentials();
        if (credentials is null && string.IsNullOrWhiteSpace(_settings.CloudinaryUploadPreset))
        {
            return new ShareUploadResult(false, null, "Cloudinary unsigned upload needs an upload preset, or save API key/secret credentials for authenticated upload.");
        }

        await using var fileStream = File.OpenRead(request.FilePath);
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", FileName(request));
        if (!string.IsNullOrWhiteSpace(_settings.CloudinaryUploadPreset))
        {
            content.Add(new StringContent(_settings.CloudinaryUploadPreset.Trim()), "upload_preset");
        }

        if (!string.IsNullOrWhiteSpace(_settings.CloudinaryFolder))
        {
            content.Add(new StringContent(_settings.CloudinaryFolder.Trim().Trim('/')), "folder");
        }

        content.Add(new StringContent($"goatshot_{MetadataValue(request, "id", Guid.NewGuid().ToString("N"))}"), "public_id");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUploadUri())
        {
            Content = content
        };
        if (credentials is not null)
        {
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.ApiKey}:{credentials.ApiSecret}"));
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var shareUrl = response.IsSuccessStatusCode ? ExtractUrl(body) : null;
        if (!string.IsNullOrWhiteSpace(shareUrl))
        {
            ClipboardInterop.SetText(shareUrl);
        }

        return new ShareUploadResult(
            response.IsSuccessStatusCode,
            shareUrl,
            response.IsSuccessStatusCode
                ? string.IsNullOrWhiteSpace(shareUrl)
                    ? $"Cloudinary upload completed: {(int)response.StatusCode} {response.ReasonPhrase}"
                    : $"Cloudinary upload completed and copied URL: {shareUrl}"
                : $"Cloudinary upload failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
    }

    private Uri BuildUploadUri()
    {
        var cloudName = Uri.EscapeDataString(_settings.CloudinaryCloudName.Trim());
        var resourceType = string.IsNullOrWhiteSpace(_settings.CloudinaryResourceType)
            ? "auto"
            : _settings.CloudinaryResourceType.Trim().ToLowerInvariant();
        var baseUrl = string.IsNullOrWhiteSpace(_settings.CloudinaryApiBaseUrl)
            ? "https://api.cloudinary.com/v1_1"
            : NormalizeHttpBaseUrl(_settings.CloudinaryApiBaseUrl);
        var builder = new UriBuilder(baseUrl);
        var basePath = builder.Path.Trim('/');
        builder.Path = string.IsNullOrWhiteSpace(basePath)
            ? $"{cloudName}/{resourceType}/upload"
            : $"{basePath}/{cloudName}/{resourceType}/upload";
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private static string? ExtractUrl(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            foreach (var property in new[] { "secure_url", "url", "asset_url" })
            {
                if (root.TryGetProperty(property, out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString();
                }
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
