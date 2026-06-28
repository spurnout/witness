using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ProviderDiagnosticsServiceTests
{
    [TestMethod]
    public async Task GetDiagnostics_MarksConfiguredS3ReadyWithoutExposingSecretValues()
    {
        await WithTempPathsAsync(paths =>
        {
            var settings = new AppSettings
            {
                S3Endpoint = "https://s3.example.test",
                S3Bucket = "captures"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveS3Credentials("S3_ACCESS_KEY_VALUE", "S3_SECRET_VALUE");
            var diagnostics = new ProviderDiagnosticsService(settings, secrets);

            var s3 = diagnostics.GetDiagnostics().Single(record => record.ProviderName == "S3-compatible");

            Assert.IsTrue(s3.ReadyForLocalAttempt, s3.ReadinessSummary);
            Assert.AreEqual("Ready", s3.Status);
            CollectionAssert.Contains(s3.ConfiguredSettings, "S3 endpoint");
            CollectionAssert.Contains(s3.ConfiguredSettings, "S3 bucket");
            CollectionAssert.Contains(s3.SavedSecrets, "S3 access key ID and secret access key");
            Assert.IsFalse(string.Join(" ", s3.SavedSecrets).Contains("S3_ACCESS_KEY_VALUE", StringComparison.Ordinal));
            Assert.IsFalse(string.Join(" ", s3.SavedSecrets).Contains("S3_SECRET_VALUE", StringComparison.Ordinal));
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task GetDiagnostics_ReportsMissingGitHubTokenAndRepository()
    {
        await WithTempPathsAsync(paths =>
        {
            var diagnostics = new ProviderDiagnosticsService(new AppSettings(), new SecretStore(paths));

            var github = diagnostics.GetDiagnostics().Single(record => record.ProviderName == "GitHub Issues");

            Assert.IsFalse(github.ReadyForLocalAttempt);
            Assert.AreEqual("Needs configuration", github.Status);
            CollectionAssert.Contains(github.MissingSettings, "GitHub repository");
            CollectionAssert.Contains(github.MissingSecrets, "GitHub personal access token");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task GetDiagnostics_MarksCloudinaryReadyWithSavedCredentialsEvenWithoutPreset()
    {
        await WithTempPathsAsync(paths =>
        {
            var settings = new AppSettings
            {
                CloudinaryCloudName = "demo-cloud"
            };
            var secrets = new SecretStore(paths);
            secrets.SaveCloudinaryCredentials("cloud-key", "cloud-secret");
            var diagnostics = new ProviderDiagnosticsService(settings, secrets);

            var cloudinary = diagnostics.GetDiagnostics().Single(record => record.ProviderName == "Cloudinary");

            Assert.IsTrue(cloudinary.ReadyForLocalAttempt, cloudinary.ReadinessSummary);
            CollectionAssert.Contains(cloudinary.SavedSecrets, "Cloudinary API key and API secret");
            Assert.IsFalse(cloudinary.MissingSettings.Contains("Cloudinary unsigned upload preset"));
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task GetDiagnostics_MarksGooglePhotosYouTubeAndOneNoteImplementedButNeedingOAuthTokens()
    {
        await WithTempPathsAsync(paths =>
        {
            var diagnostics = new ProviderDiagnosticsService(new AppSettings(), new SecretStore(paths));

            var googlePhotos = diagnostics.GetDiagnostics().Single(record => record.ProviderName == "Google Photos");
            var youtube = diagnostics.GetDiagnostics().Single(record => record.ProviderName == "YouTube");
            var oneNote = diagnostics.GetDiagnostics().Single(record => record.ProviderName == "OneNote");

            Assert.IsTrue(googlePhotos.CatalogImplemented);
            Assert.IsFalse(googlePhotos.ReadyForLocalAttempt);
            Assert.AreEqual("Needs configuration", googlePhotos.Status);
            CollectionAssert.Contains(googlePhotos.ConfiguredSettings, "Google Photos upload API base URL");
            CollectionAssert.Contains(googlePhotos.ConfiguredSettings, "Google Photos API base URL");
            CollectionAssert.Contains(googlePhotos.MissingSecrets, "Google Photos OAuth access token");

            Assert.IsTrue(youtube.CatalogImplemented);
            Assert.IsFalse(youtube.ReadyForLocalAttempt);
            Assert.AreEqual("Needs configuration", youtube.Status);
            CollectionAssert.Contains(youtube.ConfiguredSettings, "YouTube upload API base URL");
            CollectionAssert.Contains(youtube.ConfiguredSettings, "YouTube privacy status");
            CollectionAssert.Contains(youtube.ConfiguredSettings, "YouTube category ID");
            CollectionAssert.Contains(youtube.MissingSecrets, "YouTube OAuth access token");

            Assert.IsTrue(oneNote.CatalogImplemented);
            Assert.IsFalse(oneNote.ReadyForLocalAttempt);
            Assert.AreEqual("Needs configuration", oneNote.Status);
            CollectionAssert.Contains(oneNote.ConfiguredSettings, "OneNote Graph API base URL");
            CollectionAssert.Contains(oneNote.MissingSecrets, "OneNote OAuth access token");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task GetDiagnostics_ReportsWebhookProviderReadinessWithoutSecretValues()
    {
        await WithTempPathsAsync(paths =>
        {
            var missing = new ProviderDiagnosticsService(new AppSettings(), new SecretStore(paths))
                .GetDiagnostics();
            var configured = new ProviderDiagnosticsService(
                    new AppSettings
                    {
                        SlackWebhookUrl = "https://hooks.slack.example.test/services/secret",
                        DiscordWebhookUrl = "https://discord.example.test/api/webhooks/secret",
                        TeamsWebhookUrl = "https://teams.example.test/webhook/secret"
                    },
                    new SecretStore(paths))
                .GetDiagnostics();

            AssertMissing(missing, "Slack", "Slack webhook URL");
            AssertMissing(missing, "Discord", "Discord webhook URL");
            AssertMissing(missing, "Microsoft Teams", "Microsoft Teams webhook URL");

            AssertConfigured(configured, "Slack", "Slack webhook URL");
            AssertConfigured(configured, "Discord", "Discord webhook URL");
            AssertConfigured(configured, "Microsoft Teams", "Microsoft Teams webhook URL");
            Assert.IsFalse(string.Join(" ", configured.SelectMany(record => record.ConfiguredSettings)).Contains("secret", StringComparison.OrdinalIgnoreCase));
            return Task.CompletedTask;
        });
    }

    private static void AssertMissing(
        IReadOnlyList<ProviderDiagnosticRecord> diagnostics,
        string providerName,
        string settingName)
    {
        var record = diagnostics.Single(item => item.ProviderName == providerName);
        Assert.IsFalse(record.ReadyForLocalAttempt, record.ReadinessSummary);
        CollectionAssert.Contains(record.MissingSettings, settingName);
    }

    private static void AssertConfigured(
        IReadOnlyList<ProviderDiagnosticRecord> diagnostics,
        string providerName,
        string settingName)
    {
        var record = diagnostics.Single(item => item.ProviderName == providerName);
        Assert.IsTrue(record.ReadyForLocalAttempt, record.ReadinessSummary);
        CollectionAssert.Contains(record.ConfiguredSettings, settingName);
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
