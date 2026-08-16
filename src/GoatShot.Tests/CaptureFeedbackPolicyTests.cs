using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class CaptureFeedbackPolicyTests
{
    [TestMethod]
    public void ShouldShowTrayNotification_WhenTheWorkspaceIsClosedToTheTray()
    {
        Assert.IsTrue(CaptureFeedbackPolicy.ShouldShowTrayNotification(workspaceVisible: false, workspaceMinimized: false));
    }

    [TestMethod]
    public void ShouldShowTrayNotification_WhenTheWorkspaceIsMinimized()
    {
        // WPF reports IsVisible == true for a minimized window, so a naive visibility check
        // silently swallows the only feedback a quiet capture gives.
        Assert.IsTrue(CaptureFeedbackPolicy.ShouldShowTrayNotification(workspaceVisible: true, workspaceMinimized: true));
    }

    [TestMethod]
    public void ShouldNotShowTrayNotification_WhenTheWorkspaceIsOnScreen()
    {
        // The status line already reported the capture; a balloon on top of it is noise.
        Assert.IsFalse(CaptureFeedbackPolicy.ShouldShowTrayNotification(workspaceVisible: true, workspaceMinimized: false));
    }

    [TestMethod]
    public void DescribeQuietCapture_ReportsTheCopyWhenItActuallyLanded()
    {
        Assert.AreEqual(
            "Copied to clipboard: shot.png",
            CaptureFeedbackPolicy.DescribeQuietCapture(copiedToClipboard: true, fileName: "shot.png"));
    }

    [TestMethod]
    public void DescribeQuietCapture_NeverClaimsACopyThatDidNotHappen()
    {
        // The copy can be disabled in settings or lost to a clipboard another process is holding
        // open; either way the balloon must describe what actually happened.
        Assert.AreEqual(
            "Saved: shot.png",
            CaptureFeedbackPolicy.DescribeQuietCapture(copiedToClipboard: false, fileName: "shot.png"));
    }
}
