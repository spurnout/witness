# Quick-Capture Defaults Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the default screenshot silently copy to the clipboard with no popup, make that behavior configurable, and add Screenpresso-style hover window auto-select to the capture overlay.

**Architecture:** One new string setting (`PostCaptureAction`) drives a switch at the single point where `MainWindow.CaptureAndStoreAsync` finishes a capture, replacing today's unconditional `ShowCaptureTaskWindow` call. Hover auto-select is a new pure function on the existing `CaptureOverlayGeometry` plus lazy per-window child enumeration in `CaptureOverlayTargetCatalog`; `RegionCaptureWindow` draws the highlight and turns a click into a capture. All decision logic lives in pure, unit-testable statics — the WPF surfaces only render.

**Tech Stack:** C# / .NET 10 (`net10.0-windows10.0.19041.0`), WPF, MSTest, Win32 P/Invoke (`user32.dll`).

Spec: [docs/superpowers/specs/2026-08-15-quick-capture-defaults-design.md](../specs/2026-08-15-quick-capture-defaults-design.md)

## Global Constraints

- Build: `dotnet build GoatShot.slnx -c Release --no-restore`. Test: `dotnet test GoatShot.slnx -c Release --no-build`. During development `dotnet test GoatShot.slnx --filter "FullyQualifiedName~<TestName>"` is faster; the full suite must pass before the final commit.
- Settings that are enum-shaped are stored in `settings.json` as **strings**, not numbers. `SettingsStore` has no `JsonStringEnumConverter`, so a raw `enum` property would serialize as an integer and break readability. Follow `DefaultShareDestination` / `QualityProfile`.
- Never throw on a bad settings value. A hand-edited `settings.json` must not stop a capture from completing — parse defensively and fall back.
- Product name in user-facing strings comes from `BrandIdentity.ProductName`, never a hard-coded "Receipts" or "GoatShot".
- New WPF controls need `AutomationProperties.Name`, or `SettingsWindowAccessibilityAuditor` / `WpfSurfaceAccessibilityAuditor` will flag them.
- The codebase is `Nullable enable`. `System.Windows.Controls` types collide with `System.Windows.Forms`, so files alias them (`using ComboBox = System.Windows.Controls.ComboBox;`). Match the aliases already at the top of whichever file you edit.
- Comments explain *why*, not *what*. Match the density of the surrounding file — sparse, with `///` doc comments on non-obvious public members.
- Commit after each task with a conventional-commit message (`feat:`, `fix:`, `refactor:`, `test:`, `docs:`).

## File Structure

| File | Responsibility |
| --- | --- |
| `src/GoatShot.App/Models/PostCaptureAction.cs` | **New.** The three post-capture behaviors as an enum. |
| `src/GoatShot.App/Services/PostCaptureActionCatalog.cs` | **New.** Parse/normalize/describe the setting; the option list the settings ComboBox renders. |
| `src/GoatShot.App/Models/AppSettings.cs` | Two new fields; schema version default. |
| `src/GoatShot.App/Services/SettingsMigrationService.cs` | Schema 17 → 18; normalize the new string field. |
| `src/GoatShot.App/Services/CaptureOverlayGeometry.cs` | `ResolveHoverTarget`, `CaptureOverlayHoverMode`, three new `CaptureOverlayTarget` fields. |
| `src/GoatShot.App/Services/CaptureOverlayTargetCatalog.cs` | Populate z-order/parent/handle; `BuildChildTargets` for lazy per-window enumeration. |
| `src/GoatShot.App/Windows/RegionCaptureWindow.xaml(.cs)` | Hover highlight, click-to-capture, Ctrl/Shift modifiers. |
| `src/GoatShot.App/Services/ScreenshotService.cs` | Pass the hover toggle into the overlay. |
| `src/GoatShot.App/MainWindow.xaml.cs` | Switch on the setting instead of always showing the actions window. |
| `src/GoatShot.App/Services/TrayService.cs` | Balloon tip for quiet captures taken while the workspace is hidden. |
| `src/GoatShot.App/Windows/SettingsWindow.xaml(.cs)` | The dropdown, the hover checkbox, load/save. |
| `src/GoatShot.App/Services/DiagnosticBundleService.cs` | Log the two new settings. |
| `src/GoatShot.Tests/PostCaptureActionCatalogTests.cs` | **New.** Parsing and option coverage. |
| `src/GoatShot.Tests/SettingsMigrationServiceTests.cs` | Migration cases for the new field. |
| `src/GoatShot.Tests/CaptureOverlayGeometryTests.cs` | Hover hit-test cases. |

---

## Task 1: Post-capture action model and catalog

**Files:**
- Create: `src/GoatShot.App/Models/PostCaptureAction.cs`
- Create: `src/GoatShot.App/Services/PostCaptureActionCatalog.cs`
- Test: `src/GoatShot.Tests/PostCaptureActionCatalogTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum GoatShot.App.Models.PostCaptureAction { CopyQuietly, ShowActionsWindow, OpenEditor }`
  - `PostCaptureActionCatalog.Default` → `PostCaptureAction.CopyQuietly`
  - `PostCaptureActionCatalog.Parse(string?)` → `PostCaptureAction`
  - `PostCaptureActionCatalog.Normalize(string?)` → `string`
  - `PostCaptureActionCatalog.Describe(PostCaptureAction)` → `PostCaptureActionOption`
  - `PostCaptureActionCatalog.Options` → `IReadOnlyList<PostCaptureActionOption>`
  - `record PostCaptureActionOption(PostCaptureAction Action, string Label, string Description)` with `string StorageValue`

- [ ] **Step 1: Write the failing tests**

Create `src/GoatShot.Tests/PostCaptureActionCatalogTests.cs`:

```csharp
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class PostCaptureActionCatalogTests
{
    [TestMethod]
    public void Parse_ReadsEveryStoredValueRegardlessOfCasing()
    {
        Assert.AreEqual(PostCaptureAction.CopyQuietly, PostCaptureActionCatalog.Parse("CopyQuietly"));
        Assert.AreEqual(PostCaptureAction.ShowActionsWindow, PostCaptureActionCatalog.Parse("showactionswindow"));
        Assert.AreEqual(PostCaptureAction.OpenEditor, PostCaptureActionCatalog.Parse("  OpenEditor  "));
    }

    [TestMethod]
    public void Parse_FallsBackToQuietCopyForAnythingUnrecognized()
    {
        // settings.json is hand-editable, so garbage must degrade to the default rather than throw.
        Assert.AreEqual(PostCaptureAction.CopyQuietly, PostCaptureActionCatalog.Parse(null));
        Assert.AreEqual(PostCaptureAction.CopyQuietly, PostCaptureActionCatalog.Parse(string.Empty));
        Assert.AreEqual(PostCaptureAction.CopyQuietly, PostCaptureActionCatalog.Parse("   "));
        Assert.AreEqual(PostCaptureAction.CopyQuietly, PostCaptureActionCatalog.Parse("OpenTheThing"));
        Assert.AreEqual(PostCaptureAction.CopyQuietly, PostCaptureActionCatalog.Parse("7"));
    }

    [TestMethod]
    public void Normalize_RewritesLooseInputToTheCanonicalStoredValue()
    {
        Assert.AreEqual("OpenEditor", PostCaptureActionCatalog.Normalize("openeditor"));
        Assert.AreEqual("CopyQuietly", PostCaptureActionCatalog.Normalize("nonsense"));
    }

    [TestMethod]
    public void Options_CoverEveryActionWithDistinctLabels()
    {
        var actions = PostCaptureActionCatalog.Options.Select(option => option.Action).ToList();
        CollectionAssert.AreEquivalent(Enum.GetValues<PostCaptureAction>(), actions);

        foreach (var option in PostCaptureActionCatalog.Options)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(option.Label));
            Assert.IsFalse(string.IsNullOrWhiteSpace(option.Description));
            Assert.AreEqual(option.Action.ToString(), option.StorageValue);
            Assert.AreSame(option, PostCaptureActionCatalog.Describe(option.Action));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test GoatShot.slnx --filter "FullyQualifiedName~PostCaptureActionCatalogTests"`
