# Recall & Live Text Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every capture text-searchable via background local OCR, turn the OCR hotkey into a save-free text grab, let two captures be compared with word-level change highlights, stamp richer capture provenance (URL + actually-clicked window), and add drag-to-copy Live Text on the library preview.

**Architecture:** A new `OcrIndexWorkerService` (modeled on `UploadQueueWorkerService`) treats the library itself as an idempotent queue: any non-private image without `OcrRecognizedAt` gets OCR'd on a worker thread and batch-persisted through a new `WorkspaceStore.UpdateItemsAsync`. All decision logic lives in pure statics (`OcrIndexPolicy`, `TextGrabPresenter`, `CaptureComparisonService`, `PixelGridDiff`, `OcrWordSelectionService`); the WPF surfaces only render. Comparison reuses `ReceiptSceneAnalysisService.CompareTexts` for verdicts and recomputes token sets through a shared tokenizer helper so highlights can never drift from the verdict.

**Tech Stack:** C# / .NET 10 (`net10.0-windows10.0.19041.0`), WPF, MSTest, Windows.Media.Ocr, Microsoft.Data.Sqlite (FTS5).

Approved scope decisions: text grab is text-only (no library item); OCR indexing covers new captures + backfill, enabled by default; Live Text ships in the library preview.

## Global Constraints

- Build: `dotnet build GoatShot.slnx`. Test: `dotnet test GoatShot.slnx` (Debug — the user runs Receipts from `bin\Release`, which locks `Receipts.exe`; never kill the tray app for a build). Filtered runs during development; the full suite must pass before each commit.
- Never throw on bad settings or bad library state; degrade and keep capturing.
- Product name in user-facing strings comes from `BrandIdentity.ProductName`.
- New WPF controls need `AutomationProperties.Name` or the accessibility auditors flag them.
- `Nullable enable`; alias WPF/WinForms collisions the way each file already does.
- Comments explain *why*, sparse, `///` on non-obvious public members.
- Commit after each task with a conventional-commit message.

## File Structure

| File | Responsibility |
| --- | --- |
| `src/GoatShot.App/Services/WorkspaceStore.cs` | + `UpdateItemsAsync` batch write; `SourceUrl` in `BuildItem`. |
| `src/GoatShot.App/Services/OcrIndexWorkerService.cs` | **New.** Background OCR worker + `OcrIndexPolicy` pure statics. |
| `src/GoatShot.App/Services/AutomationService.cs` | Private-capture persistence guard in `RunOcrAsync`. |
| `src/GoatShot.App/Services/TextGrabPresenter.cs` | **New.** Pure clipboard/status composition for the text grab. |
| `src/GoatShot.App/Services/TrayMenuActionCatalog.cs` + `TrayService.cs` | Text-grab tray entry. |
| `src/GoatShot.App/Models/CaptureSource.cs` + `CaptureItem.cs` | + `SourceUrl`. |
| `src/GoatShot.App/Services/BrowserExtensionNativeBridgeService.cs` | Stamp `SourceUrl` on both import paths. |
| `src/GoatShot.App/Services/WorkspaceMetadataIndex.cs` | + `source_window_title`, `source_url` columns and FTS sentinel swap. |
| `src/GoatShot.App/Windows/RegionCaptureWindow.xaml.cs` | + `SelectedTarget`. |
| `src/GoatShot.App/Services/ScreenshotService.cs` | Stamp the clicked target's context; `ShouldStampFromTarget`. |
| `src/GoatShot.App/Services/CaptureComparisonService.cs` | **New.** Pure comparison model + `PixelGridDiff`. |
| `src/GoatShot.App/Services/ReceiptSceneAnalysisService.cs` | + `TokenizeForComparison` internal helper. |
| `src/GoatShot.App/Windows/CompareWindow.xaml(.cs)` | **New.** Side-by-side compare surface. |
| `src/GoatShot.App/Services/OcrWordSelectionService.cs` | **New.** Pure rect→words→text resolver. |
| `src/GoatShot.App/MainWindow.xaml(.cs)` | Worker wiring, text-grab command, Compare button/palette, Live Text preview. |
| `src/GoatShot.App/Windows/SettingsWindow.xaml(.cs)` | `EnableOcrIndexing` checkbox. |
| `src/GoatShot.App/Services/AppServices.cs` | Construct/dispose the worker. |
| `src/GoatShot.App/Services/DiagnosticBundleService.cs` | Log the new setting. |

---

## Task 1: `WorkspaceStore.UpdateItemsAsync` batch write

**Files:**
- Modify: `src/GoatShot.App/Services/WorkspaceStore.cs:156-185`
- Test: create `src/GoatShot.Tests/WorkspaceStoreBatchUpdateTests.cs`

**Interfaces:**
- Consumes: existing `Load()`, `_gate`, `JsonOptions`, `_paths.IndexPath`, `_metadataIndex`.
- Produces: `public Task UpdateItemsAsync(IReadOnlyList<CaptureItem> items)` — one JSON rewrite for N items, then per-item `Upsert`. `UpdateItemAsync` delegates to it.

- [ ] **Step 1: Write the failing tests**

