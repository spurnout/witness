using System.IO.Compression;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BrowserExtensionPackageServiceTests
{
    [TestMethod]
    public async Task PackageAsync_CreatesMinimalExtensionZipFromRepoPrototype()
    {
        var root = FindRepoRoot();
        var source = Path.Combine(root, "browser-extension");
        var output = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"), "extension.zip");
        try
        {
            var service = new BrowserExtensionPackageService();

            var result = await service.PackageAsync(source, output);

            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.IsTrue(File.Exists(output));
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "manifest.json",
                    "content-script.js",
                    "service-worker.js",
                    "popup.html",
                    "popup.js",
                    "options.html",
                    "options.js",
                    "extension-ui.css"
                },
                result.IncludedFiles.ToArray());
            using var archive = ZipFile.OpenRead(output);
            CollectionAssert.AreEquivalent(
                result.IncludedFiles.OrderBy(value => value).ToArray(),
                archive.Entries.Select(entry => entry.FullName).OrderBy(value => value).ToArray());
        }
        finally
        {
            var directory = Path.GetDirectoryName(output);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task PackageAsync_ReportsMissingNativeMessagingPermission()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "manifest.json"), """
                {
                  "manifest_version": 3,
                  "permissions": ["activeTab", "downloads"],
                  "content_scripts": [{ "matches": ["https://*/*"], "js": ["content-script.js"] }],
                  "background": { "service_worker": "service-worker.js" },
                  "action": { "default_popup": "popup.html" },
                  "options_ui": { "page": "options.html" }
                }
                """);
            await WriteRequiredFilesExceptAsync(root, "manifest.json");
            var service = new BrowserExtensionPackageService();

            var result = await service.PackageAsync(root, Path.Combine(root, "extension.zip"));

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("nativeMessaging", StringComparison.Ordinal)));
            Assert.IsFalse(File.Exists(Path.Combine(root, "extension.zip")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task PackageAsync_ReportsMissingActiveTabPermission()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "manifest.json"), """
                {
                  "manifest_version": 3,
                  "permissions": ["downloads", "nativeMessaging"],
                  "content_scripts": [{ "matches": ["https://*/*"], "js": ["content-script.js"] }],
                  "background": { "service_worker": "service-worker.js" },
                  "action": { "default_popup": "popup.html" },
                  "options_ui": { "page": "options.html" }
                }
                """);
            await WriteRequiredFilesExceptAsync(root, "manifest.json");
            var service = new BrowserExtensionPackageService();

            var result = await service.PackageAsync(root, Path.Combine(root, "extension.zip"));

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("activeTab", StringComparison.Ordinal)));
            Assert.IsFalse(File.Exists(Path.Combine(root, "extension.zip")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task PackageAsync_ReportsMissingDownloadsPermission()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "manifest.json"), """
                {
                  "manifest_version": 3,
                  "permissions": ["activeTab", "nativeMessaging", "storage"],
                  "content_scripts": [{ "matches": ["https://*/*"], "js": ["content-script.js"] }],
                  "background": { "service_worker": "service-worker.js" },
                  "action": { "default_popup": "popup.html" },
                  "options_ui": { "page": "options.html" }
                }
                """);
            await WriteRequiredFilesExceptAsync(root, "manifest.json");
            var service = new BrowserExtensionPackageService();

            var result = await service.PackageAsync(root, Path.Combine(root, "extension.zip"));

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("downloads", StringComparison.Ordinal)));
            Assert.IsFalse(File.Exists(Path.Combine(root, "extension.zip")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task PackageAsync_ReportsMissingOperatorUxManifestWiring()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "manifest.json"), """
                {
                  "manifest_version": 3,
                  "permissions": ["nativeMessaging", "activeTab", "downloads"],
                  "content_scripts": [{ "matches": ["https://*/*"], "js": ["content-script.js"] }],
                  "background": { "service_worker": "service-worker.js" }
                }
                """);
            await WriteRequiredFilesExceptAsync(root, "manifest.json");
            var service = new BrowserExtensionPackageService();

            var result = await service.PackageAsync(root, Path.Combine(root, "extension.zip"));

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("storage", StringComparison.Ordinal)));
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("popup.html", StringComparison.Ordinal)));
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("options.html", StringComparison.Ordinal)));
            Assert.IsFalse(File.Exists(Path.Combine(root, "extension.zip")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoatShot.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, "browser-extension")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find GoatShot repo root.");
    }

    private static async Task WriteRequiredFilesExceptAsync(string root, params string[] excluded)
    {
        var excludedSet = excluded.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in new[]
        {
            "content-script.js",
            "service-worker.js",
            "popup.html",
            "popup.js",
            "options.html",
            "options.js",
            "extension-ui.css"
        })
        {
            if (!excludedSet.Contains(file))
            {
                await File.WriteAllTextAsync(Path.Combine(root, file), "");
            }
        }
    }
}
