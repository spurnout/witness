using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ReplayHotkeyTests
{
    [TestMethod]
    [DataRow("Ctrl+Shift+PrintScreen")]
    [DataRow("Ctrl+Alt+Shift+R")]
    [DataRow("Alt+F12")]
    [DataRow("Ctrl+7")]
    public void TryParseGesture_AcceptsSupportedConfigurableReplayGestures(string gesture)
    {
        Assert.IsTrue(HotkeyService.TryParseGesture(gesture, out var modifiers, out var key));
        Assert.AreNotEqual(0u, key);
        Assert.AreNotEqual(0u, modifiers);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("Ctrl+NoSuchKey")]
    [DataRow("Ctrl+R+S")]
    public void TryParseGesture_RejectsInvalidReplayGestures(string gesture)
    {
        Assert.IsFalse(HotkeyService.TryParseGesture(gesture, out _, out _));
    }
}
