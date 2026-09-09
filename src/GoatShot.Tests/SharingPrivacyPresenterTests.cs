using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class SharingPrivacyPresenterTests
{
    [TestMethod]
    public void LocalOnlyRulesDoNotClaimUnattendedTransfers()
    {
        var settings = new AppSettings
        {
            AutomationRules = [new AutomationRule { Actions = [AutomationActionKind.RunOcr, AutomationActionKind.ShareDefaultDestination] }]
        };
        Assert.IsFalse(SharingPrivacyPresenter.Build(settings).UnattendedTransfersEnabled);
    }

    [TestMethod]
    public void ExternalRulesAndScriptsAreVisibleEvenWithUploadConfirmationEnabled()
    {
        var settings = new AppSettings
        {
            AutomationRules =
            [
                new AutomationRule { Actions = [AutomationActionKind.RunCustomScript] },
                new AutomationRule { Actions = [AutomationActionKind.CallCustomWebhook] },
                new AutomationRule { IsEnabled = false, Actions = [AutomationActionKind.CallCustomWebhook] }
            ]
        };
        var status = SharingPrivacyPresenter.Build(settings);
        Assert.IsTrue(status.UnattendedTransfersEnabled);
        StringAssert.Contains(status.Summary, "2 external-transfer rules");
    }

    [TestMethod]
    public void VirtualPrinterSharingAndBackgroundQueueAreReported()
    {
        var settings = new AppSettings
        {
            DefaultShareDestination = "Custom webhook",
            EnableVirtualPrinterImport = true,
            WatchFolderShareAfterImport = true
        };
        Assert.IsTrue(SharingPrivacyPresenter.Build(settings).UnattendedTransfersEnabled);
        settings.EnableVirtualPrinterImport = false;
        Assert.IsFalse(SharingPrivacyPresenter.Build(settings).UnattendedTransfersEnabled);
        settings.UploadQueue.EnableQueue = true;
        settings.UploadQueue.EnableBackgroundProcessing = true;
        StringAssert.Contains(SharingPrivacyPresenter.Build(settings).Summary, "background upload queue");
        settings.UploadQueue.EnableQueue = false;
        Assert.IsFalse(SharingPrivacyPresenter.Build(settings).UnattendedTransfersEnabled);
    }
}
