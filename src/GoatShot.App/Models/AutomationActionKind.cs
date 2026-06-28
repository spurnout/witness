namespace GoatShot.App.Models;

public enum AutomationActionKind
{
    OpenEditor,
    CopyImageToClipboard,
    CopyFileToClipboard,
    CopyPathToClipboard,
    SaveToFolder,
    RunOcr,
    RedactDetectedSensitiveData,
    ApplyImageEffect,
    StripMetadataCopy,
    ShareDefaultDestination,
    RunCustomScript,
    CallCustomWebhook,
    GenerateDocument,
    DeleteLocalFile,
    ShowNotification
}
