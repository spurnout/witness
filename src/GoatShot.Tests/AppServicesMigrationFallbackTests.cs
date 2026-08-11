using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class AppServicesMigrationFallbackTests
{
    [TestMethod]
    public void ShouldUseLegacyStateFallback_WhenSettingsCopyFailedAvoidsCreatingNewDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "Receipts.Tests", Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(root, "GoatShot");
        var receipts = Path.Combine(root, "Receipts");
        try
        {
            Directory.CreateDirectory(legacy);
            Directory.CreateDirectory(receipts);
            File.WriteAllText(Path.Combine(legacy, "settings.json"), "{}");
            var result = new LocalStateMigrationResult(
                LocalStateMigrationStatus.Failed,
                legacy,
                receipts,
                Path.Combine(receipts, "migration.json"),
                []);

            Assert.IsTrue(AppServices.ShouldUseLegacyStateFallback(result));

            File.WriteAllText(Path.Combine(receipts, "settings.json"), "{}");
            Assert.IsTrue(
                AppServices.ShouldUseLegacyStateFallback(result),
                "A partially copied Receipts root must not hide legacy durable state after any migration failure.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
