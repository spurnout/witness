using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class ClipboardShareProvider : IShareProvider
{
    private readonly IClipboardShareSurface _surface;

    public ClipboardShareProvider(ShareDestination destination)
        : this(destination, SystemClipboardShareSurface.Instance)
    {
    }

    internal ClipboardShareProvider(ShareDestination destination, IClipboardShareSurface surface)
    {
        if (!IsClipboardDestination(destination))
        {
            throw new ArgumentOutOfRangeException(nameof(destination), destination, "Destination is not a clipboard share destination.");
        }

        Destination = destination;
        _surface = surface;
    }

    public ShareDestination? Destination { get; }
    public string ProviderName => Destination switch
    {
        ShareDestination.ClipboardImage => "Clipboard image",
        ShareDestination.ClipboardFile => "Clipboard file",
        ShareDestination.ClipboardPath => "Clipboard path",
        ShareDestination.MarkdownImageLink => "Markdown image link",
        _ => "Clipboard"
    };

    public string AuthType => "None";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => false;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ProviderHealth(true, $"{ProviderName} is ready; no provider credentials are required."));
    }

    public Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            return Task.FromResult(new ShareUploadResult(false, null, "Clipboard share needs a source file path."));
        }

        switch (Destination)
        {
            case ShareDestination.ClipboardImage:
                if (!File.Exists(request.FilePath))
                {
                    return Task.FromResult(new ShareUploadResult(false, null, $"Source file does not exist: {request.FilePath}"));
                }

                _surface.SetImage(request.FilePath);
                return Task.FromResult(new ShareUploadResult(true, null, "Image copied to clipboard."));

            case ShareDestination.ClipboardFile:
                if (!File.Exists(request.FilePath))
                {
                    return Task.FromResult(new ShareUploadResult(false, null, $"Source file does not exist: {request.FilePath}"));
                }

                _surface.SetFileDropList(new[] { request.FilePath });
                return Task.FromResult(new ShareUploadResult(true, null, "File copied to clipboard."));

            case ShareDestination.ClipboardPath:
                _surface.SetText(request.FilePath);
                return Task.FromResult(new ShareUploadResult(true, null, "Path copied to clipboard."));

            case ShareDestination.MarkdownImageLink:
                var escapedPath = request.FilePath.Replace("\\", "/", StringComparison.Ordinal);
                _surface.SetText($"![{Path.GetFileNameWithoutExtension(request.FilePath)}]({escapedPath})");
                return Task.FromResult(new ShareUploadResult(true, null, "Markdown image link copied to clipboard."));

            default:
                return Task.FromResult(new ShareUploadResult(false, null, $"Unsupported clipboard destination: {Destination}"));
        }
    }

    private static bool IsClipboardDestination(ShareDestination destination)
    {
        return destination is ShareDestination.ClipboardImage
            or ShareDestination.ClipboardFile
            or ShareDestination.ClipboardPath
            or ShareDestination.MarkdownImageLink;
    }

    private sealed class SystemClipboardShareSurface : IClipboardShareSurface
    {
        public static readonly SystemClipboardShareSurface Instance = new();

        private SystemClipboardShareSurface()
        {
        }

        public void SetText(string text)
        {
            ClipboardInterop.SetText(text);
        }

        public void SetImage(string filePath)
        {
            ClipboardInterop.SetImage(ImageInterop.LoadBitmapImage(filePath));
        }

        public void SetFileDropList(IEnumerable<string> paths)
        {
            ClipboardInterop.SetFileDropList(paths);
        }
    }
}

internal interface IClipboardShareSurface
{
    void SetText(string text);
    void SetImage(string filePath);
    void SetFileDropList(IEnumerable<string> paths);
}
