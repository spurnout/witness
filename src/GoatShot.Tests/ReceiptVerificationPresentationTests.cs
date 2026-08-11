using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ReceiptVerificationPresentationTests
{
    [TestMethod]
    [DataRow(ReceiptVerificationStatus.IntactKnownDevice, "Intact — known device key", 0)]
    [DataRow(ReceiptVerificationStatus.IntactUnknownDevice, "Intact — unknown device key", 0)]
    [DataRow(ReceiptVerificationStatus.Modified, "Modified", 1)]
    [DataRow(ReceiptVerificationStatus.Incomplete, "Incomplete", 1)]
    [DataRow(ReceiptVerificationStatus.Unverifiable, "Unverifiable", 1)]
    public void StatusMapping_UsesApprovedLabelAndExitCode(
        ReceiptVerificationStatus status,
        string expectedLabel,
        int expectedExitCode)
    {
        Assert.AreEqual(expectedLabel, ReceiptVerificationPresentation.FormatStatus(status));
        Assert.AreEqual(expectedExitCode, ReceiptVerificationPresentation.ExitCode(status));
    }

    [TestMethod]
    public void StatusMapping_CoversExactlyTheFiveOfflineVerificationStates()
    {
        var statuses = Enum.GetValues<ReceiptVerificationStatus>();
        var labels = statuses
            .Select(ReceiptVerificationPresentation.FormatStatus)
            .ToArray();

        Assert.AreEqual(5, statuses.Length);
        Assert.AreEqual(5, labels.Distinct(StringComparer.Ordinal).Count());
    }
}