Create `src/GoatShot.Tests/WorkspaceStoreBatchUpdateTests.cs`. Use the temp-root env-var pattern (`RECEIPTS_LOCAL_ROOT`/`RECEIPTS_LIBRARY_ROOT`) that `WorkspaceMetadataIndexReceiptTests` uses, drawing real 1x1 PNGs with `System.Drawing.Bitmap` for `AddImageFileAsync`:

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
[DoNotParallelize]
public sealed class WorkspaceStoreBatchUpdateTests
{
    private static void WithTempStore(Action<WorkspaceStore, AppPaths> body)
    {
        var root = Path.Combine(Path.GetTempPath(), $"receipts-batch-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("RECEIPTS_LOCAL_ROOT", root);
        Environment.SetEnvironmentVariable("RECEIPTS_LIBRARY_ROOT", Path.Combine(root, "library"));
        try
        {
            var paths = new AppPaths();
            paths.EnsureCreated();
            body(new WorkspaceStore(paths, new AppSettings()), paths);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RECEIPTS_LOCAL_ROOT", null);
            Environment.SetEnvironmentVariable("RECEIPTS_LIBRARY_ROOT", null);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static string WritePng(AppPaths paths, string name)
    {
        var path = Path.Combine(paths.TempRoot, name);
        using var bitmap = new Bitmap(1, 1);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    [TestMethod]
    public void UpdateItemsAsync_PersistsEveryItemInOneCall()
    {
        WithTempStore((store, paths) =>
        {
            var items = new List<CaptureItem>();
            for (var i = 0; i < 3; i++)
            {
                items.Add(store.AddImageFileAsync(WritePng(paths, $"a{i}.png"), CaptureKind.Imported, null, null, null).Result);
            }

            for (var i = 0; i < items.Count; i++)
            {
                items[i].OcrText = $"token{i}";
            }

            store.UpdateItemsAsync(items).Wait();

            var reloaded = store.Load();
            Assert.AreEqual(3, reloaded.Count);
            CollectionAssert.AreEquivalent(
                new[] { "token0", "token1", "token2" },
                reloaded.Select(item => item.OcrText).ToArray());
        });
    }

    [TestMethod]
    public void UpdateItemsAsync_InsertsUnknownItemsLikeTheSingleItemPathDoes()
    {
        WithTempStore((store, paths) =>
        {
            var known = store.AddImageFileAsync(WritePng(paths, "known.png"), CaptureKind.Imported, null, null, null).Result;
            known.OcrText = "known";
            var fresh = new CaptureItem
            {
                Kind = CaptureKind.Imported,
                CreatedAt = DateTimeOffset.Now,
                FilePath = WritePng(paths, "fresh.png"),
                OcrText = "fresh"
            };

            store.UpdateItemsAsync([known, fresh]).Wait();

            var reloaded = store.Load();
            Assert.AreEqual(2, reloaded.Count);
            Assert.IsNotNull(reloaded.SingleOrDefault(item => item.OcrText == "fresh"));
        });
    }

    [TestMethod]
    public void UpdateItemsAsync_UpsertsEachItemIntoTheMetadataIndex()
    {
        WithTempStore((store, paths) =>
        {
            var index = new WorkspaceMetadataIndex(paths);
            store.AttachMetadataIndex(index);
            var item = store.AddImageFileAsync(WritePng(paths, "indexed.png"), CaptureKind.Imported, null, null, null).Result;
            item.OcrText = "zanzibar";

            store.UpdateItemsAsync([item]).Wait();

            Assert.IsTrue(index.SearchIds("zanzibar").Contains(item.Id, StringComparer.OrdinalIgnoreCase));
        });
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test GoatShot.slnx --filter "FullyQualifiedName~WorkspaceStoreBatchUpdateTests"`
Expected: build FAILS with `CS1061: 'WorkspaceStore' does not contain a definition for 'UpdateItemsAsync'`.
(If `AppPaths`/`AddImageFileAsync` signatures differ from the sketch, adapt the test to the real signatures first — the assertion targets stay the same.)

- [ ] **Step 3: Implement the batch method and delegate the single-item path**

In `src/GoatShot.App/Services/WorkspaceStore.cs`, replace `UpdateItemAsync` (lines 156-185) with:

```csharp
    public Task UpdateItemAsync(CaptureItem item) => UpdateItemsAsync([item]);

    /// <summary>
    /// Batched metadata write: one JSON rewrite regardless of how many items changed. The
    /// background OCR worker persists whole chunks through this so a large backfill stays
    /// O(chunks), not O(items), on the index file.
    /// </summary>
    public async Task UpdateItemsAsync(IReadOnlyList<CaptureItem> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        await Task.Run(() =>
        {
            lock (_gate)
            {
                var existingItems = Load().ToList();
                foreach (var item in items)
                {
                    var index = existingItems.FindIndex(existing =>
                        existing.Id.Equals(item.Id, StringComparison.OrdinalIgnoreCase) ||
                        existing.FilePath.Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));

                    if (index >= 0)
                    {
                        existingItems[index] = item;
                    }
                    else
                    {
                        existingItems.Insert(0, item);
                    }
                }

                var json = JsonSerializer.Serialize(
                    existingItems.OrderByDescending(existing => existing.CreatedAt).ToList(),
                    JsonOptions);
                Directory.CreateDirectory(Path.GetDirectoryName(_paths.IndexPath)!);
                File.WriteAllText(_paths.IndexPath, json);
            }

            foreach (var item in items)
            {
                _metadataIndex?.Upsert(item);
            }
        });
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test GoatShot.slnx --filter "FullyQualifiedName~WorkspaceStoreBatchUpdateTests"`
Expected: PASS, 3 tests. Then run `dotnet test GoatShot.slnx --filter "FullyQualifiedName~WorkspaceStore|FullyQualifiedName~Automation"` to confirm the delegation broke nothing.

- [ ] **Step 5: Commit**

```bash
git add src/GoatShot.App/Services/WorkspaceStore.cs src/GoatShot.Tests/WorkspaceStoreBatchUpdateTests.cs
git commit -m "feat(workspace): add batched UpdateItemsAsync to avoid O(N^2) index rewrites"
```

---

## Task 2: `EnableOcrIndexing` setting

**Files:**
- Modify: `src/GoatShot.App/Models/AppSettings.cs` (near `OcrLanguageTag`)
- Modify: `src/GoatShot.App/Windows/SettingsWindow.xaml` (under the OCR language control), `SettingsWindow.xaml.cs:295` (load), `:1451` (save)
- Modify: `src/GoatShot.App/Services/DiagnosticBundleService.cs` (`BuildRedactedSettings`)
- Test: `src/GoatShot.Tests/SettingsMigrationServiceTests.cs`

**Interfaces:**
- Produces: `AppSettings.EnableOcrIndexing` (`bool`, default `true`). No schema bump (plain bool; precedent `EnableCaptureHoverAutoSelect`).

- [ ] **Step 1: Failing test** — append to `SettingsMigrationServiceTests`:

```csharp
    [TestMethod]
    public void NewSettings_DefaultToBackgroundOcrIndexingOn()
    {
        var settings = new AppSettings();

        Assert.IsTrue(settings.EnableOcrIndexing);
        Assert.AreEqual(SettingsMigrationService.CurrentSchemaVersion, settings.SettingsSchemaVersion);
    }
```

- [ ] **Step 2: Verify RED** — `dotnet test GoatShot.slnx --filter "FullyQualifiedName~SettingsMigrationServiceTests"` → CS1061 on `EnableOcrIndexing`.

- [ ] **Step 3: Implement** — `AppSettings.cs`: add `public bool EnableOcrIndexing { get; set; } = true;` directly under `OcrLanguageTag`. `SettingsWindow.xaml`: under the OCR language control add

```xml
                <CheckBox x:Name="OcrIndexingBox"
                          Content="Index new captures with local OCR in the background"
                          AutomationProperties.Name="Index new captures with local OCR in the background" />
                <TextBlock Text="Runs Windows OCR on saved captures so library search can find text inside them. Local only; existing captures are indexed gradually."
                           Foreground="{StaticResource MutedInkBrush}"
                           FontSize="12"
                           TextWrapping="Wrap"
                           Margin="0,2,0,10" />
```

`SettingsWindow.xaml.cs`: `OcrIndexingBox.IsChecked = settings.EnableOcrIndexing;` beside line 295; `settings.EnableOcrIndexing = OcrIndexingBox.IsChecked == true;` beside line 1451. `DiagnosticBundleService.BuildRedactedSettings`: add `_settings.EnableOcrIndexing,` beside the other capture settings.

- [ ] **Step 4: Verify GREEN** — same filter, PASS (all pre-existing migration tests included).

- [ ] **Step 5: Commit**

```bash
git add src/GoatShot.App/Models/AppSettings.cs src/GoatShot.App/Windows/SettingsWindow.xaml src/GoatShot.App/Windows/SettingsWindow.xaml.cs src/GoatShot.App/Services/DiagnosticBundleService.cs src/GoatShot.Tests/SettingsMigrationServiceTests.cs
git commit -m "feat(settings): add EnableOcrIndexing toggle (default on)"
```

---

## Task 3: OCR index worker + policy + automation private-leak fix

**Files:**
- Create: `src/GoatShot.App/Services/OcrIndexWorkerService.cs`
- Modify: `src/GoatShot.App/Services/AutomationService.cs:499`
- Test: create `src/GoatShot.Tests/OcrIndexWorkerServiceTests.cs`

**Interfaces:**
- Consumes: `WorkspaceStore.Load/UpdateItemsAsync` (Task 1), `AppSettings.EnableOcrIndexing` (Task 2), `OcrRecognitionResult`, `SensitiveTextDetector.Scan`.
- Produces (all in the new file):

```csharp
public static class OcrIndexPolicy
{
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

    public static bool IsIndexable(CaptureItem item) =>
        !item.IsPrivate &&
        item.OcrRecognizedAt is null &&
        ImageExtensions.Contains(Path.GetExtension(item.FilePath));

    /// <summary>Newest first so fresh captures become searchable before deep history.</summary>
    public static IReadOnlyList<CaptureItem> SelectNextBatch(
        IEnumerable<CaptureItem> items, int batchSize, IReadOnlySet<string> skippedIds)
    {
        return items
            .Where(IsIndexable)
            .Where(item => !skippedIds.Contains(item.Id))
            .OrderByDescending(item => item.CreatedAt)
            .Take(Math.Max(1, batchSize))
            .ToList();
    }

    /// <summary>
    /// Backfilled history stays silent for automation: a first launch over a large library must
    /// not fire an OcrCompleted rule (which can share or upload) once per historical capture.
    /// </summary>
    public static bool ShouldRaiseOcrCompleted(CaptureItem item, DateTimeOffset workerStartedAt) =>
        item.CreatedAt >= workerStartedAt;

    /// <summary>Replaces any previous scan note the way the manual OCR path does.</summary>
    public static string? MergeScanNote(string? existingNotes, string scanSummary) { ... }
}

public sealed record OcrIndexPassResult(int Scanned, int Indexed, int Failed, string Message);

public sealed class OcrIndexWorkerService : IDisposable
{
    public OcrIndexWorkerService(
        AppSettings settings,
        WorkspaceStore workspaceStore,
        Func<string, CancellationToken, Task<OcrRecognitionResult>> recognizeAsync,
        Func<CaptureItem, Task>? onOcrCompletedAsync = null);
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<CaptureItem>? ItemIndexed;
    public bool IsRunning { get; }
    public string LastStatus { get; }
    public void Start();            // Stop("...disabled.") when !EnableOcrIndexing
    public void Stop(string? reason = null);
    public void Restart();
    public void Nudge();            // _timer?.Change(2s, Period) — never OCRs on the caller's thread
    public Task<OcrIndexPassResult> ProcessOnceAsync(CancellationToken ct = default);
    public void Dispose();
}
```

Worker internals: timer first-due 10 s, period 20 s, batch of 3; 0-wait `SemaphoreSlim` gate, disposed-exception swallowing, and `SetStatus` shape copied from `UploadQueueWorkerService`; fresh `workspaceStore.Load()` per pass; per-session `HashSet<string> _failedIds` (failed items retry next launch, never this session); after 3 consecutive passes where every attempted item failed, `Stop("OCR indexing paused: recognition keeps failing on this device.")`; per success set `OcrText/OcrLanguageTag/OcrRecognizedAt/OcrWords`, `Notes = OcrIndexPolicy.MergeScanNote(item.Notes, scan.Summary)`; persist the chunk via `UpdateItemsAsync`; raise `ItemIndexed` per item; `await onOcrCompletedAsync(item)` only when `ShouldRaiseOcrCompleted`. Internal CTS: `Dispose` cancels it before disposing the timer and gate.

- [ ] **Step 1: Write the failing tests** (`OcrIndexWorkerServiceTests.cs`) — policy tests (`IsIndexable_SkipsPrivateVideoAndAlreadyIndexedItems`, `SelectNextBatch_ReturnsNewestFirstAndHonorsSkipList`, `ShouldRaiseOcrCompleted_FiresOnlyForItemsCapturedAfterWorkerStart`, `MergeScanNote_ReplacesThePreviousScanLine`) plus worker tests against a real temp `WorkspaceStore` and an injected fake recognizer: `ProcessOnceAsync_IndexesABatchAndPersistsResults`, `ProcessOnceAsync_RecordsFailuresAndDoesNotRetryThemThisSession`, `ProcessOnceAsync_DoesNothingWhenIndexingDisabled`, `ProcessOnceAsync_HonorsCancellationBeforePersisting`.
- [ ] **Step 2: Verify RED** — `dotnet test GoatShot.slnx --filter "FullyQualifiedName~OcrIndexWorkerServiceTests"` → CS0246 `OcrIndexWorkerService`.
- [ ] **Step 3: Implement the file per the interface block above.**
- [ ] **Step 4: Automation guard** — `AutomationService.cs:499` becomes:

```csharp
        if (!item.IsPrivate)
        {
            // Private captures must never be written into workspace-index.json; the manual OCR
            // path already guards this and the SQLite Upsert refuses private items on its own.
            await _workspaceStore.UpdateItemAsync(item);
        }
```

- [ ] **Step 5: Verify GREEN + neighbors** — worker filter PASS; `dotnet test GoatShot.slnx --filter "FullyQualifiedName~AutomationServiceTests"` PASS.
- [ ] **Step 6: Commit**

```bash
git add src/GoatShot.App/Services/OcrIndexWorkerService.cs src/GoatShot.App/Services/AutomationService.cs src/GoatShot.Tests/OcrIndexWorkerServiceTests.cs
git commit -m "feat(ocr): background OCR index worker with library-as-queue backfill; fix private-capture leak in automation OCR"
```

---

## Task 4: Wire the worker into the app

**Files:**
- Modify: `src/GoatShot.App/Services/AppServices.cs` (construct after automation ~`:270`; expose `public OcrIndexWorkerService OcrIndexWorker { get; }`; dispose beside the upload worker `:578`)
- Modify: `src/GoatShot.App/MainWindow.xaml.cs` (`:206` start/subscribe, `:700` nudge, `:3191` restart)

**Interfaces:** consumes Task 3's surface exactly as declared.

- [ ] **Step 1: AppServices** — construct `new OcrIndexWorkerService(settings, workspaceStore, (path, ct) => ocr.RecognizeFileAsync(path, cancellationToken: ct), item => automation.ProcessOcrCompletedAsync(item))`; property + `Dispose()` entry.
- [ ] **Step 2: MainWindow** — in the same startup block as the upload worker (`:206`, audit-mode-guarded):

```csharp
        _services.OcrIndexWorker.StatusChanged += (_, message) => Dispatcher.Invoke(() => SetStatus(message));
        _services.OcrIndexWorker.ItemIndexed += OcrIndexWorker_ItemIndexed;
        _services.OcrIndexWorker.Start();
```

After `await _services.Automation.ProcessCaptureCreatedAsync(item);` (`:700`): `if (!item.IsPrivate) { _services.OcrIndexWorker.Nudge(); }`. Beside the upload worker restart after settings save (`:3191`): `_services.OcrIndexWorker.Restart();`. Handler:

```csharp
    /// <summary>Copies worker results onto the in-memory item without stealing selection.</summary>
    private void OcrIndexWorker_ItemIndexed(object? sender, CaptureItem indexed)
    {
        Dispatcher.Invoke(() =>
        {
            var existing = _allCaptures.FirstOrDefault(item =>
                item.Id.Equals(indexed.Id, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return;
            }

            existing.OcrText = indexed.OcrText;
            existing.OcrLanguageTag = indexed.OcrLanguageTag;
            existing.OcrRecognizedAt = indexed.OcrRecognizedAt;
            existing.OcrWords = indexed.OcrWords;
            existing.Notes = indexed.Notes;
            if (ReferenceEquals(CaptureList.SelectedItem, existing))
            {
                UpdateCaptureDetails(existing);
            }
        });
    }
```

- [ ] **Step 3: Build + full suite** — `dotnet build GoatShot.slnx` clean; `dotnet test GoatShot.slnx` PASS.
- [ ] **Step 4: Commit**

```bash
git add src/GoatShot.App/Services/AppServices.cs src/GoatShot.App/MainWindow.xaml.cs
git commit -m "feat(ocr): run the OCR index worker at startup and nudge it after each capture"
```

---

## Task 5: Text-grab rework (Ctrl+Shift+O becomes clipboard-only)

**Files:**
- Create: `src/GoatShot.App/Services/TextGrabPresenter.cs`
- Modify: `src/GoatShot.App/MainWindow.xaml.cs:1417-1424` + hotkey call site `:283-285`
- Modify: `src/GoatShot.App/Services/KeybindCatalog.cs:70-71`
- Test: create `src/GoatShot.Tests/TextGrabPresenterTests.cs`

**Interfaces:**

```csharp
public static class TextGrabPresenter
{
    public sealed record TextGrabPayload(bool HasText, string ClipboardText, string StatusMessage, bool Redacted);
    public static TextGrabPayload Compose(OcrRecognitionResult result);
}
```

Rules: `!result.Succeeded` → `(false, "", result.Message, false)`; blank text → `(false, "", "No text found in the selected region.", false)`; `SensitiveTextDetector.Scan(result.Text)` findings > 0 → `(true, scan.RedactedText, $"{scan.Summary} Redacted text copied to clipboard.", true)`; else `(true, result.Text, $"Copied {result.Words.Count} word(s) of recognized text.", false)`.

- [ ] **Step 1: Failing tests** — `Compose_CopiesRawTextWhenNothingSensitiveFound`, `Compose_SubstitutesRedactedTextWhenSensitiveValuesDetected` (input containing `test@example.com`, assert `Redacted` true and `[REDACTED:` in `ClipboardText`), `Compose_ReportsFailureWithoutText`, `Compose_ReportsEmptyRecognitionAsNoText`.
- [ ] **Step 2: Verify RED** — filter `TextGrabPresenterTests` → CS0246.
- [ ] **Step 3: Implement the presenter**, then rework `OcrRegionCommand` (drop the `hotkeyProfile` parameter; update `Hotkeys_ActionTriggered:284` to call `OcrRegionCommand()`):

```csharp
    /// <summary>
    /// Clipboard-only text grab: nothing is added to the library, so the temp frame written for
    /// the file-based OCR engine is deleted no matter how recognition ends. Failures surface on
    /// the status line and balloon only — this fires while other apps own the foreground, so a
    /// modal error box would steal focus from whatever the user is reading.
    /// </summary>
    private async void OcrRegionCommand()
    {
        var wasVisible = IsVisible;
        if (wasVisible)
        {
            Hide();
        }

        string? tempPath = null;
        try
        {
            using var captured = await _services.Screenshots.CaptureRegionAsync(this);
            if (captured is null)
            {
                SetStatus("Text grab canceled.");
                return;
            }

            tempPath = Path.Combine(_services.Paths.TempRoot, $"text-grab-{Guid.NewGuid():N}.png");
            captured.Bitmap.Save(tempPath, System.Drawing.Imaging.ImageFormat.Png);
            var payload = TextGrabPresenter.Compose(await _services.Ocr.RecognizeFileAsync(tempPath));
            if (payload.HasText)
            {
                ClipboardInterop.SetText(payload.ClipboardText);
            }

            SetStatus(payload.StatusMessage);
            if (!wasVisible)
            {
                _services.Tray?.ShowCaptureNotification(payload.StatusMessage);
            }
        }
        finally
        {
            if (tempPath is not null)
            {
                try { File.Delete(tempPath); } catch (IOException) { }
            }

            if (wasVisible)
            {
                ShowWorkspaceCommand();
            }
        }
    }
```

Match the hide/restore details to the existing `CaptureRegionAsync` dance at `:628-648` (including any settle delay it takes before capturing). Update `KeybindCatalog.cs:70-71` description to `"Selects a region and copies the recognized text without saving a capture."`.

- [ ] **Step 4: Verify GREEN + build** — presenter filter PASS; full build clean.
- [ ] **Step 5: Commit**

```bash
git add src/GoatShot.App/Services/TextGrabPresenter.cs src/GoatShot.App/MainWindow.xaml.cs src/GoatShot.App/Services/KeybindCatalog.cs src/GoatShot.Tests/TextGrabPresenterTests.cs
git commit -m "feat(ocr): make the OCR-region hotkey a clipboard-only text grab"
```

---

## Task 6: Tray entry for the text grab

**Files:**
- Modify: `src/GoatShot.App/Services/TrayMenuActionCatalog.cs`, `src/GoatShot.App/Services/TrayService.cs` (`Dispatch`), `src/GoatShot.App/MainWindow.xaml.cs` (public command)
- Test: `src/GoatShot.Tests/TrayMenuActionCatalogTests.cs`

- [ ] **Step 1: RED via the catalog tests** — add `TrayMenuActionKind.OcrTextGrab` to the enum, bump the hard-coded counts at `TrayMenuActionCatalogTests.cs:12-14` from 24/20/4 to 25/21/4, and add `Assert.AreEqual(HotkeyAction.OcrRegion, TrayMenuActionCatalog.HotkeyFor(TrayMenuActionKind.OcrTextGrab));`. Run the filter — `Actions_CoverEveryActionKindOnce` fails until the catalog entry exists.
- [ ] **Step 2: GREEN** — `All` gains `TrayMenuActionDefinition.Action("Grab text from a region", TrayMenuActionKind.OcrTextGrab, "Tools")` inside the existing Tools block (after "Pixel ruler"); `HotkeyByAction[TrayMenuActionKind.OcrTextGrab] = HotkeyAction.OcrRegion;`; `TrayService.Dispatch` gains `case TrayMenuActionKind.OcrTextGrab: window.OcrTextGrabCommand(); break;`; MainWindow gains `public void OcrTextGrabCommand() => OcrRegionCommand();` beside the other public commands.
- [ ] **Step 3: Verify** — catalog filter PASS; build clean.
- [ ] **Step 4: Commit**

```bash
git add src/GoatShot.App/Services/TrayMenuActionCatalog.cs src/GoatShot.App/Services/TrayService.cs src/GoatShot.App/MainWindow.xaml.cs src/GoatShot.Tests/TrayMenuActionCatalogTests.cs
git commit -m "feat(tray): add a text-grab entry labelled with the OCR hotkey"
```

---

## Task 7: Structured `SourceUrl`

**Files:**
- Modify: `src/GoatShot.App/Models/CaptureSource.cs`, `src/GoatShot.App/Models/CaptureItem.cs` (after `SourceWindowTitle`), `src/GoatShot.App/Services/WorkspaceStore.cs` (`BuildItem:308-310` block), `src/GoatShot.App/Services/BrowserExtensionNativeBridgeService.cs` (`ImportScreenshotAsync:233-238` AND `ImportStitchPackageAsync:257-262`), `src/GoatShot.App/MainWindow.xaml.cs:2734` (details text)
- Test: `src/GoatShot.Tests/BrowserExtensionNativeBridgeServiceTests.cs`

- [ ] **Step 1: Failing tests** — extend the existing bridge harness: `ImportScreenshot_StampsStructuredSourceUrl` and `ImportStitchPackage_StampsStructuredSourceUrl` (payload `Page.Url = "https://example.test/checkout"`; assert `result.Item.SourceUrl` equals it). Add a `WorkspaceStore` round-trip assertion in `WorkspaceStoreBatchUpdateTests` (save item with `SourceUrl`, reload, still there).
- [ ] **Step 2: Verify RED** → CS1061 on `SourceUrl`.
- [ ] **Step 3: Implement** — `public string? SourceUrl { get; set; }` on both models; `SourceUrl = source?.SourceUrl,` in `BuildItem`; both bridge import paths add `SourceUrl = payload.Page.Url` to their `CaptureSource` initializers (keep the Notes URL line for back-compat); details block adds a `Source URL` line beside `Source window`.
- [ ] **Step 4: Verify GREEN** — bridge + batch filters PASS; full build clean.
- [ ] **Step 5: Commit**

```bash
git add src/GoatShot.App/Models/CaptureSource.cs src/GoatShot.App/Models/CaptureItem.cs src/GoatShot.App/Services/WorkspaceStore.cs src/GoatShot.App/Services/BrowserExtensionNativeBridgeService.cs src/GoatShot.App/MainWindow.xaml.cs src/GoatShot.Tests/BrowserExtensionNativeBridgeServiceTests.cs src/GoatShot.Tests/WorkspaceStoreBatchUpdateTests.cs
git commit -m "feat(capture): stamp a structured SourceUrl from browser-extension imports"
```

---

## Task 8: Index title + URL in SQLite/FTS (sentinel swap)

**Files:**
- Modify: `src/GoatShot.App/Services/WorkspaceMetadataIndex.cs`
- Test: `src/GoatShot.Tests/WorkspaceMetadataIndexMigrationTests.cs` (new, `[DoNotParallelize]`, reusing the temp-paths + legacy-database helpers from `WorkspaceMetadataIndexReceiptTests`)

- [ ] **Step 1: Failing tests** — `EnsureFtsSchema_RecreatesFtsWhenSourceUrlColumnMissing` (build the OLD `captures_fts` shape with a row; construct index; `EnsureCreated` via any public call; PRAGMA shows `source_url`; old row gone); `Rebuild_RepopulatesSearchAfterFtsMigration` (legacy DB + `Rebuild([itemWithUrlAndTitle])` → `Search("invoices")` and `Search("checkout")` both hit — this simulates the exact startup path); `Upsert_RoundTripsSourceWindowTitleAndUrlColumns` (raw SELECT).
- [ ] **Step 2: Verify RED** — the PRAGMA/search assertions fail against the current schema.
- [ ] **Step 3: Implement** — add `source_window_title TEXT, source_url TEXT` to the CREATE TABLE; two `EnsureColumn` calls; add both columns to the FTS creation SQL in BOTH `EnsureCreated` and `EnsureFtsSchema`; **swap the sentinel check from `hotkey_profile` to `source_url`**; bind `$source_window_title`/`$source_url` in `UpsertCore`'s captures upsert, FTS insert, and the shared parameter helper.
- [ ] **Step 4: Verify GREEN** — migration filter PASS; also `dotnet test GoatShot.slnx --filter "FullyQualifiedName~WorkspaceMetadataIndex"` all green.
- [ ] **Step 5: Commit**

```bash
git add src/GoatShot.App/Services/WorkspaceMetadataIndex.cs src/GoatShot.Tests/WorkspaceMetadataIndexMigrationTests.cs
git commit -m "feat(index): index source window title and URL in SQLite metadata and FTS"
```

---

## Task 9: Stamp the actually-clicked window

**Files:**
- Modify: `src/GoatShot.App/Windows/RegionCaptureWindow.xaml.cs` (add `SelectedTarget`, set at hover-click `:249-253` and chooser-capture sites)
- Modify: `src/GoatShot.App/Services/ScreenshotService.cs:22-66`
- Test: `src/GoatShot.Tests/ScreenshotSourceStampTests.cs` (new)

**Interfaces:**
- `RegionCaptureWindow.SelectedTarget` (`CaptureOverlayTarget?`, null for drag/Enter captures).
- `internal static bool ScreenshotService.ShouldStampFromTarget(CaptureOverlayTarget? target)` — true only for `Window`/`ContentArea`/`ControlArea` kinds with `NativeHandle != 0`.

- [ ] **Step 1: Failing tests** — `ShouldStampFromTarget_TrueForClickedWindowWithHandle`; `ShouldStampFromTarget_FalseForMonitorNullOrHandleless`.
- [ ] **Step 2: Verify RED** → CS0117.
- [ ] **Step 3: Implement** — set `SelectedTarget = hovered;` in the hover-click branch and `SelectedTarget = target;` in `CaptureTarget_Click`; `SelectRegionBounds` returns the target alongside bounds; `CaptureRegionAsync` keeps the pre-overlay `GetForegroundSourceContext()` fallback but replaces the source with `GetSourceContext(new IntPtr(target.NativeHandle))` when `ShouldStampFromTarget(target)`.
- [ ] **Step 4: Verify GREEN + build.** Win32 re-read is covered by the end-of-plan smoke.
- [ ] **Step 5: Commit**

```bash
git add src/GoatShot.App/Windows/RegionCaptureWindow.xaml.cs src/GoatShot.App/Services/ScreenshotService.cs src/GoatShot.Tests/ScreenshotSourceStampTests.cs
git commit -m "feat(capture): stamp the click-captured window as the capture source"
```

---

## Task 10: Pure comparison model

**Files:**
- Create: `src/GoatShot.App/Services/CaptureComparisonService.cs` (also contains `PixelGridDiff`)
- Modify: `src/GoatShot.App/Services/ReceiptSceneAnalysisService.cs` (one internal helper next to `NormalizeText`/`Tokenize`)
- Test: create `src/GoatShot.Tests/CaptureComparisonServiceTests.cs`

**Interfaces:**

```csharp
// ReceiptSceneAnalysisService:
internal static IReadOnlySet<string> TokenizeForComparison(string? text) =>
    Tokenize(NormalizeText(text ?? string.Empty));

public enum CaptureComparisonVerdict { Identical, BelowThreshold, Addition, Deletion, Edit, MissingOcr }

public sealed record PixelGridDiffResult(bool DimensionsMatch, int CellsChanged, int CellsTotal, double? DifferencePercent);

public static class PixelGridDiff
{
    public static PixelGridDiffResult Compute(
        byte[] bgraBefore, int beforeWidth, int beforeHeight,
        byte[] bgraAfter, int afterWidth, int afterHeight,
        int gridCells = 16, int channelThreshold = 12);
}

public sealed record CaptureComparisonResult(
    CaptureComparisonVerdict Verdict, string Explanation, double? Similarity,
    IReadOnlySet<string> AddedTokens, IReadOnlySet<string> DeletedTokens,
    IReadOnlyList<OcrRecognizedWord> BeforeHighlights,
    IReadOnlyList<OcrRecognizedWord> AfterHighlights,
    PixelGridDiffResult? PixelDiff);

public static class CaptureComparisonService
{
    public static CaptureComparisonResult Compare(
        string? beforeText, IReadOnlyList<OcrRecognizedWord> beforeWords,
        string? afterText, IReadOnlyList<OcrRecognizedWord> afterWords,
        PixelGridDiffResult? pixelDiff);
}
```

Behavior: both texts blank → `MissingOcr`. Token sets via `TokenizeForComparison`; `Added = after \ before`, `Deleted = before \ after`. Verdict from `CompareTexts(before, after)`: non-null maps kind→verdict with its `Explanation`/`Similarity`; null → `Identical` when both token deltas are empty, else `BelowThreshold` ("Differences are below the noise threshold." explanation). Highlights: a word highlights when its normalized text (`TokenizeForComparison(word.Text)` singleton) is in the relevant token set — deleted→before side, added→after side; repeated tokens highlight every instance (documented v1 tradeoff). `PixelGridDiff`: 16×16 cell grid over each buffer; per cell, mean absolute channel delta sampled every 4th pixel; cell changed when mean > threshold; `DifferencePercent = CellsChanged / CellsTotal * 100`; size mismatch → `(false, 0, 0, null)`.

- [ ] **Step 1: Failing tests** — `Compare_ClassifiesEditWithTokenSetsAndBoxes` (before "invoice total 100" / after "invoice total 250" with hand-built words; assert verdict Edit, Added {"250"}, Deleted {"100"}, exact highlight boxes); `Compare_MapsRepeatedTokensToEveryInstance`; `Compare_ReturnsIdenticalForWhitespaceAndCaseDifferences`; `Compare_ReturnsBelowThresholdForTinyNoise` (long common text + one changed token); `Compare_ReturnsMissingOcrWhenEitherSideHasNoText`; `PixelGridDiff_ReportsZeroForIdenticalBuffers`; `PixelGridDiff_CountsChangedCells` (64×64, one quadrant altered → ~25%); `PixelGridDiff_SkipsWhenDimensionsDiffer`.
- [ ] **Step 2: Verify RED** → CS0246.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Verify GREEN**; also run `FullyQualifiedName~ReceiptSceneAnalysis` to prove the helper didn't disturb the comparer.
- [ ] **Step 5: Commit**

```bash
git add src/GoatShot.App/Services/CaptureComparisonService.cs src/GoatShot.App/Services/ReceiptSceneAnalysisService.cs src/GoatShot.Tests/CaptureComparisonServiceTests.cs
git commit -m "feat(compare): pure capture comparison model with token highlights and pixel grid diff"
```

---

## Task 11: Compare window + selection wiring

**Files:**
- Create: `src/GoatShot.App/Windows/CompareWindow.xaml`, `CompareWindow.xaml.cs`
- Modify: `src/GoatShot.App/MainWindow.xaml:678-706` (SelectionActionBar 7th column), `src/GoatShot.App/MainWindow.xaml.cs` (`CaptureList_SelectionChanged`, `SetSelectionActionsEnabled`, `BuildCommandPaletteEntries`, new `CompareSelected_Click`)

Window: title "Compare captures"; header row = verdict label (accent, semibold) + explanation + `Similarity: NN%` when present + pixel readout (`Pixel difference: N% of cells` / `Pixel comparison skipped (different dimensions).`); body = two columns, each `TextBlock` caption (older = "Before", newer = "After", filename + CreatedAt) above `Viewbox > Grid(Width/Height = pixel dims) > [Image Stretch=Fill, Canvas]`. Highlights cloned from the EditorWindow amber-overlay pattern: dashed 3px stroke, translucent fill, `IsHitTestVisible=False`, `AutomationProperties.Name = $"Changed text: {word.Text}"`; deleted words on the before canvas in the danger tone (`#FF6B81`-family per App.xaml resources), added words on the after canvas in the accent tone.

MainWindow: `CompareButton` (Content "Compare", `AutomationProperties.Name="Compare the two selected captures"`) in a new column after Extract text; in `CaptureList_SelectionChanged` compute `var selectedImages = CaptureList.SelectedItems.OfType<CaptureItem>().Where(item => IsImageFile(item.FilePath)).ToList();` → `CompareButton.IsEnabled = selectedImages.Count == 2;`; force-disable in the `SetSelectionActionsEnabled(false, ...)` path. `CompareSelected_Click`: exactly-2 guard with status message; OCR any side lacking words via `await RecognizeAndStoreOcrAsync(item, copyText: false)`; order by `CreatedAt`; load both images off-thread and extract BGRA via a private LockBits reader (`Bitmap` → `LockBits(ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb)` → `Marshal.Copy`); `PixelGridDiff.Compute`; `CaptureComparisonService.Compare`; `new CompareWindow(...) { Owner = this }.Show()`. Palette entry: `E("Compare selected captures", "Workspace", () => CompareSelected_Click(this, new RoutedEventArgs()))`.

- [ ] **Step 1: Implement window + wiring** (no unit test — WPF wiring; the model was Task 10).
- [ ] **Step 2: Build + full suite green.**
- [ ] **Step 3: Commit**

```bash
git add src/GoatShot.App/Windows/CompareWindow.xaml src/GoatShot.App/Windows/CompareWindow.xaml.cs src/GoatShot.App/MainWindow.xaml src/GoatShot.App/MainWindow.xaml.cs
git commit -m "feat(compare): side-by-side compare window for two selected captures"
```

---

## Task 12: Live Text on the library preview

**Files:**
- Create: `src/GoatShot.App/Services/OcrWordSelectionService.cs`
- Modify: `src/GoatShot.App/MainWindow.xaml:610-615`, `src/GoatShot.App/MainWindow.xaml.cs` (`UpdateCaptureDetails`, selection-cleared branch, new mouse handlers)
- Test: create `src/GoatShot.Tests/OcrWordSelectionServiceTests.cs`

**Interfaces:**

```csharp
public static class OcrWordSelectionService
{
    public sealed record OcrWordSelection(IReadOnlyList<OcrRecognizedWord> Words, string Text);

    /// <summary>
    /// Words whose boxes overlap the rectangle with strictly positive area, in reading order
    /// (LineIndex, then StartIndex). Same-line words join with a space, lines with a newline.
    /// </summary>
    public static OcrWordSelection Resolve(
        IReadOnlyList<OcrRecognizedWord> words, double x, double y, double width, double height);
}
```

- [ ] **Step 1: Failing tests** — `Resolve_ReturnsIntersectingWordsInReadingOrder` (shuffled two-line input, rect over a subset → exact text, newline between lines); `Resolve_JoinsSameLineWordsWithSpaces`; `Resolve_ReturnsEmptyForNoIntersection`; `Resolve_IncludesPartiallyOverlappedWords`.
- [ ] **Step 2: Verify RED** → CS0246.
- [ ] **Step 3: Implement the service.**
- [ ] **Step 4: Restructure the preview XAML** (`MainWindow.xaml:610-615` only; EmptyState/PreviewHint untouched):

```xml
                    <Viewbox Stretch="Uniform" StretchDirection="Both">
                        <Grid x:Name="PreviewSurface">
                            <Image x:Name="PreviewImage"
                                   Stretch="Fill"
                                   RenderOptions.BitmapScalingMode="HighQuality"
                                   AutomationProperties.Name="Selected capture preview" />
                            <Canvas x:Name="PreviewTextOverlay"
                                    Background="Transparent"
                                    AutomationProperties.Name="Live text selection overlay"
                                    MouseLeftButtonDown="PreviewTextOverlay_MouseLeftButtonDown"
                                    MouseMove="PreviewTextOverlay_MouseMove"
                                    MouseLeftButtonUp="PreviewTextOverlay_MouseLeftButtonUp" />
                        </Grid>
                    </Viewbox>
```

- [ ] **Step 5: Code-behind** — `UpdateCaptureDetails` sizes `PreviewSurface.Width/Height` from the loaded `BitmapSource.PixelWidth/PixelHeight` and records `_previewLiveTextEnabled = displayed dims == item.Width/Height && item.OcrWords.Count > 0` (thumbnail-fallback guard); `PreviewTextOverlay.Cursor = _previewLiveTextEnabled ? Cursors.IBeam : Cursors.Arrow;`. Drag handlers: down → capture mouse, record start, clear overlay; move → one translucent selection `Rectangle`; up → `OcrWordSelectionService.Resolve(item.OcrWords, rect)`; non-empty → `ClipboardInterop.SetText(selection.Text)` (wrapped in the ExternalException-tolerant pattern), flash amber word boxes for ~600 ms via `DispatcherTimer`, `SetStatus($"Copied {n} word(s) from the preview.")`. If the item is indexable but has no words yet, run `RecognizeAndStoreOcrAsync(item, copyText: false)` once behind an in-flight `HashSet<string>` guard with status "Recognizing text…". Selection-cleared branch: `PreviewSurface.Width = PreviewSurface.Height = double.NaN;` and clear the canvas. Stroke thickness `Math.Max(1, item.Width / 800d)` so boxes read on 4K captures.
- [ ] **Step 6: Verify** — service filter PASS; build; full suite; run the main-window render proof (`GoatShot.exe --render-main-window <out.png>` per the harness convention) to confirm the restructured tree renders.
- [ ] **Step 7: Commit**

```bash
git add src/GoatShot.App/Services/OcrWordSelectionService.cs src/GoatShot.App/MainWindow.xaml src/GoatShot.App/MainWindow.xaml.cs src/GoatShot.Tests/OcrWordSelectionServiceTests.cs
git commit -m "feat(live-text): drag-select OCR words directly on the library preview"
```

---

## Task 13: Documentation

**Files:** `README.md`, this plan's Status section.

- [ ] **Step 1:** Capture/library sections gain: background OCR indexing + toggle + "search finds text inside captures"; **explicit call-out that Ctrl+Shift+O no longer saves a capture** (and that automation rules keyed to the OcrRegion hotkey profile no longer see a capture); Compare (select two → verdict + highlighted changes); Live Text (drag over the preview to copy text); richer source stamping (clicked window, page URL for extension captures, both searchable).
- [ ] **Step 2:** Full suite one last time; mark Status here.
- [ ] **Step 3: Commit**

```bash
git add README.md docs/superpowers/plans/2026-08-16-ocr-compare-livetext.md
git commit -m "docs: describe OCR indexing, text grab, compare, and live text"
```

---

## Known risks / accepted tradeoffs

- Worker/UI write race: fresh `Load()` per pass + `OcrRecognizedAt` skip makes passes idempotent; a manual edit landing inside one in-flight chunk (seconds) can be clobbered — accepted v1.
- Backfill never fires `OcrCompleted` automation (deliberate, `ShouldRaiseOcrCompleted`); automation's own `RunOcr` still doesn't fire it either (pre-existing, out of scope).
- Compare highlight-by-token maps repeated tokens to every instance on a side.
- FTS upgrade drops and recreates `captures_fts`; the startup `Rebuild` repopulates it in the same pass. External index consumers that skip `Rebuild` would search empty until the next app start.

## Status

Not started.

## Manual smoke checklist (after Task 13, new binary — exit the tray app first)

1. Fresh capture → details pane shows `OCR words: N` within ~20 s, untouched.
2. Search finds a word visible only inside an old, backfilled screenshot.
3. Ctrl+Shift+O copies text; no new library item appears. Tray → "Grab text from a region" does the same.
4. Select 2 captures → Compare enables; window shows verdict + aligned highlight boxes; select 1 or 3 → disabled.
5. Drag over the preview copies text and flashes the boxes; Ctrl+V pastes it.
6. Browser-extension capture shows its URL in details and is findable by URL words.
7. Hover-click capture of a background window stamps that window's title, not the pre-overlay foreground's.
8. Settings → toggle OCR indexing off + Save → status line reports the worker stopped.
