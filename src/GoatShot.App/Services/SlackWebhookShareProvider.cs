using System.Net.Http;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class SlackWebhookShareProvider : IShareProvider
{
    private readonly AppSettings _settings;
    private readonly HttpClient _httpClient;

    public SlackWebhookShareProvider(AppSettings settings)
        : this(settings, new HttpClient())
    {
    }

    public SlackWebhookShareProvider(AppSettings settings, HttpClient httpClient)
    {
        _settings = settings;
        _httpClient = httpClient;
    }

    public ShareDestination? Destination => ShareDestination.SlackWebhook;
    public string ProviderName => "Slack";
    public string AuthType => "Incoming webhook";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => false;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(string.IsNullOrWhiteSpace(_settings.SlackWebhookUrl)
            ? new ProviderHealth(false, "Slack webhook URL is not configured.")
            : new ProviderHealth(true, "Slack incoming webhook is configured for local notification attempts."));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.SlackWebhookUrl))
        {
            return new ShareUploadResult(false, null, "Slack webhook share needs a configured incoming webhook URL.");
        }

        var text = ShareProviderPayloads.ExpandMessage(_settings.SlackMessageTemplate, request, ProviderName);
        using var content = ShareProviderPayloads.JsonContent(new
        {
            text,
            unfurl_links = false,
            unfurl_media = false
        });
        using var response = await _httpClient.PostAsync(_settings.SlackWebhookUrl.Trim(), content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return new ShareUploadResult(
            response.IsSuccessStatusCode,
            null,
            response.IsSuccessStatusCode
                ? "Slack webhook notification sent. Incoming webhooks do not upload local files; use Discord, WebDAV, FTP/FTPS, or a file provider for media transfer."
                : $"Slack webhook notification failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
    }
}
