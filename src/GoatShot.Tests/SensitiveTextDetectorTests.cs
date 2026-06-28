using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class SensitiveTextDetectorTests
{
    [TestMethod]
    public void Scan_RedactsCommonSecretsWithoutReturningRawValuesInSummary()
    {
        const string text = "Email matt@example.com used token=abcdefghijklmnopqrstuvwxyz123456 and card 4111 1111 1111 1111.";

        var result = SensitiveTextDetector.Scan(text);

        CollectionAssert.Contains(result.Findings.Select(finding => finding.Kind).ToList(), "email address");
        CollectionAssert.Contains(result.Findings.Select(finding => finding.Kind).ToList(), "API key or password field");
        CollectionAssert.Contains(result.Findings.Select(finding => finding.Kind).ToList(), "credit-card-like value");
        StringAssert.Contains(result.RedactedText, "[REDACTED:email-address]");
        StringAssert.Contains(result.RedactedText, "[REDACTED:api-key-or-password-field]");
        StringAssert.Contains(result.RedactedText, "[REDACTED:credit-card-like-value]");
        Assert.IsFalse(result.Summary.Contains("matt@example.com", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(result.Summary.Contains("4111", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Scan_ReturnsNoFindingSummaryForBenignText()
    {
        var result = SensitiveTextDetector.Scan("This screenshot shows a public release checklist.");

        Assert.AreEqual(0, result.Findings.Count);
        Assert.AreEqual("No sensitive data detected.", result.Summary);
    }

    [TestMethod]
    public void Scan_DetectsPhoneNumbersButIgnoresGeneratedHexTokens()
    {
        var phone = SensitiveTextDetector.Scan("Call (555) 123-4567 after review.");
        CollectionAssert.Contains(phone.Findings.Select(finding => finding.Kind).ToList(), "phone number");

        var generatedPath = SensitiveTextDetector.Scan(
            "Path: goatshot-manual-validation-desktop-proof-test-abcdef1234567890abcdef1234567890");
        Assert.IsFalse(generatedPath.Findings.Any(finding => finding.Kind == "phone number"));
    }
}
