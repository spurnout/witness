using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class TrayMenuActionCatalogTests
{
    [TestMethod]
    public void All_PreservesExpectedTrayMenuShape()
    {
        Assert.AreEqual(25, TrayMenuActionCatalog.All.Count);
        Assert.AreEqual(21, TrayMenuActionCatalog.Actions.Count());
        Assert.AreEqual(4, TrayMenuActionCatalog.All.Count(item => item.IsSeparator));
        Assert.AreEqual("Capture region", TrayMenuActionCatalog.All.First().Label);
        Assert.AreEqual("Exit", TrayMenuActionCatalog.All.Last().Label);
        CollectionAssert.AreEqual(
            new[] { "Capture", "Record", "Tools", "Workspace", "System" },
            TrayMenuActionCatalog.Actions
                .Select(action => action.Group)
                .Distinct()
                .ToArray());
    }

    [TestMethod]
    public void Actions_HaveLabelsGroupsAndKinds()
    {
        foreach (var action in TrayMenuActionCatalog.Actions)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(action.Label));
            Assert.IsFalse(string.IsNullOrWhiteSpace(action.Group));
            Assert.IsTrue(action.ActionKind.HasValue);
        }
    }

    [TestMethod]
    public void HotkeyFor_MapsEveryTrayEntryThatHasAGlobalShortcut()
    {
        Assert.AreEqual(HotkeyAction.RegionCapture, TrayMenuActionCatalog.HotkeyFor(TrayMenuActionKind.CaptureRegion));
        Assert.AreEqual(HotkeyAction.ColorPicker, TrayMenuActionCatalog.HotkeyFor(TrayMenuActionKind.PickColor));
        Assert.AreEqual(HotkeyAction.OcrRegion, TrayMenuActionCatalog.HotkeyFor(TrayMenuActionKind.OcrTextGrab));
        Assert.AreEqual(HotkeyAction.SaveReplay, TrayMenuActionCatalog.HotkeyFor(TrayMenuActionKind.SaveReplay));

        // Entries with no global shortcut must report none rather than borrowing another action's.
        Assert.IsNull(TrayMenuActionCatalog.HotkeyFor(TrayMenuActionKind.Exit));
        Assert.IsNull(TrayMenuActionCatalog.HotkeyFor(TrayMenuActionKind.OpenSettings));
    }

    [TestMethod]
    public void HotkeyFor_NeverPointsTwoTrayEntriesAtTheSameShortcut()
    {
        var mapped = TrayMenuActionCatalog.Actions
            .Select(action => TrayMenuActionCatalog.HotkeyFor(action.ActionKind!.Value))
            .Where(hotkey => hotkey.HasValue)
            .Select(hotkey => hotkey!.Value)
            .ToArray();

        Assert.AreEqual(mapped.Length, mapped.Distinct().Count());
        Assert.IsTrue(
            mapped.All(hotkey => KeybindCatalog.Definitions.Any(definition => definition.Action == hotkey)),
            "Every mapped shortcut must exist in the keybind catalog.");
    }

    [TestMethod]
    public void Actions_CoverEveryActionKindOnce()
    {
        var actionKinds = TrayMenuActionCatalog.Actions
            .Select(action => action.ActionKind!.Value)
            .ToArray();

        CollectionAssert.AreEquivalent(
            Enum.GetValues<TrayMenuActionKind>(),
            actionKinds);
        Assert.AreEqual(actionKinds.Length, actionKinds.Distinct().Count());
    }
}
