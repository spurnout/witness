using GoatShot.App.Models;

namespace GoatShot.App.Services;

public static class ReceiptVerificationPresentation
{
    public static string Label(ReceiptVerificationStatus status) => FormatStatus(status);

    public static string FormatStatus(ReceiptVerificationStatus status) => status switch
    {
        ReceiptVerificationStatus.IntactKnownDevice => "Intact — known device key",
        ReceiptVerificationStatus.IntactUnknownDevice => "Intact — unknown device key",
        ReceiptVerificationStatus.Modified => "Modified",
        ReceiptVerificationStatus.Incomplete => "Incomplete",
        ReceiptVerificationStatus.Unverifiable => "Unverifiable",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown receipt verification status.")
    };

    public static int ExitCode(ReceiptVerificationStatus status) => status switch
    {
        ReceiptVerificationStatus.IntactKnownDevice or
        ReceiptVerificationStatus.IntactUnknownDevice => 0,
        ReceiptVerificationStatus.Modified or
        ReceiptVerificationStatus.Incomplete or
        ReceiptVerificationStatus.Unverifiable => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown receipt verification status.")
    };
}
