using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed record SharingPrivacyStatus(bool UnattendedTransfersEnabled, string Summary);

/// <summary>Describes configured transfer paths without claiming that a transfer is running.</summary>
public static class SharingPrivacyPresenter
{
    public static SharingPrivacyStatus Build(AppSettings settings)
    {
        var externalDefault = ShareService.IsExternalDestination(ShareService.ParseDestination(settings.DefaultShareDestination));
        var rules = settings.AutomationRules.Count(rule => rule.IsEnabled && rule.Actions.Any(action =>
            action is AutomationActionKind.RunCustomScript or AutomationActionKind.CallCustomWebhook ||
            (action == AutomationActionKind.ShareDefaultDestination && externalDefault)));
        var imports = settings.WatchFolderShareAfterImport && externalDefault &&
            ((settings.EnableWatchFolders && settings.WatchFolderAutoImport && settings.WatchFolders.Count > 0) ||
             settings.EnableVirtualPrinterImport);
        var queue = settings.UploadQueue.EnableQueue && settings.UploadQueue.EnableBackgroundProcessing;
        var enabled = rules > 0 || imports || queue;
        var sources = new List<string>();
        if (rules > 0) sources.Add($"{rules} external-transfer rule{(rules == 1 ? string.Empty : "s")}");
        if (imports) sources.Add("automatic sharing after import");
        if (queue) sources.Add("background upload queue");
        return new SharingPrivacyStatus(enabled, enabled
            ? $"Unattended transfers enabled: {string.Join(", ", sources)}. Matching items can be sent without another prompt."
            : "Unattended transfers are off. You control each share and AI action.");
    }
}