Expected: build FAILS with `CS0246: The type or namespace name 'PostCaptureActionCatalog' could not be found`.

- [ ] **Step 3: Add the enum**

Create `src/GoatShot.App/Models/PostCaptureAction.cs`:

```csharp
namespace GoatShot.App.Models;

/// <summary>What the app does with a capture once it has been saved to the workspace.</summary>
public enum PostCaptureAction
{
    /// <summary>Copy and save with nothing on screen. The quick-screenshot default.</summary>
    CopyQuietly,

    /// <summary>Open the capture actions window with Open, Edit, Copy, Share, AI, and export.</summary>
    ShowActionsWindow,

    /// <summary>Open the annotation editor straight onto the new capture.</summary>
    OpenEditor
}
```

- [ ] **Step 4: Add the catalog**

Create `src/GoatShot.App/Services/PostCaptureActionCatalog.cs`:

```csharp
using GoatShot.App.Models;

namespace GoatShot.App.Services;

/// <summary>One selectable post-capture behavior plus the copy the settings window shows for it.</summary>
public sealed record PostCaptureActionOption(PostCaptureAction Action, string Label, string Description)
{
    /// <summary>The value written to settings.json. Stable across releases; never localize it.</summary>
    public string StorageValue => Action.ToString();
}

/// <summary>
/// Reads <see cref="AppSettings.PostCaptureAction"/>, which is stored as a string like the other
/// enum-shaped settings. Everything here falls back rather than throws: a hand-edited settings file
/// must never be able to stop a capture from completing.
/// </summary>
public static class PostCaptureActionCatalog
{
    public const PostCaptureAction Default = PostCaptureAction.CopyQuietly;

    public static IReadOnlyList<PostCaptureActionOption> Options { get; } =
    [
        new(PostCaptureAction.CopyQuietly,
            "Copy quietly",
            "Copies to the clipboard and saves to the library without opening anything."),
        new(PostCaptureAction.ShowActionsWindow,
            "Show capture actions",
            "Opens the actions window with Open, Edit, Copy, Share, AI, and export."),
        new(PostCaptureAction.OpenEditor,
            "Open the editor",
            "Opens the annotation editor straight onto the new capture.")
    ];

    public static PostCaptureAction Parse(string? value)
    {
        // TryParse happily accepts "7" and returns an undefined member, so IsDefined has to gate it.
        return Enum.TryParse<PostCaptureAction>(value?.Trim(), ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed)
            ? parsed
            : Default;
    }

    public static string Normalize(string? value) => Parse(value).ToString();

    public static PostCaptureActionOption Describe(PostCaptureAction action)
    {
        return Options.First(option => option.Action == action);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test GoatShot.slnx --filter "FullyQualifiedName~PostCaptureActionCatalogTests"`
Expected: PASS, 4 tests.

- [ ] **Step 6: Commit**

```bash
git add src/GoatShot.App/Models/PostCaptureAction.cs src/GoatShot.App/Services/PostCaptureActionCatalog.cs src/GoatShot.Tests/PostCaptureActionCatalogTests.cs
git commit -m "feat(capture): add post-capture action catalog"
```

---

## Task 2: Settings fields and migration

**Files:**
- Modify: `src/GoatShot.App/Models/AppSettings.cs:5-7`
- Modify: `src/GoatShot.App/Services/SettingsMigrationService.cs:7,12-23`
- Test: `src/GoatShot.Tests/SettingsMigrationServiceTests.cs`

**Interfaces:**
- Consumes: `PostCaptureActionCatalog.Normalize(string?)` from Task 1.
- Produces:
  - `AppSettings.PostCaptureAction` (`string`, default `"CopyQuietly"`)
  - `AppSettings.EnableCaptureHoverAutoSelect` (`bool`, default `true`)
  - `SettingsMigrationService.CurrentSchemaVersion == 18`

- [ ] **Step 1: Write the failing tests**

Append to `src/GoatShot.Tests/SettingsMigrationServiceTests.cs`, inside the existing class:

```csharp
    [TestMethod]
    public void Migrate_NormalizesAnUnusablePostCaptureActionToQuietCopy()
    {
        var settings = new AppSettings
        {
            SettingsSchemaVersion = 17,
            PostCaptureAction = "   "
        };

        SettingsMigrationService.Migrate(settings);

        Assert.AreEqual("CopyQuietly", settings.PostCaptureAction);
        Assert.AreEqual(SettingsMigrationService.CurrentSchemaVersion, settings.SettingsSchemaVersion);
    }

    [TestMethod]
    public void Migrate_KeepsAnExplicitPostCaptureChoiceAndCanonicalizesItsCasing()
    {
        var settings = new AppSettings
        {
            SettingsSchemaVersion = 17,
            PostCaptureAction = "showactionswindow"
        };

        SettingsMigrationService.Migrate(settings);

        Assert.AreEqual("ShowActionsWindow", settings.PostCaptureAction);
    }

    [TestMethod]
    public void NewSettings_DefaultToQuietCopyWithHoverAutoSelectOn()
    {
        var settings = new AppSettings();

        Assert.AreEqual("CopyQuietly", settings.PostCaptureAction);
        Assert.IsTrue(settings.EnableCaptureHoverAutoSelect);
        Assert.AreEqual(SettingsMigrationService.CurrentSchemaVersion, settings.SettingsSchemaVersion);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test GoatShot.slnx --filter "FullyQualifiedName~SettingsMigrationServiceTests"`
Expected: build FAILS with `CS0117: 'AppSettings' does not contain a definition for 'PostCaptureAction'`.

- [ ] **Step 3: Add the settings fields**

In `src/GoatShot.App/Models/AppSettings.cs`, change the schema default and add the two fields directly under `AutoCopyImageAfterCapture` so the capture-related settings stay together:

