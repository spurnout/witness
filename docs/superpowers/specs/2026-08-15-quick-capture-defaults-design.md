# Quick-capture defaults: silent clipboard capture + hover window auto-select

Date: 2026-08-15

## Problem

Two things make the current capture flow slower than Screenpresso for the "grab it and
paste it somewhere" workflow:

1. **Every capture opens the actions window.** `CaptureAndStoreAsync` unconditionally calls
   `ShowCaptureTaskWindow(item)` (`src/GoatShot.App/MainWindow.xaml.cs:708`), so a capture that
   was already copied to the clipboard still puts a window on screen that has to be dismissed.
2. **The overlay has no hover auto-select.** The target catalog (monitors, windows, content
   areas, child controls) already exists and drives edge snapping and the dropdown picker, but
   moving the mouse over a window highlights nothing, and a plain click cancels the capture
   instead of grabbing what is under the cursor.

`spec.md:156` already calls for "edge snapping / window-area auto-detection while selecting",
so item 2 closes a specified gap rather than adding scope.

## Goals

- Default capture is silent: copy to the clipboard, save to the library, no window.
- Post-capture behavior is configurable in one place.
- Hovering the overlay highlights the window (or control) under the cursor; a click captures it.
- Dragging, snapping, the pixel lens, keyboard selection, and the dropdown picker keep working
  exactly as they do now.

## Non-goals

- Redesigning the actions window or the editor.
- Per-hotkey or per-capture-type behavior overrides.
- Changing recording, replay, or clipboard-import flows.

---

## 1. Post-capture behavior

### Setting

`AppSettings` gains one string field, following the existing convention for enum-like settings
(`DefaultShareDestination`, `QualityProfile` are stored the same way, and `SettingsStore` has no
`JsonStringEnumConverter`):

```csharp
public string PostCaptureAction { get; set; } = "CopyQuietly";
public bool EnableCaptureHoverAutoSelect { get; set; } = true;
```

Valid values: `CopyQuietly`, `ShowActionsWindow`, `OpenEditor`.

### New files

- `Models/PostCaptureAction.cs` — the enum.
- `Services/PostCaptureActionCatalog.cs` — parse, normalize, describe, and expose the option list
  the settings ComboBox binds to. Pure and unit-testable. Unknown, empty, or whitespace values
  normalize to `CopyQuietly`.

### Behavior

`CaptureAndStoreAsync` switches on the normalized action instead of always showing the window:

| Value | Behavior |
| --- | --- |
| `CopyQuietly` | Save, copy (if `AutoCopyImageAfterCapture`), update the status line. No window. |
| `ShowActionsWindow` | Today's behavior: the capture actions window opens. |
| `OpenEditor` | The editor opens directly on the new capture. |

`CaptureTask_Click` (`MainWindow.xaml.cs:1487`) is unaffected — it is an explicit user request on
an already-selected capture and always opens the window.

### Quiet-mode feedback

With the workspace hidden (the tray-only hotkey path), quiet mode would otherwise give no signal
that the capture succeeded. `TrayService` already owns a `Forms.NotifyIcon`, so quiet mode shows a
short balloon tip naming the saved file — but **only when the main window is not visible**, since
the status line already covers the visible case. No new window, nothing to dismiss.

### Interaction with `AutoCopyImageAfterCapture`

The two settings stay orthogonal. `AutoCopyImageAfterCapture` governs copying in all three modes;
`PostCaptureAction` governs what UI appears. Unchecking the copy box while quiet mode is selected
yields a silent save-only capture, which is a legitimate choice. The helper text under the
dropdown states this explicitly so the combination is not surprising.

### Migration

`SettingsMigrationService.CurrentSchemaVersion` goes 17 → 18. The migration normalizes
`PostCaptureAction` (blank or unrecognized → `CopyQuietly`). Existing installs land on
`CopyQuietly`; the new default is deliberately applied to everyone rather than preserving the old
always-show-window behavior.

### Settings UI

A labeled ComboBox in the General section, immediately above the existing "Copy image to
clipboard after capture" checkbox, plus a checkbox for `EnableCaptureHoverAutoSelect`. Both get
`AutomationProperties.Name` values for `SettingsWindowAccessibilityAuditor`. Dirty tracking
(`SettingsWindow.Dirty.cs`) and the Ctrl+K settings search both walk the logical tree, so neither
needs registration work.

`DiagnosticBundleService` logs the new values alongside `AutoCopyImageAfterCapture`.

---

## 2. Hover auto-select on the capture overlay

### Target model

`CaptureOverlayTarget` gains three fields with defaults, so existing positional constructions keep
compiling:

```csharp
int ZOrder = 0,
string? ParentId = null,
long NativeHandle = 0
```

`CaptureOverlayTargetCatalog` populates them. `EnumWindows` already enumerates top-down in
z-order, so `ZOrder` is the enumeration index.

### Hit-testing

New pure function on `CaptureOverlayGeometry`:

