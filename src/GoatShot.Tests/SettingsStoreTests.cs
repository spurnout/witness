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