```csharp
    public int SettingsSchemaVersion { get; set; } = 18;
    public string LibraryRoot { get; set; } = string.Empty;
    public bool AutoCopyImageAfterCapture { get; set; } = true;
    public string PostCaptureAction { get; set; } = "CopyQuietly";
    public bool EnableCaptureHoverAutoSelect { get; set; } = true;
    public bool IncludeCursor { get; set; } = true;
```

- [ ] **Step 4: Bump the schema and normalize in the migration**

In `src/GoatShot.App/Services/SettingsMigrationService.cs`, change the constant and add the normalization. The constant:

```csharp
    public const int CurrentSchemaVersion = 18;

    /// <summary>Schema version that introduced the rebindable keybind catalog.</summary>
    private const int KeybindCatalogSchemaVersion = 17;
```

Then, immediately after the `MigrateLegacyBrandingDefaults` call in `Migrate`, add:

```csharp
        changed |= NormalizePostCaptureAction(settings);
```

And add the method next to the other private helpers in the same file:

```csharp
    /// <summary>
    /// Schema 18 introduced the post-capture behavior setting. Older files have no value at all and
    /// hand-edited ones can hold anything, so both collapse to the shipped default here rather than
    /// at every read site.
    /// </summary>
    private static bool NormalizePostCaptureAction(AppSettings settings)
    {
        var normalized = PostCaptureActionCatalog.Normalize(settings.PostCaptureAction);
        if (string.Equals(settings.PostCaptureAction, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        settings.PostCaptureAction = normalized;
        return true;
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test GoatShot.slnx --filter "FullyQualifiedName~SettingsMigrationServiceTests"`
Expected: PASS, all tests in the class including the pre-existing ones.

- [ ] **Step 6: Commit**

```bash
git add src/GoatShot.App/Models/AppSettings.cs src/GoatShot.App/Services/SettingsMigrationService.cs src/GoatShot.Tests/SettingsMigrationServiceTests.cs
git commit -m "feat(settings): add post-capture action and hover auto-select settings"
```

---

## Task 3: Quiet capture in MainWindow plus the tray balloon

**Files:**
- Modify: `src/GoatShot.App/MainWindow.xaml.cs:698-710`
- Modify: `src/GoatShot.App/Services/TrayService.cs`
- Modify: `src/GoatShot.App/Services/DiagnosticBundleService.cs:188-200`

**Interfaces:**
- Consumes: `PostCaptureActionCatalog.Parse`, `PostCaptureAction` (Task 1); `AppSettings.PostCaptureAction` (Task 2); existing `MainWindow.ShowCaptureTaskWindow(CaptureItem)`, `MainWindow.OpenEditorForItem(CaptureItem, AnnotationMode?)`, `AppServices.Tray` (nullable `TrayService`).
- Produces: `TrayService.ShowCaptureNotification(string message)`.

There is no automated test here — the behavior is a WPF window-opening side effect on a class that needs a live `AppServices`. The verification is the build plus the manual smoke check in Step 5.

- [ ] **Step 1: Add the tray balloon**

In `src/GoatShot.App/Services/TrayService.cs`, add this public method after the constructor:

```csharp
    /// <summary>
    /// Feedback for quiet capture mode. The workspace status line covers the case where the window
    /// is open, so this is the only signal a tray-only capture gets: short, non-blocking, no window.
    /// </summary>
    public void ShowCaptureNotification(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = BrandIdentity.ProductName;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(2000);
    }
```

- [ ] **Step 2: Switch on the setting in MainWindow**

In `src/GoatShot.App/MainWindow.xaml.cs`, replace the trailing `ShowCaptureTaskWindow(item);` inside `CaptureAndStoreAsync` (line 708) with `RunPostCaptureAction(item);`, so the tail of the method reads:

```csharp
        SetStatus(item.IsPrivate
            ? $"Private capture saved temporarily: {item.FilePath}"
            : $"Captured {item.Kind}: {item.FileName}");
        RunPostCaptureAction(item);
        return item;
    }
```

Then add both methods immediately above the existing `private void ShowCaptureTaskWindow(CaptureItem item)` (around line 2886):

```csharp
    /// <summary>
    /// What happens once a capture is saved. Quiet copy is the default so the fast path is
    /// hotkey to clipboard with nothing to dismiss; the other modes are opt-in from Settings.
    /// </summary>
    private void RunPostCaptureAction(CaptureItem item)
    {
        if (_auditMode)
        {
            return;
        }

        switch (PostCaptureActionCatalog.Parse(_services.Settings.PostCaptureAction))
        {
            case PostCaptureAction.ShowActionsWindow:
                ShowCaptureTaskWindow(item);
                break;
            case PostCaptureAction.OpenEditor:
                OpenEditorForItem(item);
                break;
            default:
                NotifyQuietCapture(item);
                break;
        }
    }

    /// <summary>
    /// Quiet mode shows nothing while the workspace is open, because the status line already said
    /// what happened. With the workspace hidden there would otherwise be no confirmation at all.
    /// </summary>
    private void NotifyQuietCapture(CaptureItem item)
    {
        if (IsVisible)
        {
            return;
        }

        _services.Tray?.ShowCaptureNotification(_services.Settings.AutoCopyImageAfterCapture
            ? $"Copied to clipboard: {item.FileName}"
            : $"Saved: {item.FileName}");
    }
```

- [ ] **Step 3: Log the new settings in the diagnostic bundle**

In `src/GoatShot.App/Services/DiagnosticBundleService.cs`, inside `BuildRedactedSettings`, add the two fields under `AutoCopyImageAfterCapture`:

```csharp
            _settings.LibraryRoot,
            _settings.AutoCopyImageAfterCapture,
            _settings.PostCaptureAction,
            _settings.EnableCaptureHoverAutoSelect,
            _settings.IncludeCursor,
```

- [ ] **Step 4: Build and run the full suite**

Run: `dotnet build GoatShot.slnx -c Release`
Expected: build succeeds with no new warnings.

Run: `dotnet test GoatShot.slnx -c Release --no-build`
Expected: PASS, no regressions.

- [ ] **Step 5: Manual smoke check**

Launch the app, leave the workspace window closed to the tray, and press the region-capture hotkey. Expected: the overlay appears, the drag captures, **no actions window opens**, a tray balloon says "Copied to clipboard: …", and Ctrl+V pastes the image into any app.

- [ ] **Step 6: Commit**

```bash
git add src/GoatShot.App/MainWindow.xaml.cs src/GoatShot.App/Services/TrayService.cs src/GoatShot.App/Services/DiagnosticBundleService.cs
git commit -m "feat(capture): make quiet clipboard capture the default post-capture action"
```

---

## Task 4: Settings UI for post-capture behavior

**Files:**
- Modify: `src/GoatShot.App/Windows/SettingsWindow.xaml:209-210`
- Modify: `src/GoatShot.App/Windows/SettingsWindow.xaml.cs:276-282` (load), `:1418-1424` (save), plus new helpers

