using System.IO.Compression;
using System.Text.Json;

namespace GoatShot.App.Services;

public sealed class BrowserExtensionPackageService
{
    private static readonly string[] RequiredFiles =
    {
        "manifest.json",
        "content-script.js",
        "service-worker.js",
        "popup.html",
        "popup.js",
        "options.html",
        "options.js",
        "extension-ui.css"
    };

    public async Task<BrowserExtensionPackageResult> PackageAsync(
        string sourceDirectory,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(Environment.ExpandEnvironmentVariables(sourceDirectory));
        if (!Directory.Exists(source))
        {
            return BrowserExtensionPackageResult.Failed($"Browser extension source directory was not found: {source}");
        }

        var issues = ValidateSource(source);
        if (issues.Count > 0)
        {
            return new BrowserExtensionPackageResult
            {
                Succeeded = false,
                SourceDirectory = source,
                OutputPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath)),
                Issues = issues.ToList(),
                Message = "Browser extension package validation failed."
            };
        }

        var output = Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (File.Exists(output))
        {
            File.Delete(output);
        }

        var includedFiles = RequiredFiles.OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToList();
        using (var archive = ZipFile.Open(output, ZipArchiveMode.Create))
        {
            foreach (var relative in includedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await using var fileStream = File.OpenRead(Path.Combine(source, relative));
                await fileStream.CopyToAsync(entryStream, cancellationToken);
            }
        }

        return new BrowserExtensionPackageResult
        {
            Succeeded = true,
            SourceDirectory = source,
            OutputPath = output,
            IncludedFiles = includedFiles,
            Bytes = new FileInfo(output).Length,
            Message = $"Browser extension package created: {output}"
        };
    }

    public IReadOnlyList<string> ValidateSource(string sourceDirectory)
    {
        var source = Path.GetFullPath(Environment.ExpandEnvironmentVariables(sourceDirectory));
        var issues = new List<string>();
        foreach (var file in RequiredFiles)
        {
            if (!File.Exists(Path.Combine(source, file)))
            {
                issues.Add($"Required extension file is missing: {file}");
            }
        }

        var manifestPath = Path.Combine(source, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return issues;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("manifest_version", out var version) || version.GetInt32() != 3)
            {
                issues.Add("manifest.json must use manifest_version 3.");
            }

            if (!ArrayContains(root, "permissions", "nativeMessaging"))
            {
                issues.Add("manifest.json must request nativeMessaging permission.");
            }

            if (!ArrayContains(root, "permissions", "activeTab"))
            {
                issues.Add("manifest.json must request activeTab permission for visible-tab tile capture.");
            }

            if (!ArrayContains(root, "permissions", "downloads"))
            {
                issues.Add("manifest.json must request downloads permission for stitch package export.");
            }

            if (!ArrayContains(root, "permissions", "storage"))
            {
                issues.Add("manifest.json must request storage permission for popup/options settings.");
            }

            if (!ContainsContentScript(root, "content-script.js"))
            {
                issues.Add("manifest.json must register content-script.js.");
            }

            if (!root.TryGetProperty("background", out var background) ||
                !background.TryGetProperty("service_worker", out var serviceWorker) ||
                !serviceWorker.GetString()!.Equals("service-worker.js", StringComparison.Ordinal))
            {
                issues.Add("manifest.json must register service-worker.js as the background service worker.");
            }

            if (!root.TryGetProperty("action", out var action) ||
                !action.TryGetProperty("default_popup", out var popup) ||
                !popup.GetString()!.Equals("popup.html", StringComparison.Ordinal))
            {
                issues.Add("manifest.json must register popup.html as the action default_popup.");
            }

            if (!root.TryGetProperty("options_ui", out var optionsUi) ||
                !optionsUi.TryGetProperty("page", out var optionsPage) ||
                !optionsPage.GetString()!.Equals("options.html", StringComparison.Ordinal))
            {
                issues.Add("manifest.json must register options.html as the options_ui page.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            issues.Add($"manifest.json could not be validated: {SensitiveTextDetector.Redact(ex.Message)}");
        }

        return issues;
    }

    private static bool ArrayContains(JsonElement root, string propertyName, string expected)
    {
        if (!root.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return array.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.String &&
            item.GetString()!.Equals(expected, StringComparison.Ordinal));
    }

    private static bool ContainsContentScript(JsonElement root, string expectedFile)
    {
        if (!root.TryGetProperty("content_scripts", out var contentScripts) ||
            contentScripts.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var script in contentScripts.EnumerateArray())
        {
            if (!script.TryGetProperty("js", out var js) || js.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            if (js.EnumerateArray().Any(item =>
                item.ValueKind == JsonValueKind.String &&
                item.GetString()!.Equals(expectedFile, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class BrowserExtensionPackageResult
{
    public bool Succeeded { get; set; }
    public string SourceDirectory { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public List<string> IncludedFiles { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public long Bytes { get; set; }
    public string Message { get; set; } = string.Empty;

    public static BrowserExtensionPackageResult Failed(string message)
    {
        return new BrowserExtensionPackageResult
        {
            Succeeded = false,
            Message = SensitiveTextDetector.Redact(message)
        };
    }
}
