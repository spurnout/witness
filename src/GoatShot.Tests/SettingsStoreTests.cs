using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class SettingsStoreTests
{
    [TestMethod]
    public void Save_ProtectsWebhookCredentialsAndRoundTripsThem()
    {
        var path = Path.Combine(Path.GetTempPath(), "goatshot-settings-tests", Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore();
        store.UsePath(path);
        const string webhook = "https://hooks.example.test/services/super-secret-token";

        store.Save(new AppSettings { SlackWebhookUrl = webhook });

        var persisted = File.ReadAllText(path);
        Assert.IsFalse(persisted.Contains("super-secret-token", StringComparison.Ordinal));
        StringAssert.Contains(persisted, "dpapi:v1:");
        Assert.AreEqual(webhook, store.Load().SlackWebhookUrl);
    }

    [TestMethod]
    public void SaveAndLoad_PersistsKeybindActionsByNameAndStillReadsNumericOnes()
    {
        var path = Path.Combine(Path.GetTempPath(), "goatshot-settings-tests", Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore();
        store.UsePath(path);

        store.Save(new AppSettings
        {
            Keybinds = [new KeybindAssignment { Action = HotkeyAction.ColorPicker, Gesture = "Alt+F9" }]
        });

        StringAssert.Contains(File.ReadAllText(path), "\"ColorPicker\"");
        Assert.AreEqual(HotkeyAction.ColorPicker, store.Load().Keybinds.Single().Action);

        // Files written before the converter existed stored the ordinal; they must still load.
        File.WriteAllText(
            path,
            $$"""
            { "settingsSchemaVersion": 17, "keybinds": [ { "action": {{(int)HotkeyAction.PixelRuler}}, "gesture": "Alt+F8" } ] }
            """);
        var reloaded = store.Load();

        Assert.IsFalse(store.LastLoadDiagnostics.RecoveredFromUnreadableFile);
        Assert.AreEqual(HotkeyAction.PixelRuler, reloaded.Keybinds.Single().Action);
        Assert.AreEqual("Alt+F8", reloaded.Keybinds.Single().Gesture);
    }

    [TestMethod]
    public void Load_PreservesAnUnreadableSettingsFileInsteadOfSilentlyDiscardingIt()
    {
        var path = Path.Combine(Path.GetTempPath(), "goatshot-settings-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ \"libraryRoot\": \"C:\\\\Captures\", this is not json");
        var store = new SettingsStore();
        store.UsePath(path);

        var loaded = store.Load();

        Assert.AreEqual(string.Empty, loaded.LibraryRoot, "A defaulted instance is expected after a parse failure.");
        Assert.IsTrue(store.LastLoadDiagnostics.RecoveredFromUnreadableFile);
        Assert.IsNotNull(store.LastLoadDiagnostics.PreservedCopyPath);
        Assert.IsTrue(
            File.Exists(store.LastLoadDiagnostics.PreservedCopyPath),
            "The unreadable file must survive so the operator can recover it.");
        StringAssert.Contains(
            File.ReadAllText(store.LastLoadDiagnostics.PreservedCopyPath!),
            "C:\\\\Captures");
    }

    [TestMethod]
    public void Load_KeepsEveryOtherSettingWhenAWebhookSecretCannotBeDecrypted()
    {
        var path = Path.Combine(Path.GetTempPath(), "goatshot-settings-tests", Guid.NewGuid().ToString("N"), "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // A DPAPI blob written by another user or machine cannot be unprotected here. Losing that one
        // value is acceptable; losing the whole configuration is not.
        File.WriteAllText(
            path,
            """
            {
              "libraryRoot": "C:\\Captures",
              "fileNameTemplate": "{date}-{counter}",
              "slackWebhookUrl": "dpapi:v1:AQAAANCMnd8BFdERjHoAwE_Cl-sBAAAAfoRJ0Q==",
              "enableWatchFolders": true
            }
            """);
        var store = new SettingsStore();
        store.UsePath(path);

        var loaded = store.Load();

        Assert.AreEqual("C:\\Captures", loaded.LibraryRoot);
        Assert.AreEqual("{date}-{counter}", loaded.FileNameTemplate);
        Assert.IsTrue(loaded.EnableWatchFolders);
        Assert.AreEqual(string.Empty, loaded.SlackWebhookUrl, "The undecryptable secret should be dropped.");
        Assert.IsFalse(store.LastLoadDiagnostics.RecoveredFromUnreadableFile);
        Assert.IsTrue(store.LastLoadDiagnostics.Warnings.Any(warning =>
            warning.Contains("Slack", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Load_ReportsCleanDiagnosticsForAHealthyFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "goatshot-settings-tests", Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore();
        store.UsePath(path);
        store.Save(new AppSettings { LibraryRoot = "C:\\Captures" });

        store.Load();

        Assert.IsFalse(store.LastLoadDiagnostics.RecoveredFromUnreadableFile);
        Assert.IsNull(store.LastLoadDiagnostics.PreservedCopyPath);
        Assert.AreEqual(0, store.LastLoadDiagnostics.Warnings.Count);
    }

    [TestMethod]
    public void SaveAndLoad_RoundTripsSettingsFromCustomPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "goatshot-settings-tests", Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore();
        store.UsePath(path);

        store.Save(new AppSettings
        {
            LibraryRoot = "C:\\Captures",
            FileNameTemplate = "{date}-{counter}"
        });

        var loaded = store.Load();

        Assert.AreEqual("C:\\Captures", loaded.LibraryRoot);
        Assert.AreEqual("{date}-{counter}", loaded.FileNameTemplate);
    }

    [TestMethod]
    public void Save_HandlesConcurrentWritersWithoutLeavingInvalidJson()
    {
        var path = Path.Combine(Path.GetTempPath(), "goatshot-settings-tests", Guid.NewGuid().ToString("N"), "settings.json");
        var stores = Enumerable.Range(0, 8)
            .Select(_ =>
            {
                var store = new SettingsStore();
                store.UsePath(path);
                return store;
            })
            .ToArray();

        Parallel.For(0, stores.Length, index =>
        {
            stores[index].Save(new AppSettings
            {
                LibraryRoot = $"C:\\Captures\\{index}",
                FileNameTemplate = $"template-{index}"
            });
        });

        var loadedStore = new SettingsStore();
        loadedStore.UsePath(path);
        var loaded = loadedStore.Load();

        Assert.IsTrue(loaded.LibraryRoot.StartsWith("C:\\Captures\\", StringComparison.Ordinal));
        StringAssert.StartsWith(loaded.FileNameTemplate, "template-");
        Assert.IsFalse(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp").Any());
    }
}