**Interfaces:**
- Consumes: `PostCaptureActionCatalog.Options`, `.Normalize`, `.Parse`, `.Describe` (Task 1); `AppSettings.PostCaptureAction`, `.EnableCaptureHoverAutoSelect` (Task 2); the file's existing `SelectComboBoxItemByTag(ComboBox, string)` helper.
- Produces: `PostCaptureActionBox`, `HoverAutoSelectBox`, `PostCaptureActionHelpText` controls; `SelectedComboBoxTag(ComboBox)` helper.

Dirty tracking (`SettingsWindow.Dirty.cs`) and the Ctrl+K settings search (`SettingsWindow.Search.cs`) both walk the logical tree and pick up a `TextBlock` label followed by a control, so neither needs registration work.

- [ ] **Step 1: Add the controls to the XAML**

In `src/GoatShot.App/Windows/SettingsWindow.xaml`, replace the two existing lines:

```xml
                <CheckBox x:Name="CopyAfterCaptureBox" Content="Copy image to clipboard after capture" />
                <CheckBox x:Name="IncludeCursorBox" Content="Include cursor in captures" />
```

with:

```xml
                <TextBlock Text="After capture" Foreground="{StaticResource MutedInkBrush}" />
                <ComboBox x:Name="PostCaptureActionBox"
                          Margin="0,6,0,6"
                          Style="{StaticResource GoatComboBox}"
                          AutomationProperties.Name="Behavior after a capture completes"
                          SelectionChanged="PostCaptureAction_SelectionChanged" />
                <TextBlock x:Name="PostCaptureActionHelpText"
                           Foreground="{StaticResource MutedInkBrush}"
                           FontSize="12"
                           TextWrapping="Wrap"
                           Margin="0,0,0,10" />
                <CheckBox x:Name="CopyAfterCaptureBox" Content="Copy image to clipboard after capture" />
                <CheckBox x:Name="IncludeCursorBox" Content="Include cursor in captures" />
                <CheckBox x:Name="HoverAutoSelectBox"
                          Content="Highlight the window under the cursor while selecting"
                          AutomationProperties.Name="Highlight the window under the cursor while selecting" />
```

- [ ] **Step 2: Populate and load**

In `src/GoatShot.App/Windows/SettingsWindow.xaml.cs`, inside `LoadSettingsCore`, add three lines directly above the existing `CopyAfterCaptureBox.IsChecked = settings.AutoCopyImageAfterCapture;`:

```csharp
        PopulatePostCaptureActionBox();
        SelectComboBoxItemByTag(PostCaptureActionBox, PostCaptureActionCatalog.Normalize(settings.PostCaptureAction));
        UpdatePostCaptureActionHelpText();
```

and one line after the existing `IncludeCursorBox.IsChecked = settings.IncludeCursor;`:

```csharp
        HoverAutoSelectBox.IsChecked = settings.EnableCaptureHoverAutoSelect;
```

Then add the three helpers next to the other private static combo helpers (near `SelectComboBoxItemByTag`, around line 2140):

```csharp
    /// <summary>Fills the dropdown from the catalog so the option list has one source of truth.</summary>
    private void PopulatePostCaptureActionBox()
    {
        if (PostCaptureActionBox.Items.Count > 0)
        {
            return;
        }

        foreach (var option in PostCaptureActionCatalog.Options)
        {
            PostCaptureActionBox.Items.Add(new ComboBoxItem
            {
                Content = option.Label,
                Tag = option.StorageValue
            });
        }
    }

    private void PostCaptureAction_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePostCaptureActionHelpText();
    }

    private void UpdatePostCaptureActionHelpText()
    {
        var action = PostCaptureActionCatalog.Parse(SelectedComboBoxTag(PostCaptureActionBox));
        PostCaptureActionHelpText.Text = PostCaptureActionCatalog.Describe(action).Description +
            " Copying is governed by the checkbox below, so clearing that box while Copy quietly is selected saves the capture without touching the clipboard.";
    }

    private static string SelectedComboBoxTag(System.Windows.Controls.ComboBox comboBox)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
    }
```

If `SelectionChangedEventArgs` or `ComboBoxItem` is unresolved, check the aliases at the top of the file and add `using System.Windows.Controls;` members the same way the neighbouring handlers do.

- [ ] **Step 3: Save**

In `ApplyCurrentSettingsFromControls`, add two lines directly under the existing `settings.AutoCopyImageAfterCapture = CopyAfterCaptureBox.IsChecked == true;`:

```csharp
        settings.PostCaptureAction = PostCaptureActionCatalog.Normalize(SelectedComboBoxTag(PostCaptureActionBox));
        settings.EnableCaptureHoverAutoSelect = HoverAutoSelectBox.IsChecked == true;
```

- [ ] **Step 4: Build and run the full suite**

Run: `dotnet build GoatShot.slnx -c Release`
Expected: build succeeds.

Run: `dotnet test GoatShot.slnx -c Release --no-build`
Expected: PASS. The settings renderer and accessibility auditor tests exercise this window; if the auditor reports an unnamed control, add the missing `AutomationProperties.Name`.

- [ ] **Step 5: Manual smoke check**

Open Settings → General. Expected: "After capture" shows "Copy quietly" with the description below it; changing the selection updates the description immediately and marks the window dirty; Ctrl+K search for "after capture" jumps to it. Save, pick "Show capture actions", take a capture — the actions window returns. Switch back to "Copy quietly" and confirm it stays quiet.

- [ ] **Step 6: Commit**

```bash
git add src/GoatShot.App/Windows/SettingsWindow.xaml src/GoatShot.App/Windows/SettingsWindow.xaml.cs
git commit -m "feat(settings): expose post-capture behavior and hover auto-select"
```

---

## Task 5: Hover hit-testing in CaptureOverlayGeometry

**Files:**
- Modify: `src/GoatShot.App/Services/CaptureOverlayGeometry.cs` (add `ResolveHoverTarget`, `Area`; extend the `CaptureOverlayTarget` record; add `CaptureOverlayHoverMode`)
- Test: `src/GoatShot.Tests/CaptureOverlayGeometryTests.cs`

**Interfaces:**
- Consumes: existing `CaptureOverlayTarget`, `CaptureOverlayTargetKind`, `CaptureBounds`.
- Produces:
  - `enum CaptureOverlayHoverMode { Off, Window, Control }`
  - `CaptureOverlayGeometry.ResolveHoverTarget(int screenX, int screenY, IReadOnlyList<CaptureOverlayTarget> targets, CaptureOverlayHoverMode mode)` → `CaptureOverlayTarget?`
  - `CaptureOverlayTarget` gains `int ZOrder = 0`, `string? ParentId = null`, `long NativeHandle = 0` — all trailing optional parameters, so every existing positional construction keeps compiling.

- [ ] **Step 1: Write the failing tests**

Append to the existing class in `src/GoatShot.Tests/CaptureOverlayGeometryTests.cs`:

