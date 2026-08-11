using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class StepRecorderService : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLButtonUp = 0x0202;
    private const int InitialIgnoreMs = 700;
    private const int CaptureDelayMs = 180;
    private const int MaxOcrCharsPerStep = 1_200;

    private readonly AppPaths _paths;
    private readonly ScreenshotService _screenshots;
    private readonly WorkspaceStore _workspaceStore;
    private readonly OcrService _ocr;
    private readonly object _gate = new();
    private readonly List<StepRecordingStep> _steps = new();
    private readonly string _ownProcessName = Process.GetCurrentProcess().ProcessName;
    private HookProc? _hookProc;
    private IntPtr _hookHandle;
    private DateTimeOffset _startedAt;
    private DateTimeOffset _ignoreClicksUntil;
    private Task _currentCapture = Task.CompletedTask;
    private int _captureInProgress;

    public StepRecorderService(
        AppPaths paths,
        ScreenshotService screenshots,
        WorkspaceStore workspaceStore,
        OcrService ocr)
    {
        _paths = paths;
        _screenshots = screenshots;
        _workspaceStore = workspaceStore;
        _ocr = ocr;
    }

    public event EventHandler<StepRecordingStep>? StepCaptured;
    public event EventHandler<string>? StatusChanged;

    public bool IsRecording { get; private set; }
    public IReadOnlyList<StepRecordingStep> Steps
    {
        get
        {
            lock (_gate)
            {
                return _steps.ToList();
            }
        }
    }

    public void Start()
    {
        if (IsRecording)
        {
            return;
        }

        lock (_gate)
        {
            _steps.Clear();
        }

        _startedAt = DateTimeOffset.Now;
        _ignoreClicksUntil = _startedAt.AddMilliseconds(InitialIgnoreMs);
        _hookProc = HookCallback;
        _hookHandle = SetWindowsHookEx(WhMouseLl, _hookProc, GetModuleHandle(null), 0);
        if (_hookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Step recorder mouse hook could not be installed. Win32 error: {Marshal.GetLastWin32Error()}");
        }

        IsRecording = true;
        StatusChanged?.Invoke(this, "Step recorder started. Click through the workflow to capture active-window steps.");
    }

    public async Task<StepRecordingExportResult> StopAndExportAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRecording)
        {
            return new StepRecordingExportResult
            {
                Succeeded = false,
                Message = "Step recorder is not running."
            };
        }

        StopHook();
        await WaitForCurrentCaptureAsync(cancellationToken);

        var steps = Steps;
        if (steps.Count == 0)
        {
            return new StepRecordingExportResult
            {
                Succeeded = false,
                Message = "Step recorder stopped. No steps were captured."
            };
        }

        var markdownPath = ResolveOutputPath("md");
        var htmlPath = ResolveOutputPath("html");
        Directory.CreateDirectory(_paths.DocumentsRoot);
        await File.WriteAllTextAsync(markdownPath, BuildMarkdown(steps), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(htmlPath, BuildHtml(steps), Encoding.UTF8, cancellationToken);

        return new StepRecordingExportResult
        {
            Succeeded = true,
            MarkdownPath = markdownPath,
            HtmlPath = htmlPath,
            StepCount = steps.Count,
            Message = $"Step recorder exported {steps.Count} step(s)."
        };
    }

    public void Dispose()
    {
        StopHook();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == WmLButtonUp && IsRecording)
        {
            var now = DateTimeOffset.Now;
            if (now >= _ignoreClicksUntil && Interlocked.CompareExchange(ref _captureInProgress, 1, 0) == 0)
            {
                var info = Marshal.PtrToStructure<MouseHookStruct>(lParam);
                _currentCapture = CaptureStepAsync(info.Point.X, info.Point.Y);
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private async Task CaptureStepAsync(int clickX, int clickY)
    {
        try
        {
            await Task.Delay(CaptureDelayMs);
            using var captured = await _screenshots.CaptureActiveWindowAsync();
            if (captured.Source?.ProcessName?.Equals(_ownProcessName, StringComparison.OrdinalIgnoreCase) == true)
            {
                StatusChanged?.Invoke(this, "Ignored Receipts UI click while step recorder is active.");
                return;
            }

            var item = await _workspaceStore.SaveCaptureAsync(captured, privateCapture: false);
            var step = new StepRecordingStep
            {
                Number = NextStepNumber(),
                CapturedAt = DateTimeOffset.Now,
                CaptureItemId = item.Id,
                FilePath = item.FilePath,
                SourceApp = captured.Source?.ProcessName,
                WindowTitle = captured.Source?.WindowTitle,
                ClickX = clickX,
                ClickY = clickY
            };

            await TryRecognizeOcrAsync(item, step);
            lock (_gate)
            {
                _steps.Add(step);
            }

            StepCaptured?.Invoke(this, step);
            StatusChanged?.Invoke(this, $"Captured step {step.Number}: {step.SourceLabel}");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"Step recorder capture failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _captureInProgress, 0);
        }
    }

    private async Task TryRecognizeOcrAsync(CaptureItem item, StepRecordingStep step)
    {
        try
        {
            var ocr = await _ocr.RecognizeFileAsync(item.FilePath);
            if (!ocr.Succeeded)
            {
                item.Notes = AppendStepRecorderNote(item.Notes, step, "OCR was unavailable for this step.");
                await _workspaceStore.UpdateItemAsync(item);
                return;
            }

            item.OcrText = ocr.Text;
            item.OcrLanguageTag = ocr.LanguageTag;
            item.OcrRecognizedAt = DateTimeOffset.Now;
            item.OcrWords = ocr.Words.ToList();
            item.Notes = AppendStepRecorderNote(item.Notes, step, $"OCR captured {ocr.LineCount} line(s).");
            step.OcrText = ocr.Text;
            await _workspaceStore.UpdateItemAsync(item);
        }
        catch (Exception ex)
        {
            item.Notes = AppendStepRecorderNote(item.Notes, step, $"OCR failed: {ex.Message}");
            await _workspaceStore.UpdateItemAsync(item);
        }
    }

    private int NextStepNumber()
    {
        lock (_gate)
        {
            return _steps.Count + 1;
        }
    }

    private async Task WaitForCurrentCaptureAsync(CancellationToken cancellationToken)
    {
        var capture = _currentCapture;
        if (capture.IsCompleted)
        {
            return;
        }

        var timeout = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        await Task.WhenAny(capture, timeout);
    }

    private void StopHook()
    {
        IsRecording = false;
        if (_hookHandle == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        _hookProc = null;
    }

    private string ResolveOutputPath(string extension)
    {
        var stamp = _startedAt == default
            ? DateTimeOffset.Now
            : _startedAt;
        return Path.Combine(_paths.DocumentsRoot, $"step-recording-{stamp:yyyyMMdd-HHmmss}.{extension}");
    }

    private static string AppendStepRecorderNote(string? notes, StepRecordingStep step, string ocrSummary)
    {
        var builder = new StringBuilder(notes ?? string.Empty);
        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.AppendLine($"Step recorder: step {step.Number}, click at {step.ClickX}, {step.ClickY}.");
        builder.AppendLine($"Source: {step.SourceLabel}.");
        builder.Append(ocrSummary);
        return builder.ToString();
    }

    private static string BuildMarkdown(IReadOnlyList<StepRecordingStep> steps)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Receipts Step Recording");
        builder.AppendLine();
        builder.AppendLine($"Generated: {DateTimeOffset.Now.LocalDateTime:g}");
        builder.AppendLine($"Steps: {steps.Count}");
        builder.AppendLine();

        foreach (var step in steps)
        {
            builder.AppendLine($"## Step {step.Number}");
            builder.AppendLine();
            builder.AppendLine($"- Captured: {step.CapturedAt.LocalDateTime:g}");
            builder.AppendLine($"- Source: {SensitiveTextDetector.Redact(step.SourceLabel)}");
            builder.AppendLine($"- Click: {step.ClickX}, {step.ClickY}");
            builder.AppendLine($"- Workspace item: {step.CaptureItemId}");
            builder.AppendLine();
            builder.AppendLine($"![Step {step.Number}]({MarkdownPath(step.FilePath)})");
            builder.AppendLine();
            AppendMarkdownOcr(builder, step.OcrText);
        }

        return builder.ToString();
    }

    private static void AppendMarkdownOcr(StringBuilder builder, string? ocrText)
    {
        var text = LimitedRedactedText(ocrText);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        builder.AppendLine("OCR context:");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(text);
        builder.AppendLine("```");
        builder.AppendLine();
    }

    private static string BuildHtml(IReadOnlyList<StepRecordingStep> steps)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        builder.AppendLine("<title>Receipts Step Recording</title>");
        builder.AppendLine("<style>");
        builder.AppendLine("body{font-family:Segoe UI,Arial,sans-serif;max-width:980px;margin:32px auto;padding:0 20px;line-height:1.5;color:#102027;background:#f8fbfc}");
        builder.AppendLine("img{max-width:100%;border:1px solid #c7d6dc;border-radius:6px} pre{white-space:pre-wrap;background:#e9f1f4;border-radius:6px;padding:12px}.meta{color:#31515d}");
        builder.AppendLine("</style></head><body>");
        builder.AppendLine("<h1>Receipts Step Recording</h1>");
        builder.AppendLine("<p class=\"meta\">Generated: " + Html(DateTimeOffset.Now.LocalDateTime.ToString("g")) + " · Steps: " + steps.Count + "</p>");
        foreach (var step in steps)
        {
            builder.AppendLine("<section>");
            builder.AppendLine("<h2>Step " + step.Number + "</h2>");
            builder.AppendLine("<p class=\"meta\">Captured: " + Html(step.CapturedAt.LocalDateTime.ToString("g")) + "<br>");
            builder.AppendLine("Source: " + Html(SensitiveTextDetector.Redact(step.SourceLabel)) + "<br>");
            builder.AppendLine("Click: " + step.ClickX + ", " + step.ClickY + "<br>");
            builder.AppendLine("Workspace item: " + Html(step.CaptureItemId) + "</p>");
            builder.AppendLine("<img src=\"" + Html(new Uri(step.FilePath).AbsoluteUri) + "\" alt=\"Step " + step.Number + "\">");
            var ocr = LimitedRedactedText(step.OcrText);
            if (!string.IsNullOrWhiteSpace(ocr))
            {
                builder.AppendLine("<h3>OCR context</h3><pre>" + Html(ocr) + "</pre>");
            }

            builder.AppendLine("</section>");
        }

        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    private static string LimitedRedactedText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var redacted = SensitiveTextDetector.Redact(text.Trim());
        return redacted.Length <= MaxOcrCharsPerStep
            ? redacted
            : redacted[..MaxOcrCharsPerStep] + Environment.NewLine + "[TRUNCATED]";
    }

    private static string MarkdownPath(string path)
    {
        return path.Replace("\\", "/", StringComparison.Ordinal);
    }

    private static string Html(string value)
    {
        return WebUtility.HtmlEncode(value);
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookStruct
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
