using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ShareTaskWindowModelsTests
{
    [TestMethod]
    public void ShouldConfirm_OnlyWhenSettingEnabledAndDestinationIsExternal()
    {
        var settings = new AppSettings { ConfirmBeforeUpload = true };

        Assert.IsTrue(ShareTaskWindowModels.ShouldConfirm(settings, ShareDestination.CustomWebhook));
        Assert.IsTrue(ShareTaskWindowModels.ShouldConfirm(settings, ShareDestination.GoogleDrive));
        Assert.IsFalse(ShareTaskWindowModels.ShouldConfirm(settings, ShareDestination.LocalFolder));
        Assert.IsFalse(ShareTaskWindowModels.ShouldConfirm(settings, ShareDestination.ClipboardImage));

        settings.ConfirmBeforeUpload = false;

        Assert.IsFalse(ShareTaskWindowModels.ShouldConfirm(settings, ShareDestination.CustomWebhook));
    }

    [TestMethod]
    public void BuildConfirmation_IncludesDestinationMetadataAndPrivacyMarkers()
    {
        var item = new CaptureItem
        {
            Id = "capture-1",
            Kind = CaptureKind.ActiveWindow,
            FilePath = @"C:\captures\shot.png",
            Bytes = 2048,
            Width = 800,
            Height = 600,
            IsPrivate = true,
            SourceApp = "Browser",
            SourceWindowTitle = "Private issue tracker",
            OcrText = "some recognized text"
        };

        var model = ShareTaskWindowModels.BuildConfirmation(item, ShareDestination.CustomWebhook);

        Assert.AreEqual("Custom webhook", model.Destination);
        Assert.AreEqual("shot.png", model.FileName);
        Assert.AreEqual("ActiveWindow", model.CaptureType);
        Assert.AreEqual("2 KB", model.Size);
        StringAssert.Contains(model.RiskSummary, "Posts the capture file");
        StringAssert.Contains(model.PrivacySummary, "Marked private");
        StringAssert.Contains(model.PrivacySummary, "Contains OCR text");
        StringAssert.Contains(model.PrivacySummary, "Source app: Browser");
        StringAssert.Contains(model.PrivacySummary, "Window title captured");
    }

    [TestMethod]
    public void BuildResult_ProvidesLinkActionsAndRetryState()
    {
        var item = new CaptureItem
        {
            Kind = CaptureKind.Imported,
            FilePath = @"C:\captures\shot[1].png",
            Bytes = 1024
        };
        var success = ShareTaskWindowModels.BuildResult(
            item,
            ShareDestination.GitHubIssues,
            new ShareResult
            {
                Succeeded = true,
                Url = "https://example.test/issue/1",
                Message = "created"
            },
            @"C:\temp\qr.png");
        var failure = ShareTaskWindowModels.BuildResult(
            item,
            ShareDestination.CustomWebhook,
            new ShareResult
            {
                Succeeded = false,
                Message = "failed"
            });
        var nonWebLink = ShareTaskWindowModels.BuildResult(
            item,
            ShareDestination.CustomScript,
            new ShareResult
            {
                Succeeded = true,
                Url = @"C:\captures\shared.png",
                Message = "exported"
            });

        Assert.AreEqual("Share complete", success.Title);
        Assert.AreEqual("GitHub Issues", success.Destination);
        Assert.IsTrue(success.CanOpenLink);
        Assert.IsFalse(success.CanRetry);
        Assert.AreEqual(@"C:\temp\qr.png", success.QrCodePath);
        Assert.AreEqual(@"[shot\[1\].png](https://example.test/issue/1)", success.MarkdownLink);

        Assert.AreEqual("Share failed", failure.Title);
        Assert.AreEqual("Custom webhook", failure.Destination);
        Assert.IsTrue(failure.CanRetry);
        Assert.IsFalse(failure.CanOpenLink);
        Assert.IsNull(failure.MarkdownLink);

        Assert.AreEqual(@"[shot\[1\].png](C:\captures\shared.png)", nonWebLink.MarkdownLink);
        Assert.IsFalse(nonWebLink.CanOpenLink);
    }
}