```csharp
    [TestMethod]
    public void ResolveHoverTarget_PrefersTheTopmostWindowRatherThanTheSmallestOne()
    {
        // The small window is behind the big one. Smallest-area containment would pick it; z-order
        // is what actually decides which window a click would land on.
        var behind = new CaptureOverlayTarget(
            "window:behind",
            "Window: Behind",
            CaptureOverlayTargetKind.Window,
            new CaptureBounds { X = 100, Y = 100, Width = 200, Height = 200 },
            ZOrder: 5);
        var front = new CaptureOverlayTarget(
            "window:front",
            "Window: Front",
            CaptureOverlayTargetKind.Window,
            new CaptureBounds { X = 50, Y = 50, Width = 600, Height = 600 },
            ZOrder: 1);

        var hovered = CaptureOverlayGeometry.ResolveHoverTarget(
            150,
            150,
            [behind, front],
            CaptureOverlayHoverMode.Window);

        Assert.AreSame(front, hovered);
    }

    [TestMethod]
    public void ResolveHoverTarget_DrillsIntoTheSmallestChildOfTheHoveredWindowInControlMode()
    {
        var window = new CaptureOverlayTarget(
            "window:app",
            "Window: App",
            CaptureOverlayTargetKind.Window,
            new CaptureBounds { X = 0, Y = 0, Width = 800, Height = 600 });
        var content = new CaptureOverlayTarget(
            "window:app:content",
            "Content area: App",
            CaptureOverlayTargetKind.ContentArea,
            new CaptureBounds { X = 8, Y = 60, Width = 784, Height = 532 },
            ShowInChooser: false,
            ParentId: "window:app");
        var pane = new CaptureOverlayTarget(
            "control:pane",
            "Control: Pane",
            CaptureOverlayTargetKind.ControlArea,
            new CaptureBounds { X = 20, Y = 80, Width = 300, Height = 200 },
            ShowInChooser: false,
            ParentId: "window:app");

        Assert.AreSame(
            pane,
            CaptureOverlayGeometry.ResolveHoverTarget(100, 120, [window, content, pane], CaptureOverlayHoverMode.Control));
        Assert.AreSame(
            window,
            CaptureOverlayGeometry.ResolveHoverTarget(100, 120, [window, content, pane], CaptureOverlayHoverMode.Window));
    }

    [TestMethod]
    public void ResolveHoverTarget_IgnoresChildrenBelongingToADifferentWindow()
    {
        var window = new CaptureOverlayTarget(
            "window:app",
            "Window: App",
            CaptureOverlayTargetKind.Window,
            new CaptureBounds { X = 0, Y = 0, Width = 800, Height = 600 });
        var foreignPane = new CaptureOverlayTarget(
            "control:other",
            "Control: Other",
            CaptureOverlayTargetKind.ControlArea,
            new CaptureBounds { X = 20, Y = 80, Width = 300, Height = 200 },
            ShowInChooser: false,
            ParentId: "window:somewhere-else");

        var hovered = CaptureOverlayGeometry.ResolveHoverTarget(
            100,
            120,
            [window, foreignPane],
            CaptureOverlayHoverMode.Control);

        Assert.AreSame(window, hovered);
    }

    [TestMethod]
    public void ResolveHoverTarget_FallsBackToTheMonitorOverBareDesktop()
    {
        var monitor = new CaptureOverlayTarget(
            "monitor:1",
            "Primary monitor",
            CaptureOverlayTargetKind.Monitor,
            new CaptureBounds { X = 0, Y = 0, Width = 1920, Height = 1080 });
        var window = new CaptureOverlayTarget(
            "window:app",
            "Window: App",
            CaptureOverlayTargetKind.Window,
            new CaptureBounds { X = 100, Y = 100, Width = 200, Height = 200 });

        var hovered = CaptureOverlayGeometry.ResolveHoverTarget(
            1500,
            900,
            [monitor, window],
            CaptureOverlayHoverMode.Control);

        Assert.AreSame(monitor, hovered);
    }

    [TestMethod]
    public void ResolveHoverTarget_ReturnsNothingWhenAutoSelectIsOff()
    {
        var window = new CaptureOverlayTarget(
            "window:app",
            "Window: App",
            CaptureOverlayTargetKind.Window,
            new CaptureBounds { X = 0, Y = 0, Width = 800, Height = 600 });

        Assert.IsNull(CaptureOverlayGeometry.ResolveHoverTarget(10, 10, [window], CaptureOverlayHoverMode.Off));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test GoatShot.slnx --filter "FullyQualifiedName~CaptureOverlayGeometryTests"`
Expected: build FAILS with `CS0117: 'CaptureOverlayGeometry' does not contain a definition for 'ResolveHoverTarget'`.

- [ ] **Step 3: Extend the target record and add the hover mode**

At the bottom of `src/GoatShot.App/Services/CaptureOverlayGeometry.cs`, replace the `CaptureOverlayTarget` record declaration with:

```csharp
public sealed record CaptureOverlayTarget(
    string Id,
    string DisplayName,
    CaptureOverlayTargetKind Kind,
    CaptureBounds Bounds,
    bool ShowInChooser = true,
    int ZOrder = 0,
    string? ParentId = null,
    long NativeHandle = 0)
{
    public override string ToString() => DisplayName;
}

/// <summary>How much detail hover auto-select resolves under the cursor.</summary>
public enum CaptureOverlayHoverMode
{
    /// <summary>Auto-select is disabled; the overlay is drag-only.</summary>
    Off,

    /// <summary>Whole windows and monitors.</summary>
    Window,

    /// <summary>Panes and controls inside the hovered window.</summary>
    Control
}
```

- [ ] **Step 4: Add the hit-test**

In the same file, add these two methods to `CaptureOverlayGeometry`, directly after `FindNearestChooserTarget`:

```csharp
    /// <summary>
    /// Screenpresso-style hover resolution: what a click right now would capture. Topmost window
    /// wins rather than smallest-area, because with overlapping windows a pure containment test
    /// picks whichever happens to be smaller instead of whichever is actually on top. Equal
    /// z-orders fall back to list order, which LINQ's stable sort preserves.
    /// </summary>
    public static CaptureOverlayTarget? ResolveHoverTarget(
        int screenX,
        int screenY,
        IReadOnlyList<CaptureOverlayTarget> targets,
        CaptureOverlayHoverMode mode)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (mode == CaptureOverlayHoverMode.Off)
        {
            return null;
        }

        var window = targets
            .Where(target => target.Kind == CaptureOverlayTargetKind.Window)
            .Where(target => ContainsPoint(Normalize(target.Bounds), screenX, screenY))
            .OrderBy(target => target.ZOrder)
            .FirstOrDefault();

        if (window is null)
        {
            return targets
                .Where(target => target.Kind == CaptureOverlayTargetKind.Monitor)
                .FirstOrDefault(target => ContainsPoint(Normalize(target.Bounds), screenX, screenY));
        }

        if (mode != CaptureOverlayHoverMode.Control)
        {
            return window;
        }

        var child = targets
            .Where(target => target.Kind is CaptureOverlayTargetKind.ContentArea or CaptureOverlayTargetKind.ControlArea)
            .Where(target => string.Equals(target.ParentId, window.Id, StringComparison.Ordinal))
            .Where(target => ContainsPoint(Normalize(target.Bounds), screenX, screenY))
            .OrderBy(Area)
            .FirstOrDefault();

        return child ?? window;
    }

    private static long Area(CaptureOverlayTarget target)
    {
        var bounds = Normalize(target.Bounds);
        return (long)bounds.Width * bounds.Height;
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test GoatShot.slnx --filter "FullyQualifiedName~CaptureOverlayGeometryTests"`
Expected: PASS, including the pre-existing snapping and lens tests.

