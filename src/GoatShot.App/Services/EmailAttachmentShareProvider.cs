using System.Diagnostics;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class EmailAttachmentShareProvider : IShareProvider
{
    private readonly IEmailHandoffSurface _surface;

    public EmailAttachmentShareProvider()
        : this(SystemEmailHandoffSurface.Instance)
    {
    }

    internal EmailAttachmentShareProvider(IEmailHandoffSurface surface)
    {
        _surface = surface;
    }

    public ShareDestination? Destination => ShareDestination.EmailAttachment;
    public string ProviderName => "Email attachment";
    public string AuthType => "Mail client";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => false;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ProviderHealth(true, "Email handoff is available through the default Windows mail client; client availability is verified when launched."));
    }

    public Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            return Task.FromResult(new ShareUploadResult(false, null, "Email handoff needs a source file path."));
        }

        _surface.SetText(request.FilePath);
        var subject = Uri.EscapeDataString($"GoatShot capture: {Path.GetFileName(request.FilePath)}");
        var body = Uri.EscapeDataString($"Capture path copied to clipboard:%0D%0A{request.FilePath}");
        _surface.OpenMailto($"mailto:?subject={subject}&body={body}");
        return Task.FromResult(new ShareUploadResult(
            true,
            null,
            "Opened default email client and copied the capture path. File attachment automation is mail-client dependent."));
    }

    private sealed class SystemEmailHandoffSurface : IEmailHandoffSurface
    {
        public static readonly SystemEmailHandoffSurface Instance = new();

        private SystemEmailHandoffSurface()
        {
        }

        public void SetText(string text)
        {
            ClipboardInterop.SetText(text);
        }

        public void OpenMailto(string mailtoUri)
        {
            Process.Start(new ProcessStartInfo(mailtoUri) { UseShellExecute = true });
        }
    }
}

internal interface IEmailHandoffSurface
{
    void SetText(string text);
    void OpenMailto(string mailtoUri);
}
