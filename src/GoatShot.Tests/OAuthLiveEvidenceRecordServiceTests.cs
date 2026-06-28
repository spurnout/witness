using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class OAuthLiveEvidenceRecordServiceTests
{
    [TestMethod]
    public async Task RecordAsync_PassedRequiresAllEvidenceCategoriesAndWritesRedactedRecord()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var result = await new OAuthLiveEvidenceRecordService().RecordAsync(new OAuthLiveEvidenceRecordRequest
            {
                Providers = ConfiguredProviders(),
                ProviderName = "Google Drive",
                Status = "passed",
                OutputPath = root,
                OperatorName = "QA Operator",
                Note = "reviewed token=super-secret-token-1234567890",
                Evidence =
                {
                    Evidence("consent", "consent-screen-redacted.md"),
                    Evidence("exchange", "exchange-log-redacted.txt"),
                    Evidence("refresh", "refresh-result-redacted.txt"),
                    Evidence("upload", "upload-proof-redacted.json"),
                    Evidence("cleanup", "cleanup-note.md"),
                    Evidence("account", "account-diagnostics-redacted.json")
                }
            });

            Assert.IsTrue(result.Succeeded, string.Join(Environment.NewLine, result.Issues));
            Assert.IsTrue(result.ProofComplete);
            Assert.AreEqual("google-drive", result.ProviderKind);
            Assert.AreEqual(6, result.Evidence.Count);
            Assert.AreEqual(0, result.MissingRequiredCategories.Count);
            Assert.IsFalse(result.WouldOpenBrowser);
            Assert.IsFalse(result.WouldContactProvider);
            Assert.IsFalse(result.WouldExchangeCode);
            Assert.IsFalse(result.WouldStoreToken);
            Assert.IsFalse(result.WouldRefreshToken);
            Assert.IsFalse(result.WouldUploadFile);
            Assert.IsFalse(result.WouldDeleteRemoteFile);
            AssertGeneratedFile(root, "google-drive-live-evidence.md");
            AssertGeneratedFile(root, "google-drive-live-evidence.json");

            var generatedText = string.Join(
                Environment.NewLine,
                Directory.GetFiles(root, "*.*", SearchOption.AllDirectories).Select(File.ReadAllText));
            Assert.IsFalse(generatedText.Contains("super-secret-token", StringComparison.Ordinal));
            StringAssert.Contains(generatedText, "REDACTED");
            StringAssert.Contains(generatedText, "Proof complete: `True`");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [TestMethod]
    public async Task RecordAsync_PassedWithMissingEvidenceFailsButWritesRecord()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var result = await new OAuthLiveEvidenceRecordService().RecordAsync(new OAuthLiveEvidenceRecordRequest
            {
                Providers = ConfiguredProviders(),
                ProviderName = "Dropbox",
                Status = "passed",
                OutputPath = root,
                Evidence =
                {
                    Evidence("consent", "consent.md")
                }
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.ProofComplete);
            CollectionAssert.Contains(result.MissingRequiredCategories, "exchange");
            CollectionAssert.Contains(result.MissingRequiredCategories, "refresh");
            CollectionAssert.Contains(result.MissingRequiredCategories, "upload");
            CollectionAssert.Contains(result.MissingRequiredCategories, "cleanup");
            CollectionAssert.Contains(result.MissingRequiredCategories, "account");
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("Passed OAuth live evidence requires", StringComparison.OrdinalIgnoreCase)));
            AssertGeneratedFile(root, "dropbox-live-evidence.md");
            AssertGeneratedFile(root, "dropbox-live-evidence.json");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [TestMethod]
    public async Task RecordAsync_BlockedRequiresNote()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var result = await new OAuthLiveEvidenceRecordService().RecordAsync(new OAuthLiveEvidenceRecordRequest
            {
                Providers = ConfiguredProviders(),
                ProviderName = "OneDrive",
                Status = "blocked",
                OutputPath = root
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("require --note", StringComparison.OrdinalIgnoreCase)));
            AssertGeneratedFile(root, "onedrive-live-evidence.md");
            AssertGeneratedFile(root, "onedrive-live-evidence.json");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [TestMethod]
    public async Task RecordAsync_ClassifiesGooglePhotosYouTubeAndOneNoteProviderKinds()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var googlePhotos = await new OAuthLiveEvidenceRecordService().RecordAsync(new OAuthLiveEvidenceRecordRequest
            {
                Providers = ConfiguredProviders(),
                ProviderName = "Google Photos",
                Status = "pending",
                OutputPath = root
            });
            var youtube = await new OAuthLiveEvidenceRecordService().RecordAsync(new OAuthLiveEvidenceRecordRequest
            {
                Providers = ConfiguredProviders(),
                ProviderName = "YouTube",
                Status = "pending",
                OutputPath = root
            });
            var oneNote = await new OAuthLiveEvidenceRecordService().RecordAsync(new OAuthLiveEvidenceRecordRequest
            {
                Providers = ConfiguredProviders(),
                ProviderName = "OneNote",
                Status = "pending",
                OutputPath = root
            });

            Assert.IsTrue(googlePhotos.Succeeded, string.Join(Environment.NewLine, googlePhotos.Issues));
            Assert.IsTrue(youtube.Succeeded, string.Join(Environment.NewLine, youtube.Issues));
            Assert.IsTrue(oneNote.Succeeded, string.Join(Environment.NewLine, oneNote.Issues));
            Assert.AreEqual("google-photos", googlePhotos.ProviderKind);
            Assert.AreEqual("youtube", youtube.ProviderKind);
            Assert.AreEqual("onenote", oneNote.ProviderKind);
            AssertGeneratedFile(root, "google-photos-live-evidence.md");
            AssertGeneratedFile(root, "youtube-live-evidence.md");
            AssertGeneratedFile(root, "onenote-live-evidence.md");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [TestMethod]
    public async Task RecordAsync_UnknownProviderFailsWithoutContactingProvider()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var result = await new OAuthLiveEvidenceRecordService().RecordAsync(new OAuthLiveEvidenceRecordRequest
            {
                Providers = ConfiguredProviders(),
                ProviderName = "Unknown Provider",
                Status = "pending",
                OutputPath = root
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.WouldContactProvider);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("was not found", StringComparison.OrdinalIgnoreCase)));
            AssertGeneratedFile(root, "unknown-provider-live-evidence.md");
            AssertGeneratedFile(root, "unknown-provider-live-evidence.json");
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    private static OAuthLiveEvidenceInput Evidence(string category, string value) => new()
    {
        Category = category,
        Value = value
    };

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

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