- [ ] **Step 6: Commit**

```bash
git add src/GoatShot.App/Services/CaptureOverlayGeometry.cs src/GoatShot.Tests/CaptureOverlayGeometryTests.cs
git commit -m "feat(capture): resolve the hovered window or control under the cursor"
```

---

## Task 6: Populate z-order and enumerate child targets on demand

**Files:**
- Modify: `src/GoatShot.App/Services/CaptureOverlayTargetCatalog.cs`

**Interfaces:**
- Consumes: `CaptureOverlayTarget` with `ZOrder` / `ParentId` / `NativeHandle` (Task 5).
- Produces: `CaptureOverlayTargetCatalog.BuildChildTargets(CaptureOverlayTarget windowTarget, int maxControls = 40)` → `IReadOnlyList<CaptureOverlayTarget>`. Window targets returned by `BuildLiveTargets` now carry their enumeration index as `ZOrder` and their `HWND` as `NativeHandle`; content and control targets carry `ParentId`.

This task is P/Invoke against the live desktop, so it has no unit test — `EnumWindows` results are not reproducible in CI. Task 5 covers the decision logic on synthetic targets; verification here is the build plus Task 7's manual check.

- [ ] **Step 1: Record z-order, parent, and handle during enumeration**

In `BuildLiveTargets`, the `EnumWindows` callback currently discards the enumeration index. Replace the callback body so the index becomes the z-order (`EnumWindows` walks front to back) and children learn their parent:

```csharp
        EnumWindows((handle, _) =>
        {
            if (windowCount >= MaxWindowTargets)
            {
                return true;
            }

            if (!TryBuildWindowTarget(handle, windowCount, out var windowTarget))
            {
                return true;
            }

            targets.Add(windowTarget);
            windowCount++;

            var content = BuildContentAreaTarget(windowTarget);
            if (content is not null)
            {
                targets.Add(content);
            }

            if (controlCount < MaxControlTargets)
            {
                EnumChildWindows(handle, (child, _) =>
                {
                    if (controlCount >= MaxControlTargets)
                    {
                        return false;
                    }

                    if (TryBuildControlTarget(child, windowTarget, controlCount, out var controlTarget))
                    {
                        targets.Add(controlTarget);
                        controlCount++;
                    }

                    return true;
                }, IntPtr.Zero);
            }

            return true;
        }, IntPtr.Zero);
```

- [ ] **Step 2: Update the three target builders**

`TryBuildWindowTarget` takes the z-order and stores the handle:

```csharp
    private static bool TryBuildWindowTarget(IntPtr handle, int zOrder, out CaptureOverlayTarget target)
    {
        target = null!;
        if (handle == IntPtr.Zero || !IsWindowVisible(handle) || IsIconic(handle))
        {
            return false;
        }

        var title = GetWindowTitle(handle);
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        if (!TryGetBounds(handle, out var bounds) || bounds.Width < 120 || bounds.Height < 90)
        {
            return false;
        }

        target = new CaptureOverlayTarget(
            $"window:{handle.ToInt64():X}",
            $"Window: {TrimForDisplay(title, 72)} ({bounds.Width} x {bounds.Height})",
            CaptureOverlayTargetKind.Window,
            bounds,
            ShowInChooser: true,
            ZOrder: zOrder,
            NativeHandle: handle.ToInt64());
        return true;
    }
```

`BuildContentAreaTarget` links to its parent (the rest of the method body is unchanged; only the constructed target changes):

```csharp
        return new CaptureOverlayTarget(
            $"{windowTarget.Id}:content",
            $"Content area: {StripTargetPrefix(windowTarget.DisplayName)}",
            CaptureOverlayTargetKind.ContentArea,
            new CaptureBounds
            {
                X = window.X + sideInset,
                Y = window.Y + topInset,
                Width = Math.Max(1, window.Width - (sideInset * 2)),
                Height = Math.Max(1, window.Height - topInset - bottomInset)
            },
            ShowInChooser: false,
            ZOrder: windowTarget.ZOrder,
            ParentId: windowTarget.Id,
            NativeHandle: windowTarget.NativeHandle);
```

`TryBuildControlTarget` takes the whole parent target instead of just its bounds:

```csharp
    private static bool TryBuildControlTarget(
        IntPtr handle,
        CaptureOverlayTarget parent,
        int index,
        out CaptureOverlayTarget target)
    {
        target = null!;
        if (handle == IntPtr.Zero || !IsWindowVisible(handle))
        {
            return false;
        }

        if (!TryGetBounds(handle, out var bounds) ||
            bounds.Width < 120 ||
            bounds.Height < 70 ||
            !Contains(parent.Bounds, bounds))
        {
            return false;
        }

        var className = GetClassName(handle);
        var title = GetWindowTitle(handle);
        var label = string.IsNullOrWhiteSpace(title) ? className : title;
        if (string.IsNullOrWhiteSpace(label))
        {
            label = $"Control {index + 1}";
        }

        target = new CaptureOverlayTarget(
            $"control:{handle.ToInt64():X}",
            $"Control: {TrimForDisplay(label, 56)} ({bounds.Width} x {bounds.Height})",
            CaptureOverlayTargetKind.ControlArea,
            bounds,
            ShowInChooser: false,
            ZOrder: parent.ZOrder,
            ParentId: parent.Id,
            NativeHandle: handle.ToInt64());
        return true;
    }
```

- [ ] **Step 3: Add lazy per-window child enumeration**

Add this public method after `BuildLiveTargets`:

```csharp
    /// <summary>
    /// Child targets for a single window, resolved on demand. The eager catalog caps controls
    /// globally, which starves every window late in z-order; hover only ever needs the window under
    /// the cursor, so it asks for that one window's children instead of paying for all of them.
    /// </summary>
    public static IReadOnlyList<CaptureOverlayTarget> BuildChildTargets(
        CaptureOverlayTarget windowTarget,
        int maxControls = MaxControlTargets)
    {
        ArgumentNullException.ThrowIfNull(windowTarget);
        var targets = new List<CaptureOverlayTarget>();
        var content = BuildContentAreaTarget(windowTarget);
        if (content is not null)
        {
            targets.Add(content);
        }

        var handle = new IntPtr(windowTarget.NativeHandle);
        if (handle == IntPtr.Zero || maxControls <= 0)
        {
            return targets;
        }

        var count = 0;
        EnumChildWindows(handle, (child, _) =>
        {
            if (count >= maxControls)
            {
                return false;
            }

            if (TryBuildControlTarget(child, windowTarget, count, out var controlTarget))
            {
                targets.Add(controlTarget);
                count++;
            }

            return true;
        }, IntPtr.Zero);

        return targets;
    }
```

