using System.Windows.Input;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class KeybindGestureReaderTests
{
    [TestMethod]
    public void TryBuild_ComposesModifiersInCanonicalOrder()
    {
        Assert.AreEqual(
            "Ctrl+Shift+R",
            KeybindGestureReader.TryBuild(Key.R, ModifierKeys.Shift | ModifierKeys.Control));
        Assert.AreEqual(
            "Ctrl+Alt+Shift+R",
            KeybindGestureReader.TryBuild(Key.R, ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Control));
    }

    [TestMethod]
    public void TryBuild_MapsDigitsAndFunctionKeys()
    {
        Assert.AreEqual("Ctrl+7", KeybindGestureReader.TryBuild(Key.D7, ModifierKeys.Control));
        Assert.AreEqual("Alt+F12", KeybindGestureReader.TryBuild(Key.F12, ModifierKeys.Alt));
    }

    [TestMethod]
    public void TryBuild_AllowsPrintScreenAndFunctionKeysWithoutModifiers()
    {
        Assert.AreEqual("PrintScreen", KeybindGestureReader.TryBuild(Key.Snapshot, ModifierKeys.None));
        Assert.AreEqual("F9", KeybindGestureReader.TryBuild(Key.F9, ModifierKeys.None));
    }

    [TestMethod]
    public void TryBuild_RejectsBareLettersAndDigitsThatWouldHijackTyping()
    {
        Assert.IsNull(KeybindGestureReader.TryBuild(Key.R, ModifierKeys.None));
        Assert.IsNull(KeybindGestureReader.TryBuild(Key.D7, ModifierKeys.None));
    }

    [TestMethod]
    public void TryBuild_RejectsChordsUsingTheWindowsKey()
    {
        Assert.IsNull(KeybindGestureReader.TryBuild(Key.R, ModifierKeys.Windows));
        Assert.IsNull(KeybindGestureReader.TryBuild(Key.R, ModifierKeys.Control | ModifierKeys.Windows));
    }

    [TestMethod]
    public void TryBuild_RejectsKeysTheGestureParserCannotRepresent()
    {
        Assert.IsNull(KeybindGestureReader.TryBuild(Key.NumPad5, ModifierKeys.Control));
        Assert.IsNull(KeybindGestureReader.TryBuild(Key.OemComma, ModifierKeys.Control));
        Assert.IsNull(KeybindGestureReader.TryBuild(Key.Tab, ModifierKeys.Control));
    }

    [TestMethod]
    public void TryBuild_OnlyEmitsGesturesTheCatalogAcceptsBack()
    {
        Key[] keys = [Key.A, Key.Z, Key.D0, Key.D9, Key.F1, Key.F24, Key.Snapshot];
        foreach (var key in keys)
        {
            var gesture = KeybindGestureReader.TryBuild(key, ModifierKeys.Control);
            Assert.IsNotNull(gesture, $"{key} should map to a gesture.");
            Assert.IsTrue(KeybindCatalog.IsValidGesture(gesture), $"'{gesture}' must round-trip through the parser.");
            Assert.AreEqual(gesture, KeybindCatalog.NormalizeGesture(gesture), "Reader output must already be canonical.");
        }
    }

    [TestMethod]
    public void Describe_NamesSupportedKeysAndReturnsNullForUnsupportedOnes()
    {
        Assert.AreEqual("R", KeybindGestureReader.Describe(Key.R));
        Assert.AreEqual("7", KeybindGestureReader.Describe(Key.D7));
        Assert.AreEqual("PrintScreen", KeybindGestureReader.Describe(Key.Snapshot));
        Assert.IsNull(KeybindGestureReader.Describe(Key.OemComma));
    }
}
