using System.Net.Http;
using System.Net.Http.Headers;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class DiscordWebhookShareProvider : IShareProvider
{
    private readonly AppSettings _settings;
    private readonly HttpClient _httpClient;

    public DiscordWebhookShareProvider(AppSettings settings)
        : this(settings, new HttpClient())
    {
    }

    public DiscordWebhookShareProvider(AppSettings settings, HttpClient httpClient)
    {
        _settings = settings;
        _httpClient = httpClient;
    }

    public ShareDestination? Destination => ShareDestination.DiscordWebhook;
    public string ProviderName => "Discord";
    public string AuthType => "Webhook";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => true;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(string.IsNullOrWhiteSpace(_settings.DiscordWebhookUrl)
            ? new ProviderHealth(false, "Discord webhook URL is not configured.")
            : new ProviderHealth(true, "Discord webhook is configured for local upload attempts."));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.DiscordWebhookUrl))
        {
            return new ShareUploadResult(false, null, "Discord webhook upload needs a configured webhook URL.");
        }

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return new ShareUploadResult(false, null, $"Source file does not exist: {request.FilePath}");
        }

        var text = ShareProviderPayloads.ExpandMessage(_settings.DiscordMessageTemplate, request, ProviderName);
        await using var fileStream = File.OpenRead(request.FilePath);
        using var content = new MultipartFormDataContent();
        content.Add(ShareProviderPayloads.JsonContent(new { content = text }), "payload_json");
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(ShareProviderPayloads.DetectContentType(request.FilePath));
        content.Add(fileContent, "files[0]", Path.GetFileName(request.FilePath));

        using var response = await _httpClient.PostAsync(_settings.DiscordWebhookUrl.Trim(), content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var url = ShareProviderPayloads.FirstUrl(body);
        if (!string.IsNullOrWhiteSpace(url))
        {
            ClipboardInterop.SetText(url);
        }

        return new ShareUploadResult(
            response.IsSuccessStatusCode,
            url,
            response.IsSuccessStatusCode
                ? string.IsNullOrWhiteSpace(url)
                    ? $"Discord webhook upload completed: {(int)response.StatusCode} {response.ReasonPhrase}"
                    : $"Discord webhook upload completed and copied URL: {url}"
                : $"Discord webhook upload failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
    }
}
