using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class RoadmapModelTests
{
    [TestMethod]
    public void AutomationEnums_CoverRoadmapTriggersAndActions()
    {
        CollectionAssert.IsSubsetOf(
            new[]
            {
                AutomationTrigger.CaptureCreated,
                AutomationTrigger.CaptureEdited,
                AutomationTrigger.FileAddedToWatchFolder,
                AutomationTrigger.RecordingCompleted,
                AutomationTrigger.OcrCompleted,
                AutomationTrigger.UploadCompleted,
                AutomationTrigger.AiActionCompleted
            },
            Enum.GetValues<AutomationTrigger>());

        CollectionAssert.IsSubsetOf(
            new[]
            {
                AutomationActionKind.OpenEditor,
                AutomationActionKind.CopyImageToClipboard,
                AutomationActionKind.RunOcr,
                AutomationActionKind.RedactDetectedSensitiveData,
                AutomationActionKind.ShareDefaultDestination,
                AutomationActionKind.RunCustomScript,
                AutomationActionKind.CallCustomWebhook,
                AutomationActionKind.GenerateDocument,
                AutomationActionKind.ShowNotification
            },
            Enum.GetValues<AutomationActionKind>());
    }

    [TestMethod]
    public void ProviderCatalog_IncludesCurrentAndRoadmapShareDestinations()
    {
        var providers = ShareProviderCatalog.CreateDefault();
        var names = providers.Select(provider => provider.ProviderName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        CollectionAssert.IsSubsetOf(
            new[]
            {
                "Google Drive",
                "Google Photos",
                "OneDrive",
                "Dropbox",
                "S3-compatible",
                "SFTP",
                "Imgur",
                "Cloudinary",
                "Linear",
                "GitHub Issues",
                "Jira",
                "Slack",
                "Discord",
                "Microsoft Teams",
                "WebDAV",
                "YouTube",
                "OneNote",
                "Azure DevOps",
                "FTP/FTPS"
            },
            names.ToList());
    }

    [TestMethod]
    public async Task ProviderCatalog_MarksNonOAuthExpansionProvidersImplemented()
    {
        var settings = new AppSettings
        {
            SlackWebhookUrl = "https://hooks.slack.example.test/services/test",
            DiscordWebhookUrl = "https://discord.example.test/api/webhooks/test",
            TeamsWebhookUrl = "https://teams.example.test/webhook"
        };
        var providers = ShareProviderCatalog.CreateDefault(settings)
            .Where(provider => new[]
            {
                "GitHub Issues",
                "Jira",
                "Slack",
                "Discord",
                "Microsoft Teams",
                "WebDAV",
                "Azure DevOps",
                "FTP/FTPS"
            }.Contains(provider.ProviderName, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.AreEqual(8, providers.Count);
        foreach (var provider in providers)
        {
            var health = await provider.ValidateCredentialsAsync(CancellationToken.None);
            Assert.IsTrue(health.IsHealthy, $"{provider.ProviderName}: {health.Message}");
        }
    }
}
