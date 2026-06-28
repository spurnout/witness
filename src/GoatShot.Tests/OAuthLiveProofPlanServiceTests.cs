using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class OAuthLiveProofPlanServiceTests
{
    [TestMethod]
    public async Task CreateAsync_AllConfiguredProvidersWritesPlanWithoutContactingProvider()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var result = await new OAuthLiveProofPlanService().CreateAsync(new OAuthLiveProofPlanRequest
            {
                Providers = ConfiguredProviders(),
                OutputPath = root,
                CallbackUri = "http://127.0.0.1:53628/oauth/callback"
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues.Concat(result.Providers.SelectMany(provider => provider.Issues))));
            Assert.IsFalse(result.WouldOpenBrowser);
            Assert.IsFalse(result.WouldContactProvider);
            Assert.IsFalse(result.WouldExchangeCode);
            Assert.IsFalse(result.WouldStoreToken);
            Assert.IsFalse(result.WouldRefreshToken);
            Assert.IsFalse(result.WouldUploadFile);
            Assert.IsFalse(result.WouldDeleteRemoteFile);
            Assert.AreEqual(6, result.Providers.Count);
            Assert.IsTrue(result.Providers.All(provider => provider.Status == "manual-live-proof-plan-ready"));
            Assert.IsTrue(result.Providers.All(provider => provider.RequiredEvidence.Any(item => item.Contains("consent screen", StringComparison.OrdinalIgnoreCase))));
            Assert.IsTrue(result.Providers.All(provider => provider.ScopeReview.Count > 0));
            Assert.IsTrue(result.Providers.All(provider => provider.ConsentScreenChecklist.Count > 0));
            Assert.IsTrue(result.Providers.All(provider => provider.AccountDiagnostics.Count > 0));
            Assert.IsTrue(result.Providers.All(provider => !string.IsNullOrWhiteSpace(provider.CleanupBoundary)));
            Assert.IsTrue(result.NonGoals.Any(item => item.Contains("No provider API is contacted", StringComparison.OrdinalIgnoreCase)));
            AssertProviderProfile(
                result,
                "Google Drive",
                "google-drive",
                "--google-drive-folder-id <safe-proof-folder-id>");
            AssertProviderProfile(
                result,
                "Google Photos",
                "google-photos",
                "--google-photos-album-id <safe-proof-album-id>");
            AssertProviderProfile(
                result,
                "OneDrive",
                "onedrive",
                "--onedrive-folder /GoatShotProof");
            AssertProviderProfile(
                result,
                "Dropbox",
                "dropbox",
                "--dropbox-folder /GoatShotProof");
            AssertProviderProfile(
                result,
                "YouTube",
                "youtube",
                "--provider \"YouTube\"");
            AssertProviderProfile(
                result,
                "OneNote",
                "onenote",
                "--provider \"OneNote\"");

            AssertGeneratedFile(root, "oauth-live-proof-plan.md");
            AssertGeneratedFile(root, "oauth-live-proof-plan.json");
            var markdown = await File.ReadAllTextAsync(Path.Combine(root, "oauth-live-proof-plan.md"));
            StringAssert.Contains(markdown, "Would contact provider: `False`");
            StringAssert.Contains(markdown, "### Scope Review");
            StringAssert.Contains(markdown, "### Consent Screen Checklist");
            StringAssert.Contains(markdown, "### Account Diagnostics");
            StringAssert.Contains(markdown, "Google Drive");
            StringAssert.Contains(markdown, "Google Photos");
            StringAssert.Contains(markdown, "YouTube");
            StringAssert.Contains(markdown, "OneNote");
            StringAssert.Contains(markdown, "--google-drive-folder-id <safe-proof-folder-id>");
            StringAssert.Contains(markdown, "--google-photos-album-id <safe-proof-album-id>");
            StringAssert.Contains(markdown, "goatshot oauth exchange");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [TestMethod]
    public async Task CreateAsync_MissingClientIdBlocksProviderButWritesPlan()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var provider = ConfiguredProviders().Single(provider => provider.ProviderName == "Dropbox");
            provider.ClientId = string.Empty;

            var result = await new OAuthLiveProofPlanService().CreateAsync(new OAuthLiveProofPlanRequest
            {
                Providers = [provider],
                OutputPath = root
            });

            Assert.IsFalse(result.Succeeded);
            var entry = result.Providers.Single();
            Assert.AreEqual("blocked-before-live-proof", entry.Status);
            Assert.IsFalse(entry.ClientIdConfigured);
            Assert.IsTrue(entry.Issues.Any(issue => issue.Contains("client ID", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(entry.WouldContactProvider);
            AssertGeneratedFile(root, "oauth-live-proof-plan.md");
            AssertGeneratedFile(root, "oauth-live-proof-plan.json");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [TestMethod]
    public async Task CreateAsync_RedactsCallbackAndPolicyReasonSecrets()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var result = await new OAuthLiveProofPlanService().CreateAsync(new OAuthLiveProofPlanRequest
            {
                Providers = ConfiguredProviders().Take(1).ToArray(),
                OutputPath = root,
                CallbackUri = "http://127.0.0.1:53628/oauth/callback?code=super-secret-code-1234567890",
                PolicyAllowed = false,
                PolicyReason = "blocked token=super-secret-token-1234567890"
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.PolicyAllowed);
            StringAssert.Contains(result.CallbackUri, "REDACTED");
            StringAssert.Contains(result.PolicyReason, "REDACTED");
            Assert.IsTrue(result.Providers.Single().Issues.Any(issue => issue.Contains("REDACTED", StringComparison.OrdinalIgnoreCase)));

            var generatedText = string.Join(
                Environment.NewLine,
                Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                    .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    .Select(File.ReadAllText));
            Assert.IsFalse(generatedText.Contains("super-secret-code", StringComparison.Ordinal));
            Assert.IsFalse(generatedText.Contains("super-secret-token", StringComparison.Ordinal));
            StringAssert.Contains(generatedText, "REDACTED");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    private static OAuthProviderSettings[] ConfiguredProviders() =>
    [
        new()
        {
            ProviderName = "Google Drive",
            ClientId = "google-client-id",
            AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
            TokenEndpoint = "https://oauth2.googleapis.com/token",
            Scopes = "https://www.googleapis.com/auth/drive.file",
            UsePkce = true
        },
        new()
        {
            ProviderName = "Google Photos",
            ClientId = "google-photos-client-id",
            AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
            TokenEndpoint = "https://oauth2.googleapis.com/token",
            Scopes = "https://www.googleapis.com/auth/photoslibrary.appendonly",
            UsePkce = true
        },
        new()
        {
            ProviderName = "OneDrive",
            ClientId = "onedrive-client-id",
            AuthorizationEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
            TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
            Scopes = "Files.ReadWrite offline_access",
            UsePkce = true
        },
        new()
        {
            ProviderName = "YouTube",
            ClientId = "youtube-client-id",
            AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
            TokenEndpoint = "https://oauth2.googleapis.com/token",
            Scopes = "https://www.googleapis.com/auth/youtube.upload",
            UsePkce = true
        },
        new()
        {
            ProviderName = "OneNote",
            ClientId = "onenote-client-id",
            AuthorizationEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
            TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
            Scopes = "Notes.Create Files.Read offline_access",
            UsePkce = true
        },
        new()
        {
            ProviderName = "Dropbox",
            ClientId = "dropbox-client-id",
            AuthorizationEndpoint = "https://www.dropbox.com/oauth2/authorize",
            TokenEndpoint = "https://api.dropboxapi.com/oauth2/token",
            Scopes = "files.content.write sharing.write",
            UsePkce = true
        }
    ];

    private static void AssertGeneratedFile(string root, string fileName)
    {
        var path = Path.Combine(root, fileName);
        Assert.IsTrue(File.Exists(path), $"{fileName} was not generated.");
        Assert.IsTrue(new FileInfo(path).Length > 0, $"{fileName} was empty.");
    }

    private static void AssertProviderProfile(
        OAuthLiveProofPlanResult result,
        string providerName,
        string providerKind,
        string uploadCommandFragment)
    {
        var provider = result.Providers.Single(provider => provider.ProviderName == providerName);
        Assert.AreEqual(providerKind, provider.ProviderKind);
        Assert.IsTrue(provider.ScopeReview.Any(item => item.Contains(providerKind, StringComparison.OrdinalIgnoreCase) ||
            item.Contains("scope", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(provider.ConsentScreenChecklist.Any(item => item.Contains("consent", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(provider.AccountDiagnostics.Any(item => item.Contains("diagnostic", StringComparison.OrdinalIgnoreCase) ||
            item.Contains("redirect URI", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(provider.Commands.Any(command => command.Contains(uploadCommandFragment, StringComparison.Ordinal)));
        Assert.IsTrue(provider.RequiredEvidence.Any(item => item.Contains("cleanup", StringComparison.OrdinalIgnoreCase)));
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
