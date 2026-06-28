using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class CustomWebhookShareProvider : IShareProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AppSettings _settings;
    private readonly HttpClient _httpClient;

    public CustomWebhookShareProvider(AppSettings settings)
        : this(settings, new HttpClient())
    {
    }

    public CustomWebhookShareProvider(AppSettings settings, HttpClient httpClient)
    {
        _settings = settings;
        _httpClient = httpClient;
    }

    public ShareDestination? Destination => ShareDestination.CustomWebhook;
    public string ProviderName => "Custom webhook";
    public string AuthType => "Webhook URL";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => true;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(string.IsNullOrWhiteSpace(_settings.CustomWebhookUrl)
            ? new ProviderHealth(false, "Custom webhook URL is not configured.")
            : new ProviderHealth(true, "Custom webhook URL is configured for local upload attempts."));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.CustomWebhookUrl))
        {
            return new ShareUploadResult(false, null, "No custom webhook URL is configured.");
        }

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return new ShareUploadResult(false, null, $"Source file does not exist: {request.FilePath}");
        }

        await using var fileStream = File.OpenRead(request.FilePath);
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", Path.GetFileName(request.FilePath));
        content.Add(
            new StringContent(JsonSerializer.Serialize(BuildWebhookMetadata(request), JsonOptions), Encoding.UTF8, "application/json"),
            "metadata");

        using var response = await _httpClient.PostAsync(_settings.CustomWebhookUrl.Trim(), content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var url = FirstUrl(body);
        if (!string.IsNullOrWhiteSpace(url))
        {
            ClipboardInterop.SetText(url);
        }

        return new ShareUploadResult(
            response.IsSuccessStatusCode,
            url,
            response.IsSuccessStatusCode
                ? string.IsNullOrWhiteSpace(url)
                    ? $"Webhook upload completed: {(int)response.StatusCode} {response.ReasonPhrase}"
                    : $"Webhook upload completed and copied URL: {url}"
                : $"Webhook upload failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
    }

    private static Dictionary<string, string> BuildWebhookMetadata(ShareUploadRequest request)
    {
        return new Dictionary<string, string>
        {
            ["id"] = MetadataValue(request, "id"),
            ["file"] = MetadataValue(request, "file", request.FilePath),
            ["captureType"] = MetadataValue(request, "captureType", request.CaptureType),
            ["createdAt"] = MetadataValue(request, "createdAt"),
            ["width"] = MetadataValue(request, "width"),
            ["height"] = MetadataValue(request, "height"),
            ["sourceApp"] = MetadataValue(request, "sourceApp"),
            ["bounds"] = MetadataValue(request, "bounds")
        };
    }

    private static string MetadataValue(ShareUploadRequest request, string key, string fallback = "")
    {
        return request.Metadata.TryGetValue(key, out var value) ? value : fallback;
    }

    private static string? FirstUrl(string text)
    {
        var match = Regex.Match(text, @"https?://[^\s""'<>]+");
        return match.Success ? match.Value : null;
    }
}
