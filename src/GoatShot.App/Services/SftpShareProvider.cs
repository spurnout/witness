using GoatShot.App.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace GoatShot.App.Services;

public sealed class SftpShareProvider : IShareProvider
{
    private readonly AppSettings _settings;
    private readonly ISftpClientAdapter _client;

    public SftpShareProvider(AppPaths paths, AppSettings settings)
        : this(paths, settings, new SshNetSftpClientAdapter())
    {
    }

    public SftpShareProvider(AppPaths paths, AppSettings settings, ISftpClientAdapter client)
    {
        _ = paths;
        _settings = settings;
        _client = client;
    }

    public ShareDestination? Destination => ShareDestination.Sftp;
    public string ProviderName => "SFTP";
    public string AuthType => "SSH key";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => true;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var issue = ValidateConfiguration();
        return Task.FromResult(string.IsNullOrWhiteSpace(issue)
            ? new ProviderHealth(true, "SFTP is configured for in-process SSH.NET upload with a pinned host key.")
            : new ProviderHealth(false, issue));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var issue = ValidateConfiguration();
        if (!string.IsNullOrWhiteSpace(issue))
        {
            return new ShareUploadResult(false, null, issue);
        }

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return new ShareUploadResult(false, null, $"Source file does not exist: {request.FilePath}");
        }

        var remoteDirectory = NormalizeRemoteDirectory(_settings.SftpRemoteDirectory);
        var remoteFileName = BuildRemoteFileName(request);
        var remotePath = CombineRemotePath(remoteDirectory, remoteFileName);
        var result = await _client.UploadAsync(new SftpUploadRequest(
            _settings.SftpHost.Trim(),
            _settings.SftpPort <= 0 ? 22 : _settings.SftpPort,
            _settings.SftpUsername.Trim(),
            ExpandOptionalPath(_settings.SftpPrivateKeyPath)!,
            NormalizeFingerprint(_settings.SftpHostKeyFingerprint),
            request.FilePath,
            remoteDirectory,
            remotePath), cancellationToken);

        var shareUrl = result.Succeeded ? BuildPublicUrl(remoteFileName) : null;
        if (!string.IsNullOrWhiteSpace(shareUrl))
        {
            ClipboardInterop.SetText(shareUrl);
        }

        return new ShareUploadResult(
            result.Succeeded,
            shareUrl,
            result.Succeeded
                ? string.IsNullOrWhiteSpace(shareUrl)
                    ? $"SFTP upload completed: {_settings.SftpUsername}@{_settings.SftpHost}:{remotePath}"
                    : $"SFTP upload completed and copied URL: {shareUrl}"
                : $"SFTP upload failed: {SensitiveTextDetector.Redact(result.Message)}");
    }

    private string ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_settings.SftpHost) || string.IsNullOrWhiteSpace(_settings.SftpUsername))
        {
            return "SFTP host and username are not configured.";
        }

        var privateKeyPath = ExpandOptionalPath(_settings.SftpPrivateKeyPath);
        if (string.IsNullOrWhiteSpace(privateKeyPath))
        {
            return "SFTP private key path is required for the in-process client.";
        }

        if (!File.Exists(privateKeyPath))
        {
            return $"SFTP private key was configured but not found: {privateKeyPath}";
        }

        if (string.IsNullOrWhiteSpace(_settings.SftpHostKeyFingerprint))
        {
            return "SFTP host key SHA-256 fingerprint is required; GoatShot will not trust an unknown server key.";
        }

        return string.Empty;
    }

    private static string BuildRemoteFileName(ShareUploadRequest request)
    {
        var fileName = MetadataValue(request, "fileName", Path.GetFileName(request.FilePath));
        var safeFileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return $"{MetadataValue(request, "id", Guid.NewGuid().ToString("N"))}-{safeFileName}";
    }

    internal static string NormalizeRemoteDirectory(string? value)
    {
        var directory = string.IsNullOrWhiteSpace(value) ? "/" : value.Replace('\\', '/').Trim();
        return directory.Length == 0 || directory == "." ? "." : directory == "/" ? "/" : directory.TrimEnd('/');
    }

    internal static string CombineRemotePath(string directory, string fileName) =>
        directory is "" or "." or "/"
            ? directory == "/" ? $"/{fileName}" : fileName
            : $"{directory.TrimEnd('/')}/{fileName}";

    private string? BuildPublicUrl(string remoteFileName)
    {
        if (string.IsNullOrWhiteSpace(_settings.SftpPublicBaseUrl))
        {
            return null;
        }

        var publicBaseUrl = _settings.SftpPublicBaseUrl.Trim();
        if (!publicBaseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !publicBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            publicBaseUrl = $"https://{publicBaseUrl}";
        }

        var builder = new UriBuilder(publicBaseUrl);
        var basePath = builder.Path.Trim('/');
        builder.Path = string.IsNullOrWhiteSpace(basePath)
            ? EncodeKey(remoteFileName)
            : $"{EncodeKey(basePath)}/{EncodeKey(remoteFileName)}";
        builder.Query = string.Empty;
        return builder.Uri.ToString();
    }

    private static string EncodeKey(string key) =>
        string.Join("/", key.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

    private static string MetadataValue(ShareUploadRequest request, string key, string fallback) =>
        request.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static string? ExpandOptionalPath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));

    internal static string NormalizeFingerprint(string value) =>
        value.Trim().Replace("SHA256:", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(":", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
}

public sealed record SftpUploadRequest(
    string Host,
    int Port,
    string Username,
    string PrivateKeyPath,
    string HostKeySha256,
    string LocalPath,
    string RemoteDirectory,
    string RemotePath);

public sealed record SftpUploadResult(bool Succeeded, string Message);

public interface ISftpClientAdapter
{
    Task<SftpUploadResult> UploadAsync(SftpUploadRequest request, CancellationToken cancellationToken);
}

public sealed class SshNetSftpClientAdapter : ISftpClientAdapter
{
    public async Task<SftpUploadResult> UploadAsync(SftpUploadRequest request, CancellationToken cancellationToken)
    {
        return await Task.Run(() => Upload(request, cancellationToken), cancellationToken);
    }

    private static SftpUploadResult Upload(SftpUploadRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var key = new PrivateKeyFile(request.PrivateKeyPath);
            var connection = new ConnectionInfo(
                request.Host,
                request.Port,
                request.Username,
                new PrivateKeyAuthenticationMethod(request.Username, key))
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            using var client = new SftpClient(connection);
            var hostKeyAccepted = false;
            client.HostKeyReceived += (_, args) =>
            {
                var received = SftpShareProvider.NormalizeFingerprint(args.FingerPrintSHA256);
                hostKeyAccepted = received.Equals(request.HostKeySha256, StringComparison.OrdinalIgnoreCase);
                args.CanTrust = hostKeyAccepted;
            };
            cancellationToken.Register(client.Dispose);
            client.Connect();
            if (!hostKeyAccepted)
            {
                return new SftpUploadResult(false, "Host key verification failed.");
            }

            EnsureRemoteDirectory(client, request.RemoteDirectory);
            using var input = File.OpenRead(request.LocalPath);
            client.UploadFile(input, request.RemotePath, canOverride: true);
            client.Disconnect();
            return new SftpUploadResult(true, "Upload completed.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SshException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new SftpUploadResult(false, exception.Message);
        }
    }

    private static void EnsureRemoteDirectory(SftpClient client, string directory)
    {
        if (directory is "" or "." or "/")
        {
            return;
        }

        var current = directory.StartsWith('/') ? "/" : string.Empty;
        foreach (var segment in directory.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current == "/" ? $"/{segment}" : string.IsNullOrEmpty(current) ? segment : $"{current}/{segment}";
            if (!client.Exists(current))
            {
                client.CreateDirectory(current);
            }
        }
    }
}
