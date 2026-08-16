using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class CaptureTaskAutoDismissTests
{
    [TestMethod]
    public void NormalizeSeconds_KeepsUsableDelaysUntouched()
    {
        Assert.AreEqual(8, CaptureTaskAutoDismiss.NormalizeSeconds(8));
        Assert.AreEqual(1, CaptureTaskAutoDismiss.NormalizeSeconds(1));
        Assert.AreEqual(CaptureTaskAutoDismiss.MaxSeconds, CaptureTaskAutoDismiss.NormalizeSeconds(CaptureTaskAutoDismiss.MaxSeconds));
    }

    [TestMethod]
    public void NormalizeSeconds_TreatsNegativeDelaysAsDisabledRatherThanInstant()
    {
        // A negative value in a hand-edited settings file must not dismiss the window immediately.
        Assert.AreEqual(0, CaptureTaskAutoDismiss.NormalizeSeconds(-1));
        Assert.AreEqual(0, CaptureTaskAutoDismiss.NormalizeSeconds(int.MinValue));
    }

    [TestMethod]
    public void NormalizeSeconds_ClampsAbsurdlyLongDelays()
    {
        Assert.AreEqual(CaptureTaskAutoDismiss.MaxSeconds, CaptureTaskAutoDismiss.NormalizeSeconds(int.MaxValue));
    }

    [TestMethod]
    public void IsEnabled_OnlyWhenAPositiveDelaySurvivesNormalization()
    {
        Assert.IsTrue(CaptureTaskAutoDismiss.IsEnabled(8));
        Assert.IsFalse(CaptureTaskAutoDismiss.IsEnabled(0), "Zero is the documented 'stay open' value.");
        Assert.IsFalse(CaptureTaskAutoDismiss.IsEnabled(-5));
    }

    [TestMethod]
    public void DefaultSeconds_IsTheShippedEightSecondDelay()
    {
        Assert.AreEqual(8, CaptureTaskAutoDismiss.DefaultSeconds);
        Assert.IsTrue(CaptureTaskAutoDismiss.IsEnabled(CaptureTaskAutoDismiss.DefaultSeconds));
    }
}
