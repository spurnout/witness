using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BrowserExtensionOperatorDiagnosticsServiceTests
{
    [TestMethod]
    public async Task Build_DistinguishesExtensionNativeHostAndBrowserProofStates()
    {
        await WithTempPathsAsync(async paths =>
        {
            var extensionRoot = CreateExtensionFixture(paths);
            var service = new BrowserExtensionOperatorDiagnosticsService(paths);

            var diagnostics = service.Build(new BrowserExtensionOperatorDiagnosticsRequest
            {
                ExtensionSourceDirectory = extensionRoot,
                NativeHostStatus = new BrowserNativeHostStatus
                {
                    HostName = BrowserNativeHostRegistrationService.HostName,
                    ManifestRoot = Path.Combine(paths.TempRoot, "native-host"),
                    Registrations =
                    {
                        new BrowserNativeHostRegistrationState
                        {
                            Browser = BrowserNativeHostBrowser.Chrome,
                            Installed = false,
                            Message = "Chrome native messaging host is not registered in HKCU."
                        },
                        new BrowserNativeHostRegistrationState
                        {
                            Browser = BrowserNativeHostBrowser.Edge,
                            Installed = false,
                            ManifestPath = Path.Combine(paths.TempRoot, "missing-edge-manifest.json"),
                            Message = "Edge registry points to a missing manifest."
                        },
                        new BrowserNativeHostRegistrationState
                        {
                            Browser = BrowserNativeHostBrowser.Firefox,
                            Installed = true,
                            ManifestPath = Path.Combine(paths.TempRoot, "firefox", "com.goatshot.bridge.json"),
                            Message = "Firefox native messaging manifest is installed in the user profile folder."
                        }
                    }
                }
            });

            Assert.IsTrue(diagnostics.ExtensionSourceExists);
            Assert.IsTrue(diagnostics.ManifestExists);
            Assert.IsTrue(diagnostics.ServiceWorkerExists);
            Assert.IsTrue(diagnostics.SafeFixtureExists);
            Assert.IsTrue(diagnostics.BrowserProofRequired);
            AssertEntry(diagnostics, "extension-source-ready", "ready");
            AssertEntry(diagnostics, "safe-fixture-ready", "ready");
            AssertEntry(diagnostics, "extension-installed-not-detectable-from-desktop", "manual");
            AssertEntry(diagnostics, "native-host-missing", "blocked", "Chrome");
            AssertEntry(diagnostics, "native-host-manifest-missing", "blocked", "Edge");
            AssertEntry(diagnostics, "native-host-registered-browser-proof-needed", "manual", "Firefox");
            AssertEntry(diagnostics, "native-host-unreachable-browser-proof-needed", "manual");
            AssertEntry(diagnostics, "payload-rejected-diagnostics-available", "ready");
            AssertEntry(diagnostics, "stitch-package-import-diagnostics-available", "ready");
            AssertEntry(diagnostics, "browser-download-package-boundary", "manual");
            Assert.IsTrue(diagnostics.Warnings.Any(warning => warning.Contains("not detectable", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(diagnostics.Warnings.Any(warning => warning.Contains("Host Status", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(diagnostics.NextActions.Any(action => action.Contains("safe-fixture.html", StringComparison.OrdinalIgnoreCase)));

            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task Build_ReportsMissingSourceAsBlocked()
    {
        await WithTempPathsAsync(async paths =>
        {
            var service = new BrowserExtensionOperatorDiagnosticsService(paths);

            var diagnostics = service.Build(new BrowserExtensionOperatorDiagnosticsRequest
            {
                ExtensionSourceDirectory = Path.Combine(paths.TempRoot, "missing-extension"),
                NativeHostStatus = new BrowserNativeHostStatus
                {
                    HostName = BrowserNativeHostRegistrationService.HostName
                }
            });

            Assert.IsFalse(diagnostics.ExtensionSourceExists);
            AssertEntry(diagnostics, "extension-source-missing", "blocked");
            AssertEntry(diagnostics, "native-host-status-unavailable", "warning");
            Assert.IsTrue(diagnostics.Issues.Any(issue => issue.Contains("source folder", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(diagnostics.NextActions.Any(action => action.Contains("Restore", StringComparison.OrdinalIgnoreCase)));

            await Task.CompletedTask;
        });
    }

    private static void AssertEntry(
        BrowserExtensionOperatorDiagnostics diagnostics,
        string code,
        string status,
        string? browser = null)
    {
        var entry = diagnostics.Entries.FirstOrDefault(candidate =>
            candidate.Code.Equals(code, StringComparison.OrdinalIgnoreCase) &&
            candidate.Status.Equals(status, StringComparison.OrdinalIgnoreCase) &&
            (browser is null || candidate.Browser.Equals(browser, StringComparison.OrdinalIgnoreCase)));
        Assert.IsNotNull(entry, $"Missing diagnostic entry {code}/{status}/{browser ?? "*"}.");
    }

    private static string CreateExtensionFixture(AppPaths paths)
    {
        var root = Path.Combine(paths.TempRoot, "browser-extension");
        Directory.CreateDirectory(Path.Combine(root, "samples"));
        File.WriteAllText(Path.Combine(root, "manifest.json"), "{}");
        File.WriteAllText(Path.Combine(root, "service-worker.js"), "// service worker");
        File.WriteAllText(Path.Combine(root, "popup.html"), "<!doctype html>");
        File.WriteAllText(Path.Combine(root, "options.html"), "<!doctype html>");
        File.WriteAllText(Path.Combine(root, "samples", "safe-fixture.html"), "<!doctype html>");
        return root;
    }

    private static async Task WithTempPathsAsync(Func<AppPaths, Task> action)
    {
        var originalLocalRoot = Environment.GetEnvironmentVariable("GOATSHOT_LOCAL_ROOT");
        var originalLibraryRoot = Environment.GetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", Path.Combine(root, "local"));
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", Path.Combine(root, "library"));
            var paths = AppPaths.Create(new AppSettings());
            await action(paths);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", originalLocalRoot);
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", originalLibraryRoot);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
