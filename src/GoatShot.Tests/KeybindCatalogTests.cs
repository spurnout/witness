using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class KeybindCatalogTests
{
    [TestMethod]
    public void Definitions_CoverEveryHotkeyActionWithParsableDefaults()
    {
        var actions = Enum.GetValues<HotkeyAction>();
        CollectionAssert.AreEquivalent(
            actions,
            KeybindCatalog.Definitions.Select(definition => definition.Action).ToArray());

        foreach (var definition in KeybindCatalog.Definitions)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(definition.Label), $"{definition.Action} needs a label.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(definition.Description), $"{definition.Action} needs a description.");
            Assert.IsTrue(
                KeybindCatalog.IsValidGesture(definition.DefaultGesture),
                $"{definition.Action} default '{definition.DefaultGesture}' must parse.");
        }
    }

    [TestMethod]
    public void Definitions_DoNotShipConflictingDefaults()
    {
        var conflicts = KeybindCatalog.FindConflicts(KeybindCatalog.Resolve(null));
        Assert.AreEqual(0, conflicts.Count, "Shipped defaults must not collide with each other.");
    }

    [TestMethod]
    public void Resolve_FallsBackToDefaultsWhenNothingStored()
    {
        var resolved = KeybindCatalog.Resolve(null);

        Assert.AreEqual(KeybindCatalog.Definitions.Count, resolved.Count);
        Assert.IsTrue(resolved.All(keybind => keybind.IsDefault));
        Assert.IsTrue(resolved.All(keybind => keybind.IsValid));
        Assert.AreEqual(
            KeybindCatalog.DefaultGesture(HotkeyAction.ToggleRecording),
            resolved.Single(keybind => keybind.Action == HotkeyAction.ToggleRecording).Gesture);
    }

    [TestMethod]
    public void Resolve_PreservesCatalogOrderRegardlessOfStoredOrder()
    {
        var assignments = KeybindCatalog.Definitions
            .Reverse()
            .Select(definition => new KeybindAssignment
            {
                Action = definition.Action,
                Gesture = definition.DefaultGesture
            })
            .ToList();

        var resolved = KeybindCatalog.Resolve(assignments);

        CollectionAssert.AreEqual(
            KeybindCatalog.Definitions.Select(definition => definition.Action).ToArray(),
            resolved.Select(keybind => keybind.Action).ToArray());
    }

    [TestMethod]
    public void Resolve_CanonicalizesModifierOrderAndAliasSpellings()
    {
        var resolved = KeybindCatalog.Resolve(
        [
            new KeybindAssignment { Action = HotkeyAction.ToggleRecording, Gesture = "shift + control + r" }
        ]);

        var toggle = resolved.Single(keybind => keybind.Action == HotkeyAction.ToggleRecording);
        Assert.AreEqual("Ctrl+Shift+R", toggle.Gesture);
        Assert.AreEqual("Ctrl + Shift + R", toggle.DisplayGesture);
        Assert.IsTrue(toggle.IsValid);
        Assert.IsTrue(toggle.IsDefault, "Canonicalized gesture equal to the default must not read as customized.");
    }

    [TestMethod]
    public void Resolve_TreatsBlankGestureAsExplicitlyUnbound()
    {
        var resolved = KeybindCatalog.Resolve(
        [
            new KeybindAssignment { Action = HotkeyAction.PixelRuler, Gesture = "   " }
        ]);

        var ruler = resolved.Single(keybind => keybind.Action == HotkeyAction.PixelRuler);
        Assert.IsFalse(ruler.IsBound);
        Assert.IsTrue(ruler.IsValid, "Unbound is a legitimate state, not a validation failure.");
        Assert.IsFalse(ruler.IsDefault);
        Assert.AreEqual(string.Empty, ruler.Gesture);
        Assert.AreEqual("Not set", ruler.DisplayGesture);
    }

    [TestMethod]
    public void Resolve_KeepsUnparsableGestureVisibleAndFlagsItInvalid()
    {
        var resolved = KeybindCatalog.Resolve(
        [
            new KeybindAssignment { Action = HotkeyAction.ColorPicker, Gesture = "Ctrl+NoSuchKey" }
        ]);

        var picker = resolved.Single(keybind => keybind.Action == HotkeyAction.ColorPicker);
        Assert.IsFalse(picker.IsValid);
        Assert.IsTrue(picker.IsBound);
        Assert.AreEqual("Ctrl+NoSuchKey", picker.Gesture);
    }

    [TestMethod]
    public void Resolve_IgnoresDuplicateAndUnknownStoredEntries()
    {
        var resolved = KeybindCatalog.Resolve(
        [
            new KeybindAssignment { Action = HotkeyAction.OcrRegion, Gesture = "Alt+F9" },
            new KeybindAssignment { Action = HotkeyAction.OcrRegion, Gesture = "Alt+F8" },
            new KeybindAssignment { Action = (HotkeyAction)9999, Gesture = "Alt+F7" }
        ]);

        Assert.AreEqual(KeybindCatalog.Definitions.Count, resolved.Count);
        Assert.AreEqual("Alt+F9", resolved.Single(keybind => keybind.Action == HotkeyAction.OcrRegion).Gesture);
    }

    [TestMethod]
    public void FindConflicts_DetectsSameChordAcrossActionsDespiteDifferentSpelling()
    {
        var resolved = KeybindCatalog.Resolve(
        [
            new KeybindAssignment { Action = HotkeyAction.ColorPicker, Gesture = "Ctrl+Shift+C" },
            new KeybindAssignment { Action = HotkeyAction.PixelRuler, Gesture = "shift+control+c" }
        ]);

        var conflicts = KeybindCatalog.FindConflicts(resolved);

        Assert.AreEqual(1, conflicts.Count);
        CollectionAssert.AreEquivalent(
            new[] { HotkeyAction.ColorPicker, HotkeyAction.PixelRuler },
            conflicts[0].Actions.ToArray());
        Assert.AreEqual("Ctrl + Shift + C", conflicts[0].DisplayGesture);
    }

    [TestMethod]
    public void FindConflicts_IgnoresUnboundAndInvalidEntries()
    {
        var resolved = KeybindCatalog.Resolve(
        [
            new KeybindAssignment { Action = HotkeyAction.ColorPicker, Gesture = string.Empty },
            new KeybindAssignment { Action = HotkeyAction.PixelRuler, Gesture = string.Empty },
            new KeybindAssignment { Action = HotkeyAction.OcrRegion, Gesture = "Ctrl+Bogus" },
            new KeybindAssignment { Action = HotkeyAction.SaveReplay, Gesture = "Ctrl+Bogus" }
        ]);

        Assert.AreEqual(0, KeybindCatalog.FindConflicts(resolved).Count);
    }

    [TestMethod]
    public void ConflictingActions_ReportsEveryActionInvolvedInACollision()
    {
        var resolved = KeybindCatalog.Resolve(
        [
            new KeybindAssignment { Action = HotkeyAction.ColorPicker, Gesture = "Ctrl+Shift+C" },
            new KeybindAssignment { Action = HotkeyAction.PixelRuler, Gesture = "Ctrl+Shift+C" }
        ]);

        var conflicting = KeybindCatalog.ConflictingActions(resolved);

        Assert.IsTrue(conflicting.Contains(HotkeyAction.ColorPicker));
        Assert.IsTrue(conflicting.Contains(HotkeyAction.PixelRuler));
        Assert.IsFalse(conflicting.Contains(HotkeyAction.OcrRegion));
    }

    [TestMethod]
    [DataRow("ctrl+shift+r", "Ctrl+Shift+R")]
    [DataRow("Shift+Ctrl+R", "Ctrl+Shift+R")]
    [DataRow("alt+shift+ctrl+f12", "Ctrl+Alt+Shift+F12")]
    [DataRow("control + print screen", "Ctrl+PrintScreen")]
    [DataRow("snapshot", "PrintScreen")]
    [DataRow("  Ctrl + 7  ", "Ctrl+7")]
    public void NormalizeGesture_ProducesCanonicalStorageForm(string input, string expected)
    {
        Assert.AreEqual(expected, KeybindCatalog.NormalizeGesture(input));
    }

    [TestMethod]
    public void NormalizeGesture_ReturnsTrimmedInputWhenItCannotBeParsed()
    {
        Assert.AreEqual("Ctrl+Bogus", KeybindCatalog.NormalizeGesture("  Ctrl+Bogus  "));
        Assert.AreEqual(string.Empty, KeybindCatalog.NormalizeGesture(null));
    }

    [TestMethod]
    public void ToAssignments_RoundTripsThroughResolveWithoutDrift()
    {
        var original = KeybindCatalog.Resolve(
        [
            new KeybindAssignment { Action = HotkeyAction.ToggleRecording, Gesture = "shift+ctrl+r" },
            new KeybindAssignment { Action = HotkeyAction.PixelRuler, Gesture = string.Empty }
        ]);

        var round = KeybindCatalog.Resolve(KeybindCatalog.ToAssignments(original));

        CollectionAssert.AreEqual(
            original.Select(keybind => keybind.Gesture).ToArray(),
            round.Select(keybind => keybind.Gesture).ToArray());
        Assert.IsFalse(round.Single(keybind => keybind.Action == HotkeyAction.PixelRuler).IsBound);
    }

    [TestMethod]
    public void BuildRegistrationPlan_SkipsUnboundAndInvalidEntriesAndAssignsStableIds()
    {
        var resolved = KeybindCatalog.Resolve(
        [
            new KeybindAssignment { Action = HotkeyAction.PixelRuler, Gesture = string.Empty },
            new KeybindAssignment { Action = HotkeyAction.ColorPicker, Gesture = "Ctrl+Bogus" }
        ]);

        var plan = KeybindCatalog.BuildRegistrationPlan(resolved);

        Assert.IsFalse(plan.Any(entry => entry.Action == HotkeyAction.PixelRuler));
        Assert.IsFalse(plan.Any(entry => entry.Action == HotkeyAction.ColorPicker));
        Assert.AreEqual(plan.Count, plan.Select(entry => entry.Id).Distinct().Count(), "Hotkey ids must be unique.");
        Assert.IsTrue(plan.All(entry => entry.Key != 0));

        var toggle = plan.Single(entry => entry.Action == HotkeyAction.ToggleRecording);
        Assert.AreEqual(
            KeybindCatalog.BuildRegistrationPlan(KeybindCatalog.Resolve(null))
                .Single(entry => entry.Action == HotkeyAction.ToggleRecording).Id,
            toggle.Id,
            "Ids must not shift when an unrelated action is unbound.");
    }
}