- [ ] **Step 4: Build and run the full suite**

Run: `dotnet build GoatShot.slnx -c Release`
Expected: build succeeds. If the compiler reports an ambiguous or missing argument at a `TryBuildControlTarget` call, the caller is still passing `windowTarget.Bounds` instead of `windowTarget` — fix the call, not the signature.

Run: `dotnet test GoatShot.slnx -c Release --no-build`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/GoatShot.App/Services/CaptureOverlayTargetCatalog.cs
git commit -m "feat(capture): record target z-order and enumerate window children on demand"
```

---

## Task 7: Hover highlight and click-to-capture on the overlay

**Files:**
- Modify: `src/GoatShot.App/Windows/RegionCaptureWindow.xaml`
- Modify: `src/GoatShot.App/Windows/RegionCaptureWindow.xaml.cs`
- Modify: `src/GoatShot.App/Services/ScreenshotService.cs:46-63`

**Interfaces:**
- Consumes: `CaptureOverlayGeometry.ResolveHoverTarget`, `CaptureOverlayHoverMode` (Task 5); `CaptureOverlayTargetCatalog.BuildChildTargets` (Task 6); `AppSettings.EnableCaptureHoverAutoSelect` (Task 2).
- Produces: `RegionCaptureWindow` constructor gains two trailing optional parameters — `bool enableHoverAutoSelect = true` and `Func<CaptureOverlayTarget, IReadOnlyList<CaptureOverlayTarget>>? childTargetProvider = null` — so the existing calls in `CaptureOverlayPreviewRenderer.cs:74` and the tests keep compiling untouched.

No unit test: this is mouse-driven WPF behavior. Verification is the build, the existing overlay renderer proof, and the manual check in Step 6.

- [ ] **Step 1: Add the hover visuals to the XAML**

In `src/GoatShot.App/Windows/RegionCaptureWindow.xaml`, add `KeyUp="Window_KeyUp"` to the `Window` element beside the existing `KeyDown="Window_KeyDown"`.

Inside `<Canvas x:Name="SelectionCanvas">`, add the hover rectangle as the **first** child, above `SelectionRectangle`, so the solid drag selection always paints over it:

```xml
            <Rectangle x:Name="HoverRectangle"
                       Visibility="Collapsed"
                       Stroke="#7FE0FF"
                       StrokeThickness="2"
                       StrokeDashArray="4 3"
                       Fill="#1A7FE0FF"
                       IsHitTestVisible="False" />
```

Update the hint text so the new gestures are discoverable — replace the existing `Text="Drag a region, ..."` value with:

```xml
                       Text="Point at a window to highlight it and click to capture it, or drag a region. Ctrl targets a pane inside the window; Shift turns auto-select and snapping off. Arrows move the keyboard selection, Shift+arrows resize. Enter captures; Esc cancels." />
