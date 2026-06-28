using System.Diagnostics;
using System.Globalization;
using System.Text;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class SftpShareProvider : IShareProvider
{
    private readonly AppPaths _paths;
    private readonly AppSettings _settings;
    private readonly ISftpProcessRunner _processRunner;

    public SftpShareProvider(AppPaths paths, AppSettings settings)
        : this(paths, settings, new OpenSshSftpProcessRunner())
    {
    }

    public SftpShareProvider(AppPaths paths, AppSettings settings, ISftpProcessRunner processRunner)
    {
        _paths = paths;
        _settings = settings;
        _processRunner = processRunner;
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

        if (string.IsNullOrWhiteSpace(_settings.SftpHost) ||
            string.IsNullOrWhiteSpace(_settings.SftpUsername))
        {
            return Task.FromResult(new ProviderHealth(false, "SFTP host and username are not configured."));
        }

        if (FindSftpExecutable(_settings.SftpExecutablePath) is null)
        {
            return Task.FromResult(new ProviderHealth(false, "OpenSSH sftp.exe was not found on PATH, GOATSHOT_SFTP_PATH, or the configured SFTP executable path."));
        }

        var privateKeyPath = ExpandOptionalPath(_settings.SftpPrivateKeyPath);
        if (!string.IsNullOrWhiteSpace(privateKeyPath) && !File.Exists(privateKeyPath))
        {
            return Task.FromResult(new ProviderHealth(false, $"SFTP private key was configured but not found: {privateKeyPath}"));
        }

        return Task.FromResult(new ProviderHealth(true, "SFTP is configured for local OpenSSH upload attempts."));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.SftpHost) ||
            string.IsNullOrWhiteSpace(_settings.SftpUsername))
        {
            return new ShareUploadResult(false, null, "SFTP upload needs host and username settings.");
        }

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return new ShareUploadResult(false, null, $"Source file does not exist: {request.FilePath}");
        }

        var sftp = FindSftpExecutable(_settings.SftpExecutablePath);
        if (string.IsNullOrWhiteSpace(sftp))
        {
            return new ShareUploadResult(false, null, "SFTP upload needs OpenSSH sftp.exe on PATH, GOATSHOT_SFTP_PATH, or the configured SFTP executable path.");
        }

        var privateKeyPath = ExpandOptionalPath(_settings.SftpPrivateKeyPath);
        if (!string.IsNullOrWhiteSpace(privateKeyPath) && !File.Exists(privateKeyPath))
        {
            return new ShareUploadResult(false, null, $"SFTP private key was configured but not found: {privateKeyPath}");
        }

        Directory.CreateDirectory(_paths.TempRoot);
        var batchPath = Path.Combine(_paths.TempRoot, $"sftp-upload-{MetadataValue(request, "id", Guid.NewGuid().ToString("N"))}.txt");
        var port = _settings.SftpPort <= 0 ? 22 : _settings.SftpPort;
        var remoteDirectory = NormalizeRemoteDirectory(_settings.SftpRemoteDirectory);
        var remoteFileName = BuildRemoteFileName(request);
        var remotePath = CombineRemotePath(remoteDirectory, remoteFileName);
        var target = $"{_settings.SftpUsername.Trim()}@{_settings.SftpHost.Trim()}";
        var batchLines = BuildBatchLines(request.FilePath, remoteDirectory, remotePath);

        await File.WriteAllLinesAsync(batchPath, batchLines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);

        try
        {
            var arguments = BuildArguments(batchPath, port, privateKeyPath, target);
            var result = await _processRunner.RunAsync(
                new SftpProcessRequest(sftp, arguments, batchPath, target, remotePath),
                cancellationToken);

            if (!result.Started)
            {
                return new ShareUploadResult(false, null, "SFTP upload could not start sftp.exe.");
            }

            var shareUrl = result.ExitCode == 0 ? BuildPublicUrl(remoteFileName) : null;
            if (!string.IsNullOrWhiteSpace(shareUrl))
            {
                ClipboardInterop.SetText(shareUrl);
            }

            return new ShareUploadResult(
                result.ExitCode == 0,
                shareUrl,
                result.ExitCode == 0
                    ? string.IsNullOrWhiteSpace(shareUrl)
                        ? $"SFTP upload completed: {target}:{remotePath}"
                        : $"SFTP upload completed and copied URL: {shareUrl}"
                    : $"SFTP upload failed with exit code {result.ExitCode}: {ShortProcessOutput(result.StandardError, result.StandardOutput)}");
        }
        finally
        {
            TryDelete(batchPath);
        }
    }

    internal static string? FindSftpExecutable(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
            return File.Exists(expanded) ? expanded : null;
        }

        var environmentPath = Environment.GetEnvironmentVariable("GOATSHOT_SFTP_PATH");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(environmentPath.Trim());
            return File.Exists(expanded) ? expanded : null;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "sftp.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        var systemCandidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "OpenSSH",
            "sftp.exe");
        return File.Exists(systemCandidate) ? systemCandidate : null;
    }

    private static string[] BuildBatchLines(string filePath, string remoteDirectory, string remotePath)
    {
        var localPath = filePath.Replace('\\', '/');
        return remoteDirectory == "/"
            ? new[]
            {
                $"put {QuoteSftpPath(localPath)} {QuoteSftpPath(remotePath)}"
            }
            : new[]
            {
                $"-mkdir {QuoteSftpPath(remoteDirectory)}",
                $"put {QuoteSftpPath(localPath)} {QuoteSftpPath(remotePath)}"
            };
    }

    private static IReadOnlyList<string> BuildArguments(
        string batchPath,
        int port,
        string? privateKeyPath,
        string target)
    {
        var arguments = new List<string>
        {
            "-b",
            batchPath,
            "-P",
            port.ToString(CultureInfo.InvariantCulture),
            "-o",
            "BatchMode=yes",
            "-o",
            "ConnectTimeout=15"
        };

        if (!string.IsNullOrWhiteSpace(privateKeyPath))
        {
            arguments.Add("-i");
            arguments.Add(privateKeyPath);
        }

        arguments.Add(target);
        return arguments;
    }

    private static string BuildRemoteFileName(ShareUploadRequest request)
    {
        var fileName = MetadataValue(request, "fileName", Path.GetFileName(request.FilePath));
        var safeFileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return $"{MetadataValue(request, "id", Guid.NewGuid().ToString("N"))}-{safeFileName}";
    }

    private static string NormalizeRemoteDirectory(string? value)
    {
        var directory = string.IsNullOrWhiteSpace(value)
            ? "/"
            : value.Replace('\\', '/').Trim();

        if (directory.Length == 0 || directory == ".")
        {
            return ".";
        }

        if (directory == "/")
        {
            return "/";
        }

        return directory.TrimEnd('/');
    }

    private static string CombineRemotePath(string directory, string fileName)
    {
        return directory is "" or "." or "/"
            ? directory == "/" ? $"/{fileName}" : fileName
            : $"{directory.TrimEnd('/')}/{fileName}";
    }

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

    private static string EncodeKey(string key)
    {
        return string.Join("/", key.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(EncodePathSegment));
    }

    private static string EncodePathSegment(string segment)
    {
        return Uri.EscapeDataString(segment)
            .Replace("%2D", "-", StringComparison.OrdinalIgnoreCase)
            .Replace("%2E", ".", StringComparison.OrdinalIgnoreCase)
            .Replace("%5F", "_", StringComparison.OrdinalIgnoreCase)
            .Replace("%7E", "~", StringComparison.OrdinalIgnoreCase);
    }

    private static string QuoteSftpPath(string path)
    {
        return $"\"{path.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string MetadataValue(ShareUploadRequest request, string key, string fallback)
    {
        return request.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private static string? ExpandOptionalPath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : Environment.ExpandEnvironmentVariables(path.Trim());
    }

    private static string ShortProcessOutput(string stderr, string stdout)
    {
        var text = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        text = text.ReplaceLineEndings(" ").Trim();
        return text.Length <= 360 ? text : text[..360] + "...";
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Temp batch cleanup is best-effort.
        }
    }
}

public sealed record SftpProcessRequest(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string BatchPath,
    string Target,
    string RemotePath);

public sealed record SftpProcessResult(
    bool Started,
    int ExitCode,
    string StandardOutput,
    string StandardError);

public interface ISftpProcessRunner
{
    Task<SftpProcessResult> RunAsync(SftpProcessRequest request, CancellationToken cancellationToken);
}

public sealed class OpenSshSftpProcessRunner : ISftpProcessRunner
{
    public async Task<SftpProcessResult> RunAsync(SftpProcessRequest request, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        foreach (var argument in request.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
        if (process is null)
        {
            return new SftpProcessResult(false, -1, string.Empty, string.Empty);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return new SftpProcessResult(true, process.ExitCode, stdout, stderr);
    }
}
