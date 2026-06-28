using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class CustomScriptShareProvider : IShareProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly AppSettings _settings;

    public CustomScriptShareProvider(AppPaths paths, AppSettings settings)
    {
        _paths = paths;
        _settings = settings;
    }

    public ShareDestination? Destination => ShareDestination.CustomScript;
    public string ProviderName => "Custom script";
    public string AuthType => "Local command";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => true;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(string.IsNullOrWhiteSpace(_settings.CustomScriptCommand)
            ? new ProviderHealth(false, "Custom script command is not configured.")
            : new ProviderHealth(true, "Custom script command is configured for local execution."));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.CustomScriptCommand))
        {
            return new ShareUploadResult(false, null, "No custom script command is configured.");
        }

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return new ShareUploadResult(false, null, $"Source file does not exist: {request.FilePath}");
        }

        var metadataPath = await WriteMetadataAsync(request, cancellationToken);
        var ocrPath = await WriteOcrTextAsync(request, cancellationToken);
        var command = ExpandScriptCommand(_settings.CustomScriptCommand, request, metadataPath, ocrPath);
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var url = FirstUrl(stdout);
        if (!string.IsNullOrWhiteSpace(url))
        {
            ClipboardInterop.SetText(url);
        }

        return new ShareUploadResult(
            process.ExitCode == 0,
            url,
            process.ExitCode == 0
                ? string.IsNullOrWhiteSpace(url)
                    ? "Custom script completed."
                    : $"Custom script completed and copied URL: {url}"
                : $"Custom script failed with exit code {process.ExitCode}: {stderr.Trim()}");
    }

    private async Task<string> WriteMetadataAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.TempRoot);
        var id = MetadataValue(request, "id", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(_paths.TempRoot, $"{id}-metadata.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(BuildMetadata(request), JsonOptions), cancellationToken);
        return path;
    }

    private async Task<string> WriteOcrTextAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.TempRoot);
        var id = MetadataValue(request, "id", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(_paths.TempRoot, $"{id}-ocr.txt");
        await File.WriteAllTextAsync(path, MetadataValue(request, "ocrText"), cancellationToken);
        return path;
    }

    private static Dictionary<string, string> BuildMetadata(ShareUploadRequest request)
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

    private static string ExpandScriptCommand(
        string template,
        ShareUploadRequest request,
        string metadataPath,
        string ocrPath)
    {
        return template
            .Replace("{file}", EscapePowerShellLiteral(request.FilePath), StringComparison.OrdinalIgnoreCase)
            .Replace("{metadata}", EscapePowerShellLiteral(metadataPath), StringComparison.OrdinalIgnoreCase)
            .Replace("{ocr}", EscapePowerShellLiteral(ocrPath), StringComparison.OrdinalIgnoreCase)
            .Replace("{id}", EscapePowerShellLiteral(MetadataValue(request, "id")), StringComparison.OrdinalIgnoreCase)
            .Replace("{capture_type}", EscapePowerShellLiteral(request.CaptureType), StringComparison.OrdinalIgnoreCase);
    }

    private static string MetadataValue(ShareUploadRequest request, string key, string fallback = "")
    {
        return request.Metadata.TryGetValue(key, out var value) ? value : fallback;
    }

    private static string EscapePowerShellLiteral(string value)
    {
        return value.Replace("'", "''");
    }

    private static string? FirstUrl(string text)
    {
        var match = Regex.Match(text, @"https?://[^\s""'<>]+");
        return match.Success ? match.Value : null;
    }
}
