using System.Net.Http;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class MicrosoftTeamsWebhookShareProvider : IShareProvider
{
    private readonly AppSettings _settings;
    private readonly HttpClient _httpClient;

    public MicrosoftTeamsWebhookShareProvider(AppSettings settings)
        : this(settings, new HttpClient())
    {
    }

    public MicrosoftTeamsWebhookShareProvider(AppSettings settings, HttpClient httpClient)
    {
        _settings = settings;
        _httpClient = httpClient;
    }

    public ShareDestination? Destination => ShareDestination.MicrosoftTeamsWebhook;
    public string ProviderName => "Microsoft Teams";
    public string AuthType => "Incoming webhook";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => false;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(string.IsNullOrWhiteSpace(_settings.TeamsWebhookUrl)
            ? new ProviderHealth(false, "Microsoft Teams webhook URL is not configured.")
            : new ProviderHealth(true, "Microsoft Teams incoming webhook is configured for local notification attempts."));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.TeamsWebhookUrl))
        {
            return new ShareUploadResult(false, null, "Microsoft Teams webhook share needs a configured incoming webhook URL.");
        }

        var text = ShareProviderPayloads.ExpandMessage(_settings.TeamsMessageTemplate, request, ProviderName);
        using var content = ShareProviderPayloads.JsonContent(new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = new
                    {
                        schema = "http://adaptivecards.io/schemas/adaptive-card.json",
                        type = "AdaptiveCard",
                        version = "1.4",
                        body = new object[]
                        {
                            new
                            {
                                type = "TextBlock",
                                text,
                                wrap = true
                            }
                        }
                    }
                }
            }
        });
        using var response = await _httpClient.PostAsync(_settings.TeamsWebhookUrl.Trim(), content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return new ShareUploadResult(
            response.IsSuccessStatusCode,
            null,
            response.IsSuccessStatusCode
                ? "Microsoft Teams webhook notification sent. Incoming webhooks do not upload local files; use WebDAV, FTP/FTPS, or a file provider for media transfer."
                : $"Microsoft Teams webhook notification failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
    }
}
