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

    [TestMethod]
    public void IsRecoverableClipboardCopyFailure_CoversEveryWayTheCopyItselfCanFail()
    {
        // Each of these has been observed from Clipboard.SetImage or BitmapImage file loads:
        // busy clipboard, locked or missing file, unrecognized codec, denied access, and a
        // truncated image file — which throws FileFormatException, a FormatException rather
        // than an IOException.
        Assert.IsTrue(CaptureFeedbackPolicy.IsRecoverableClipboardCopyFailure(
            new System.Runtime.InteropServices.COMException()));
        Assert.IsTrue(CaptureFeedbackPolicy.IsRecoverableClipboardCopyFailure(new IOException()));
        Assert.IsTrue(CaptureFeedbackPolicy.IsRecoverableClipboardCopyFailure(new NotSupportedException()));
        Assert.IsTrue(CaptureFeedbackPolicy.IsRecoverableClipboardCopyFailure(new UnauthorizedAccessException()));
        Assert.IsTrue(CaptureFeedbackPolicy.IsRecoverableClipboardCopyFailure(new FileFormatException()));
    }

    [TestMethod]
    public void IsRecoverableClipboardCopyFailure_StillSurfacesGenuineBugs()
    {
        // A NullReferenceException or similar is a programming error, not a flaky copy; swallowing
        // it would hide real defects behind a "Saved" balloon.
        Assert.IsFalse(CaptureFeedbackPolicy.IsRecoverableClipboardCopyFailure(new NullReferenceException()));
        Assert.IsFalse(CaptureFeedbackPolicy.IsRecoverableClipboardCopyFailure(new InvalidOperationException()));
    }
}
