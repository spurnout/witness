using System.Text.Json;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ReceiptsLocalStateMigrationServiceTests
{
    private string _testRoot = null!;
    private string _legacyRoot = null!;
    private string _receiptsRoot = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "receipts-local-state-migration-tests", Guid.NewGuid().ToString("N"));
        _legacyRoot = Path.Combine(_testRoot, "GoatShot");
        _receiptsRoot = Path.Combine(_testRoot, "Receipts");
        Directory.CreateDirectory(_legacyRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        var pluginLink = Path.Combine(_legacyRoot, "plugins", "linked.plugin");
        if (Directory.Exists(pluginLink) &&
            (File.GetAttributes(pluginLink) & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(pluginLink);
        }

        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task MigrateAsync_CopiesOnlyAllowlistedDurableStateAndLeavesMediaInPlace()
    {
        var settings = """
            {
              "libraryRoot": "D:\\Existing GoatShot Library"
            }
            """;
        WriteLegacyFile("settings.json", settings);
        WriteLegacyFile("workspace-index.json", "{\"items\":[]}");
        WriteLegacyFile("workspace.sqlite", "database");
        WriteLegacyFile("workspace.sqlite-wal", "wal");
        WriteLegacyFile("workspace.sqlite-shm", "shm");
        WriteLegacyFile("ai-action-history.json", "[]");
        WriteLegacyFile("share-history.json", "[]");
        WriteLegacyFile("upload-queue.json", "[]");
        WriteLegacyFile(Path.Combine("secrets", "github-token.dpapi"), "protected-secret");
        WriteLegacyFile(Path.Combine("secrets", "readme.txt"), "not-a-secret-payload");
        WriteLegacyFile(Path.Combine("temp", "frame.png"), "temporary-frame");
        WriteLegacyFile(Path.Combine("thumbnails", "preview.png"), "thumbnail");
        WriteLegacyFile(Path.Combine("logs", "goatshot.log"), "possibly-sensitive-log");
        WriteLegacyFile(Path.Combine("Images", "receipt.png"), "media");

        var service = new ReceiptsLocalStateMigrationService(_legacyRoot, _receiptsRoot);
        var result = await service.MigrateAsync();

        Assert.AreEqual(LocalStateMigrationStatus.Completed, result.Status);
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(9, result.CopiedFileCount);
        Assert.AreEqual(settings, File.ReadAllText(Path.Combine(_receiptsRoot, "settings.json")));
        Assert.AreEqual("wal", File.ReadAllText(Path.Combine(_receiptsRoot, "workspace.sqlite-wal")));
        Assert.AreEqual("shm", File.ReadAllText(Path.Combine(_receiptsRoot, "workspace.sqlite-shm")));
        Assert.AreEqual("protected-secret", File.ReadAllText(Path.Combine(_receiptsRoot, "secrets", "github-token.dpapi")));
        Assert.IsFalse(File.Exists(Path.Combine(_receiptsRoot, "secrets", "readme.txt")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_receiptsRoot, "temp")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_receiptsRoot, "thumbnails")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_receiptsRoot, "logs")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_receiptsRoot, "Images")));
        Assert.IsTrue(File.Exists(Path.Combine(_legacyRoot, "Images", "receipt.png")));

        using var marker = JsonDocument.Parse(File.ReadAllText(result.MarkerPath));
        Assert.AreEqual(BrandIdentity.LocalStateMigrationSchema, marker.RootElement.GetProperty("schema").GetString());
        Assert.AreEqual(_legacyRoot, marker.RootElement.GetProperty("sourceRoot").GetString());
        Assert.AreEqual(_receiptsRoot, marker.RootElement.GetProperty("destinationRoot").GetString());
    }

    [TestMethod]
    public async Task MigrateAsync_CopiesInstalledPluginsScheduleStateAndAdbAuthorizationOnly()
    {
        WriteLegacyFile("plugin-background-updates.json", "background-state");
        WriteLegacyFile(Path.Combine("plugins", "sample.plugin", "plugin.json"), "legacy-plugin-manifest");
        WriteLegacyFile(Path.Combine("plugins", "sample.plugin", "scripts", "run.ps1"), "plugin-script");
        WriteLegacyFile(Path.Combine("plugins", "sample.plugin", "temp", "download.tmp"), "temporary-plugin-data");
        WriteLegacyFile(Path.Combine("plugins", "sample.plugin", "staging", "package.zip"), "staged-plugin-data");
        WriteLegacyFile(Path.Combine("plugin-staging", "sample.plugin", "package.zip"), "staged-package");
        WriteLegacyFile(
            Path.Combine("plugin-update-schedule", "plugin-update-schedule.json"),
            "schedule-manifest");
        WriteLegacyFile(
            Path.Combine("plugin-update-schedule", "plugin-background-updates-state.json"),
            "schedule-state");
        WriteLegacyFile(
            Path.Combine("plugin-update-schedule", "run-plugin-background-updates.ps1"),
            "generated-runner");
        WriteLegacyFile(Path.Combine("plugin-update-schedule", "last-run.log"), "runtime-log");
        WriteLegacyFile(Path.Combine("adb-authorization", "adbkey.pk8"), "private-key-state");
        WriteLegacyFile(Path.Combine("adb-authorization", "adbkey.pub"), "public-key-state");
        WriteLegacyFile(Path.Combine("adb-authorization", "notes.txt"), "not-authorization-state");
        WriteReceiptsFile(Path.Combine("plugins", "sample.plugin", "plugin.json"), "newer-plugin-manifest");

        var service = new ReceiptsLocalStateMigrationService(_legacyRoot, _receiptsRoot);
        var result = await service.MigrateAsync();

        Assert.AreEqual(LocalStateMigrationStatus.Completed, result.Status);
        Assert.AreEqual(
            "newer-plugin-manifest",
            File.ReadAllText(Path.Combine(_receiptsRoot, "plugins", "sample.plugin", "plugin.json")));
        Assert.AreEqual(
            "plugin-script",
            File.ReadAllText(Path.Combine(_receiptsRoot, "plugins", "sample.plugin", "scripts", "run.ps1")));
        Assert.AreEqual(
            "background-state",
            File.ReadAllText(Path.Combine(_receiptsRoot, "plugin-background-updates.json")));
        Assert.AreEqual(
            "schedule-manifest",
            File.ReadAllText(Path.Combine(_receiptsRoot, "plugin-update-schedule", "plugin-update-schedule.json")));
        Assert.AreEqual(
            "schedule-state",
            File.ReadAllText(Path.Combine(_receiptsRoot, "plugin-update-schedule", "plugin-background-updates-state.json")));
        Assert.AreEqual(
            "private-key-state",
            File.ReadAllText(Path.Combine(_receiptsRoot, "adb-authorization", "adbkey.pk8")));
        Assert.AreEqual(
            "public-key-state",
            File.ReadAllText(Path.Combine(_receiptsRoot, "adb-authorization", "adbkey.pub")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_receiptsRoot, "plugin-staging")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_receiptsRoot, "plugins", "sample.plugin", "temp")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_receiptsRoot, "plugins", "sample.plugin", "staging")));
        Assert.IsFalse(File.Exists(Path.Combine(
            _receiptsRoot,
            "plugin-update-schedule",
            "run-plugin-background-updates.ps1")));
        Assert.IsFalse(File.Exists(Path.Combine(_receiptsRoot, "plugin-update-schedule", "last-run.log")));
        Assert.IsFalse(File.Exists(Path.Combine(_receiptsRoot, "adb-authorization", "notes.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(_legacyRoot, "plugins", "sample.plugin", "plugin.json")));
        Assert.IsTrue(result.Files.Any(file =>
            file.RelativePath == Path.Combine("plugins", "sample.plugin", "plugin.json") &&
            file.Disposition == LocalStateMigrationFileDisposition.SkippedExistingDestination));
        Assert.AreEqual(
            2,
            result.Files.Count(file => file.Disposition == LocalStateMigrationFileDisposition.SkippedTransientData));
    }

    [TestMethod]
    public async Task MigrateAsync_NeverOverwritesDestinationAndMarkerMakesRerunsIdempotent()
    {
        WriteLegacyFile("settings.json", "legacy-settings");
        WriteLegacyFile("workspace.sqlite", "first-database");
        WriteReceiptsFile("settings.json", "newer-receipts-settings");

        var service = new ReceiptsLocalStateMigrationService(_legacyRoot, _receiptsRoot);
        var first = await service.MigrateAsync();
        WriteLegacyFile("workspace.sqlite", "changed-after-migration");
        var second = await service.MigrateAsync();

        Assert.AreEqual(LocalStateMigrationStatus.Completed, first.Status);
        Assert.AreEqual("newer-receipts-settings", File.ReadAllText(Path.Combine(_receiptsRoot, "settings.json")));
        Assert.AreEqual("first-database", File.ReadAllText(Path.Combine(_receiptsRoot, "workspace.sqlite")));
        Assert.IsTrue(first.Files.Any(file =>
            file.RelativePath == "settings.json" &&
            file.Disposition == LocalStateMigrationFileDisposition.SkippedExistingDestination));
        Assert.AreEqual(LocalStateMigrationStatus.AlreadyCompleted, second.Status);
        Assert.AreEqual(0, second.CopiedFileCount);
        Assert.AreEqual("first-database", File.ReadAllText(Path.Combine(_receiptsRoot, "workspace.sqlite")));
    }

    [TestMethod]
    public async Task MigrateAsync_SkipsOversizedStateAndRecordsTheSafetyDecision()
    {
        WriteLegacyFile("settings.json", "12345");
        WriteLegacyFile("workspace-index.json", "1234567890");

        var service = new ReceiptsLocalStateMigrationService(
            _legacyRoot,
            _receiptsRoot,
            maximumIndividualFileBytes: 8,
            maximumTotalBytes: 8);
        var result = await service.MigrateAsync();

        Assert.AreEqual(LocalStateMigrationStatus.Completed, result.Status);
        Assert.IsTrue(File.Exists(Path.Combine(_receiptsRoot, "settings.json")));
        Assert.IsFalse(File.Exists(Path.Combine(_receiptsRoot, "workspace-index.json")));
        Assert.IsTrue(result.Files.Any(file =>
            file.RelativePath == "workspace-index.json" &&
            file.Disposition == LocalStateMigrationFileDisposition.SkippedSafetyLimit));
        Assert.IsTrue(File.Exists(result.MarkerPath));
    }

    [TestMethod]
    public async Task MigrateAsync_EnforcesPluginSpecificPerFileAndTotalBounds()
    {
        WriteLegacyFile(Path.Combine("plugins", "a.plugin", "a.txt"), "123456");
        WriteLegacyFile(Path.Combine("plugins", "b.plugin", "b.txt"), "123456");
        WriteLegacyFile(Path.Combine("plugins", "c.plugin", "oversized.txt"), "123456789");

        var service = new ReceiptsLocalStateMigrationService(
            _legacyRoot,
            _receiptsRoot,
            maximumIndividualFileBytes: 100,
            maximumTotalBytes: 100,
            maximumPluginIndividualFileBytes: 8,
            maximumPluginTotalBytes: 10);
        var result = await service.MigrateAsync();

        Assert.AreEqual(LocalStateMigrationStatus.Completed, result.Status);
        Assert.IsTrue(File.Exists(Path.Combine(_receiptsRoot, "plugins", "a.plugin", "a.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(_receiptsRoot, "plugins", "b.plugin", "b.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(_receiptsRoot, "plugins", "c.plugin", "oversized.txt")));
        CollectionAssert.AreEquivalent(
            new[]
            {
                Path.Combine("plugins", "b.plugin", "b.txt"),
                Path.Combine("plugins", "c.plugin", "oversized.txt")
            },
            result.Files
                .Where(file => file.Disposition == LocalStateMigrationFileDisposition.SkippedSafetyLimit)
                .Select(file => file.RelativePath)
                .ToArray());
        Assert.IsTrue(File.Exists(result.MarkerPath));
    }

    [TestMethod]
    public async Task MigrateAsync_PartialCopyFailureIsRetryableAndRerunRemainsIdempotent()
    {
        WriteLegacyFile("settings.json", "legacy-settings");
        var lockedRelativePath = Path.Combine("plugins", "locked.plugin", "plugin.json");
        WriteLegacyFile(lockedRelativePath, "locked-plugin");
        var lockedPath = Path.Combine(_legacyRoot, lockedRelativePath);

        LocalStateMigrationResult first;
        using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var service = new ReceiptsLocalStateMigrationService(_legacyRoot, _receiptsRoot);
            first = await service.MigrateAsync();
        }

        Assert.AreEqual(LocalStateMigrationStatus.Failed, first.Status);
        Assert.AreEqual("legacy-settings", File.ReadAllText(Path.Combine(_receiptsRoot, "settings.json")));
        Assert.IsFalse(File.Exists(Path.Combine(_receiptsRoot, lockedRelativePath)));
        Assert.IsFalse(File.Exists(first.MarkerPath));
        Assert.IsTrue(first.Files.Any(file =>
            file.RelativePath == lockedRelativePath &&
            file.Disposition == LocalStateMigrationFileDisposition.Failed));

        var retryService = new ReceiptsLocalStateMigrationService(_legacyRoot, _receiptsRoot);
        var retry = await retryService.MigrateAsync();
        WriteLegacyFile(lockedRelativePath, "changed-after-retry");
        var completedRerun = await retryService.MigrateAsync();

        Assert.AreEqual(LocalStateMigrationStatus.Completed, retry.Status);
        Assert.AreEqual("locked-plugin", File.ReadAllText(Path.Combine(_receiptsRoot, lockedRelativePath)));
        Assert.IsTrue(retry.Files.Any(file =>
            file.RelativePath == "settings.json" &&
            file.Disposition == LocalStateMigrationFileDisposition.SkippedExistingDestination));
        Assert.IsTrue(File.Exists(retry.MarkerPath));
        Assert.AreEqual(LocalStateMigrationStatus.AlreadyCompleted, completedRerun.Status);
        Assert.AreEqual("locked-plugin", File.ReadAllText(Path.Combine(_receiptsRoot, lockedRelativePath)));
        Assert.AreEqual("changed-after-retry", File.ReadAllText(Path.Combine(_legacyRoot, lockedRelativePath)));
    }

    [TestMethod]
    public async Task MigrateAsync_RejectsReparsePointsInsideInstalledPlugins()
    {
        var externalPluginRoot = Path.Combine(_testRoot, "external-plugin");
        WriteFile(externalPluginRoot, "payload.txt", "outside-plugin-state");
        var pluginsRoot = Path.Combine(_legacyRoot, "plugins");
        Directory.CreateDirectory(pluginsRoot);
        var linkPath = Path.Combine(pluginsRoot, "linked.plugin");
        try
        {
            Directory.CreateSymbolicLink(linkPath, externalPluginRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Assert.Inconclusive($"This host cannot create the directory link needed for the reparse-point test: {exception.Message}");
        }

        var service = new ReceiptsLocalStateMigrationService(_legacyRoot, _receiptsRoot);
        var result = await service.MigrateAsync();

        Assert.AreEqual(LocalStateMigrationStatus.Completed, result.Status);
        Assert.IsFalse(Directory.Exists(Path.Combine(_receiptsRoot, "plugins", "linked.plugin")));
        Assert.AreEqual("outside-plugin-state", File.ReadAllText(Path.Combine(externalPluginRoot, "payload.txt")));
        Assert.IsTrue(result.Files.Any(file =>
            file.RelativePath == Path.Combine("plugins", "linked.plugin") &&
            file.Disposition == LocalStateMigrationFileDisposition.SkippedReparsePoint));
        using var marker = JsonDocument.Parse(File.ReadAllText(result.MarkerPath));
        CollectionAssert.Contains(
            marker.RootElement.GetProperty("skippedReparsePointPaths")
                .EnumerateArray()
                .Select(element => element.GetString())
                .ToArray(),
            Path.Combine("plugins", "linked.plugin"));
    }

    [TestMethod]
    public async Task MigrateAsync_ReturnsNotNeededWhenCompatibilityOverrideUsesTheSameRoot()
    {
        WriteLegacyFile("settings.json", "settings");
        var service = new ReceiptsLocalStateMigrationService(_legacyRoot, _legacyRoot);

        var result = await service.MigrateAsync();

        Assert.AreEqual(LocalStateMigrationStatus.NotNeeded, result.Status);
        Assert.IsTrue(result.Succeeded);
        Assert.IsFalse(File.Exists(result.MarkerPath));
        Assert.AreEqual("settings", File.ReadAllText(Path.Combine(_legacyRoot, "settings.json")));
    }

    [TestMethod]
    public async Task MigrateAsync_WritesMarkerWhenNoLegacyStateExists()
    {
        Directory.Delete(_legacyRoot);
        var service = new ReceiptsLocalStateMigrationService(_legacyRoot, _receiptsRoot);

        var result = await service.MigrateAsync();

        Assert.AreEqual(LocalStateMigrationStatus.Completed, result.Status);
        Assert.AreEqual(0, result.CopiedFileCount);
        Assert.IsTrue(File.Exists(result.MarkerPath));
    }

    [TestMethod]
    public void MigrateAsync_CompletesWhenStartupBlocksOnAThreadWithSynchronizationContext()
    {
        WriteLegacyFile("settings.json", new string('x', 4 * 1024 * 1024));
        var service = new ReceiptsLocalStateMigrationService(_legacyRoot, _receiptsRoot);
        Exception? failure = null;
        LocalStateMigrationResult? result = null;
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            try
            {
                result = service.MigrateAsync().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true,
            Name = "Receipts migration blocking-startup regression"
        };

        thread.Start();

        Assert.IsTrue(
            completed.Wait(TimeSpan.FromSeconds(10)),
            "Migration deadlocked while startup synchronously waited under a synchronization context.");
        Assert.IsNull(failure);
        Assert.IsNotNull(result);
        Assert.AreEqual(LocalStateMigrationStatus.Completed, result.Status);
        Assert.IsTrue(File.Exists(result.MarkerPath));
    }

    private void WriteLegacyFile(string relativePath, string content)
    {
        WriteFile(_legacyRoot, relativePath, content);
    }

    private void WriteReceiptsFile(string relativePath, string content)
    {
        WriteFile(_receiptsRoot, relativePath, content);
    }

    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            // Intentionally do not pump continuations. Startup synchronously waits
            // for migration, so migration code must never capture this context.
        }
    }
}
