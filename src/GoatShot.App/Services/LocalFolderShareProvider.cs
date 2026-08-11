using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class LocalFolderShareProvider : IShareProvider
{
    private readonly AppSettings _settings;

    public LocalFolderShareProvider(AppSettings settings)
    {
        _settings = settings;
    }

    public ShareDestination? Destination => ShareDestination.LocalFolder;
    public string ProviderName => "Local folder";
    public string AuthType => "None";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => false;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var folder = ResolveExportFolder();
        var message = string.IsNullOrWhiteSpace(_settings.LocalExportFolder)
            ? $"Local folder export is ready; exports use the default folder: {folder}"
            : $"Local folder export is ready: {folder}";

        return Task.FromResult(new ProviderHealth(true, message));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return new ShareUploadResult(false, null, $"Source file does not exist: {request.FilePath}");
        }

        var folder = ResolveExportFolder();
        Directory.CreateDirectory(folder);

        var target = MakeUniquePath(Path.Combine(folder, Path.GetFileName(request.FilePath)));
        await using var input = File.OpenRead(request.FilePath);
        await using var output = File.Create(target);
        await input.CopyToAsync(output, cancellationToken);

        return new ShareUploadResult(true, null, $"Copied to local export folder: {target}");
    }

    private string ResolveExportFolder()
    {
        return string.IsNullOrWhiteSpace(_settings.LocalExportFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Receipts Exports")
            : Environment.ExpandEnvironmentVariables(_settings.LocalExportFolder);
    }

    private static string MakeUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 2; i < 10_000; i++)
        {
            var candidate = Path.Combine(directory, $"{name}-{i}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name}-{Guid.NewGuid():N}{extension}");
    }
}