```csharp
public static CaptureOverlayTarget? ResolveHoverTarget(
    int screenX,
    int screenY,
    IReadOnlyList<CaptureOverlayTarget> targets,
    CaptureOverlayHoverMode mode)
```

Rules, in order:

1. **Topmost window wins.** Among `Window`-kind targets containing the point, choose the lowest
   `ZOrder`. Deliberately *not* the smallest area — with overlapping windows, smallest-area
   containment picks the wrong one. Equal `ZOrder` values (the default, and what hand-built test
   fixtures will have) break by list order, so the result is always deterministic.
2. **Control drill-down.** In `Control` mode, among `ContentArea`/`ControlArea` targets that
   belong to the chosen window (via `ParentId`) and contain the point, choose the smallest area.
   Fall back to the window itself when there is none.
3. **Monitor fallback.** When no window contains the point, return the `Monitor` target containing
   it, so hovering bare desktop highlights the whole screen.
4. **Suppressed.** In `Off` mode, return `null`.

`CaptureOverlayHoverMode` is `Off | Window | Control`.

### Lazy control enumeration

The eager catalog caps child controls at 40 **globally**, which means windows late in z-order get
none at all. That is acceptable for snapping but breaks Ctrl drill-down. Control targets for hover
are therefore enumerated on demand for the hovered window only, keyed and cached by
`NativeHandle`, which removes the cap entirely for the window you are actually pointing at. The
eager catalog keeps its current caps for snapping and the dropdown picker.

### Overlay interaction

- `RegionCaptureWindow` gets a dashed `HoverRectangle` visually distinct from the solid drag
  `SelectionRectangle`, hidden while a drag is in progress.
- Hover resolution runs on `MouseMove` when the left button is up. The hovered target is fed
  through the existing `ResolveSelection` as a single-target option set, so context padding, the
  size badge, and the status text stay identical to the drag path.
- **Click captures.** On mouse-up, when the drag moved less than 3px and a hover target exists,
  capture that target. Today the same gesture cancels the capture; from now on only Esc cancels.
- **Ctrl** switches to `Control` mode (drill into panes and controls). **Shift** suppresses
  auto-select entirely — consistent with its existing "ignore snap while dragging" meaning.
- Key-down and key-up both refresh the highlight, so modifier changes take effect without moving
  the mouse.
- When `EnableCaptureHoverAutoSelect` is false, the mode is `Off` and the overlay behaves exactly
  as it does today.

Hit-testing is a list scan over roughly 100 targets per mouse move — no measurable cost.

---

## 3. Tests

- `CaptureOverlayGeometryTests` (existing file, existing conventions):
  - topmost window wins over a larger, lower-z-order window containing the same point;
  - `Control` mode returns the smallest child target belonging to the hovered window;
  - `Control` mode falls back to the window when it has no matching children;
  - a point over no window returns the containing monitor;
  - `Off` mode returns null.
- New `PostCaptureActionCatalogTests`: each valid value round-trips; unknown, empty, and
  whitespace values normalize to `CopyQuietly`; the option list covers every enum member.
- Settings migration test: a v17 blob without `PostCaptureAction` migrates to v18 with
  `CopyQuietly`.
- The existing renderer and accessibility auditors already cover the overlay and the settings
  window; new controls carry automation names so those stay green.

## Files touched

| File | Change |
| --- | --- |
| `Models/PostCaptureAction.cs` | New enum |
| `Models/AppSettings.cs` | `PostCaptureAction`, `EnableCaptureHoverAutoSelect` |
| `Services/PostCaptureActionCatalog.cs` | New parse/normalize/describe service |
| `Services/SettingsMigrationService.cs` | Schema 17 → 18, normalize new field |
| `Services/CaptureOverlayGeometry.cs` | `ResolveHoverTarget`, `CaptureOverlayHoverMode`, target fields |
| `Services/CaptureOverlayTargetCatalog.cs` | Populate z-order/parent/handle, lazy per-window controls |
| `Services/TrayService.cs` | Quiet-mode balloon tip |
| `Services/DiagnosticBundleService.cs` | Log the new settings |
| `Windows/RegionCaptureWindow.xaml(.cs)` | Hover highlight, click-to-capture, modifiers |
| `Windows/SettingsWindow.xaml(.cs)` | Post-capture dropdown, hover toggle |
| `MainWindow.xaml.cs` | Switch on `PostCaptureAction` instead of always showing the window |
| `src/GoatShot.Tests/*` | Geometry, catalog, and migration tests |

## Accepted limitations

- The eager catalog still caps at 30 windows for **snapping**. A window buried 31-deep in z-order
  will not snap during a drag, though hover still resolves it through the monitor fallback.
  Raising that cap is a separate performance question and is out of scope here.
- Window bounds for hover come from live `GetWindowRect` calls while the frozen background is a
  point-in-time screenshot. An app that animates its own geometry during the overlay can show a
  highlight that disagrees with the frozen pixels by a few pixels. This is already true of the
  existing snapping path.