```

- [ ] **Step 2: Add hover state to the code-behind**

In `src/GoatShot.App/Windows/RegionCaptureWindow.xaml.cs`, extend the fields and constructor:

```csharp
    private readonly CaptureBounds _virtualBounds;
    private readonly IReadOnlyList<CaptureOverlayTarget> _targets;
    private readonly int _contextPadding;
    private readonly bool _hoverAutoSelectEnabled;
    private readonly Func<CaptureOverlayTarget, IReadOnlyList<CaptureOverlayTarget>> _childTargetProvider;
    private readonly Dictionary<long, IReadOnlyList<CaptureOverlayTarget>> _childTargetCache = new();
    private WpfPoint? _start;
    private WpfPoint _lastHoverPosition;
    private CaptureOverlaySelection? _lastSelection;
    private CaptureOverlayTarget? _hoverTarget;

    public RegionCaptureWindow(
        BitmapSource frozenScreen,
        int contextPadding = 0,
        IReadOnlyList<CaptureOverlayTarget>? targets = null,
        CaptureBounds? virtualBounds = null,
        bool enableHoverAutoSelect = true,
        Func<CaptureOverlayTarget, IReadOnlyList<CaptureOverlayTarget>>? childTargetProvider = null)
    {
        InitializeComponent();

        _virtualBounds = virtualBounds ?? CaptureOverlayTargetCatalog.GetVirtualScreenBounds();
        _targets = targets ?? CaptureOverlayTargetCatalog.BuildLiveTargets();
        _contextPadding = Math.Clamp(contextPadding, 0, CaptureOverlayGeometry.MaxContextPadding);
        _hoverAutoSelectEnabled = enableHoverAutoSelect;
        _childTargetProvider = childTargetProvider ?? CaptureOverlayTargetCatalog.BuildChildTargets;
```

The rest of the constructor is unchanged.

- [ ] **Step 3: Resolve and draw the hover highlight**

Replace `Root_MouseMove` and add the hover helpers below it:

```csharp
    private void Root_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_start is not WpfPoint start || e.LeftButton != MouseButtonState.Pressed)
        {
            UpdateHover(e.GetPosition(Root));
            return;
        }

        UpdateSelection(start, e.GetPosition(Root));
    }

    /// <summary>
    /// Resolves what a click would capture and draws it. Control drill-down is scoped to the
    /// hovered window so the expensive child enumeration only ever runs for one window at a time.
    /// </summary>
    private void UpdateHover(WpfPoint position)
    {
        _lastHoverPosition = position;
        var mode = ResolveHoverMode();
        if (mode == CaptureOverlayHoverMode.Off)
        {
            ClearHover();
            return;
        }

        var screenX = (int)Math.Round(_virtualBounds.X + position.X);
        var screenY = (int)Math.Round(_virtualBounds.Y + position.Y);
        var target = CaptureOverlayGeometry.ResolveHoverTarget(
            screenX,
            screenY,
            _targets,
            CaptureOverlayHoverMode.Window);

        if (target is not null &&
            mode == CaptureOverlayHoverMode.Control &&
            target.Kind == CaptureOverlayTargetKind.Window)
        {
            var scoped = new List<CaptureOverlayTarget> { target };
            scoped.AddRange(GetChildTargets(target));
            target = CaptureOverlayGeometry.ResolveHoverTarget(
                screenX,
                screenY,
                scoped,
                CaptureOverlayHoverMode.Control) ?? target;
        }

        if (target is null)
        {
            ClearHover();
            return;
        }

        _hoverTarget = target;
        DrawHover(ResolveTargetSelection(target));
    }

    private CaptureOverlayHoverMode ResolveHoverMode()
    {
        if (!_hoverAutoSelectEnabled || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return CaptureOverlayHoverMode.Off;
        }

        return Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            ? CaptureOverlayHoverMode.Control
            : CaptureOverlayHoverMode.Window;
    }

    private IReadOnlyList<CaptureOverlayTarget> GetChildTargets(CaptureOverlayTarget window)
    {
        if (_childTargetCache.TryGetValue(window.NativeHandle, out var cached))
        {
            return cached;
        }

        var children = _childTargetProvider(window);
        _childTargetCache[window.NativeHandle] = children;
        return children;
    }

    private void DrawHover(CaptureOverlaySelection selection)
    {
        var bounds = selection.FinalBounds;
        var left = bounds.X - _virtualBounds.X;
        var top = bounds.Y - _virtualBounds.Y;

        Canvas.SetLeft(HoverRectangle, left);
        Canvas.SetTop(HoverRectangle, top);
        HoverRectangle.Width = bounds.Width;
        HoverRectangle.Height = bounds.Height;
        HoverRectangle.Visibility = Visibility.Visible;

        SizeText.Text = $"{bounds.Width} x {bounds.Height}";
        SelectionHintText.Text = selection.StatusText;
        Canvas.SetLeft(SizeBadge, left);
        Canvas.SetTop(SizeBadge, Math.Max(0, top - 54));
        SizeBadge.Visibility = Visibility.Visible;
    }

    private void ClearHover()
    {
        _hoverTarget = null;
        HoverRectangle.Visibility = Visibility.Collapsed;
        if (_lastSelection is null)
        {
            SizeBadge.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Runs a whole target through the drag geometry so padding and status text match.</summary>
    private CaptureOverlaySelection ResolveTargetSelection(CaptureOverlayTarget target)
    {
        var bounds = target.Bounds;
        return CaptureOverlayGeometry.ResolveSelection(
            bounds.X,
            bounds.Y,
            bounds.X + bounds.Width,
            bounds.Y + bounds.Height,
            new CaptureOverlayGeometryOptions(
                _virtualBounds,
                [target],
                CaptureOverlayGeometry.DefaultSnapThreshold,
                _contextPadding));
    }
```

- [ ] **Step 4: Turn a click into a capture, and hide the hover while dragging**

In `Root_MouseLeftButtonDown`, hide the hover rectangle when the drag begins by adding one line after `LensBorder.Visibility = Visibility.Visible;`:

```csharp
        HoverRectangle.Visibility = Visibility.Collapsed;
```

Replace the short-selection branch of `Root_MouseLeftButtonUp` so a click captures the highlight instead of cancelling:

```csharp
        var selection = _lastSelection;
        if (selection is null || selection.RawBounds.Width < 3 || selection.RawBounds.Height < 3)
        {
            // A click with no drag captures whatever the hover highlight was showing. From here on
            // only Esc cancels, so a stray click no longer throws the capture away.
            if (_hoverTarget is { } hovered)
            {
                SelectedBounds = ResolveTargetSelection(hovered).FinalBounds;
                DialogResult = true;
                return;
            }

            DialogResult = false;
            return;
        }
```

Then simplify `PreviewTarget` to reuse the shared helper — replace its body with:

```csharp
    private CaptureOverlaySelection PreviewTarget(CaptureOverlayTarget target)
    {
        SelectionRectangle.Visibility = Visibility.Visible;
        SizeBadge.Visibility = Visibility.Visible;
        LensBorder.Visibility = Visibility.Collapsed;
        HoverRectangle.Visibility = Visibility.Collapsed;

        var selection = ResolveTargetSelection(target);
        _lastSelection = selection;
        DrawSelection(selection);
        return selection;
    }
```

- [ ] **Step 5: Refresh the highlight when modifiers change**

Add the modifier branch at the very top of `Window_KeyDown`, before the Escape check:

```csharp
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift)
        {
            RefreshHover();
            return;
        }
```

And add the new handler plus its helper next to it:

```csharp
    private void Window_KeyUp(object sender, WpfKeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift)
        {
            RefreshHover();
        }
    }

    /// <summary>Re-resolves the highlight after a modifier change, without needing a mouse move.</summary>
    private void RefreshHover()
    {
        if (_start is not null && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            return;
        }

        UpdateHover(_lastHoverPosition);
    }
```

- [ ] **Step 6: Pass the setting through ScreenshotService**

In `src/GoatShot.App/Services/ScreenshotService.cs`, update the overlay construction inside `SelectRegionBounds`:

```csharp
        var overlay = new RegionCaptureWindow(
            source,
            _settings.CaptureContextPadding,
            enableHoverAutoSelect: _settings.EnableCaptureHoverAutoSelect);
```

- [ ] **Step 7: Build and run the full suite**

Run: `dotnet build GoatShot.slnx -c Release`
Expected: build succeeds. `CaptureOverlayPreviewRenderer.cs:74` and the overlay tests use the original four parameters and must still compile unchanged.

Run: `dotnet test GoatShot.slnx -c Release --no-build`
Expected: PASS.

- [ ] **Step 8: Manual smoke check**

Trigger a region capture and, without pressing the mouse button, move the cursor over a few windows. Expected:
- each window highlights with a dashed outline and its size/name badge;
- holding **Ctrl** narrows the highlight to the pane under the cursor;
- holding **Shift** removes the highlight entirely and dragging behaves exactly as before;
- a single click on a highlighted window captures that window;
- a drag still produces a free region with snapping and the pixel lens;
- moving over empty desktop highlights the whole monitor;
- **Esc** still cancels.

- [ ] **Step 9: Commit**

```bash
git add src/GoatShot.App/Windows/RegionCaptureWindow.xaml src/GoatShot.App/Windows/RegionCaptureWindow.xaml.cs src/GoatShot.App/Services/ScreenshotService.cs
git commit -m "feat(capture): highlight the hovered window and capture it on click"
```

---

## Task 8: Documentation

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: everything above. Produces nothing consumed by later tasks.

- [ ] **Step 1: Find the capture section**

Run: `grep -n "PrintScreen\|Capture" README.md | head -20`
Expected: the hotkey/capture documentation block. Read the surrounding 40 lines so the new text matches the file's voice and heading depth.

- [ ] **Step 2: Document both behaviors**

Add to the capture section, matching the surrounding formatting:

- The default post-capture behavior is a quiet clipboard copy: the capture is saved to the workspace and copied, with no window. Settings → General → **After capture** switches to the capture actions window or straight into the editor.
- On the capture overlay, pointing at a window highlights it and a click captures it. **Ctrl** targets a pane inside that window, **Shift** turns auto-select and snapping off, and dragging still selects a free region. Settings → General has a toggle to disable the highlight.

- [ ] **Step 3: Run the full suite one last time**

Run: `dotnet build GoatShot.slnx -c Release` then `dotnet test GoatShot.slnx -c Release --no-build`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: describe quiet capture defaults and hover auto-select"
```
