using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using GoatShot.App.Models;
using GoatShot.App.Windows;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingBrushes = System.Drawing.Brushes;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingGraphics = System.Drawing.Graphics;
using DrawingGraphicsUnit = System.Drawing.GraphicsUnit;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using DrawingPen = System.Drawing.Pen;
using DrawingSolidBrush = System.Drawing.SolidBrush;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfControl = System.Windows.Controls.Control;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using WpfTextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace GoatShot.App.Services;

public static class WpfSurfaceAccessibilityAuditor
{
    public static async Task AuditAsync(
        AppServices services,
        string? surfaceKey,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("A WPF surface audit output path is required.", nameof(outputPath));
        }

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var surface = NormalizeSurface(surfaceKey);
        Window? window = null;
        string? temporaryEditorImage = null;
        FrameExplorerRenderFixture? frameExplorerFixture = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (surface == "frame-explorer")
            {
                frameExplorerFixture = await FrameExplorerWindowRenderer.CreateFixtureAsync(cancellationToken);
                window = new FrameExplorerWindow(
                    services,
                    frameExplorerFixture.Item,
                    autoLoad: false);
            }
            else
            {
                (window, temporaryEditorImage) = CreateWindow(services, surface, fullPath);
            }

            if (surface == "recording" && window is MainWindow recordingWindow)
            {
                recordingWindow.PrepareRecordingAuditSurface();
            }

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -10000;
            window.Top = -10000;
            window.Width = surface switch
            {
                "editor" => 1200,
                "frame-explorer" => 1320,
                _ => 1180
            };
            window.Height = surface is "settings" or "frame-explorer" ? 820 : 760;
            window.ShowInTaskbar = false;
            window.Show();

            if (window is EditorWindow editor)
            {
                editor.SelectTool(AnnotationMode.Redact);
            }

            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            if (window is FrameExplorerWindow frameExplorer && frameExplorerFixture is not null)
            {
                await frameExplorer.PrepareRenderProofAsync(frameExplorerFixture.PreviewFramePath);
            }

            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();

            var root = SelectAuditRoot(window, surface);
            var elements = Descendants(root)
                .Where(IsKeyboardRelevant)
                .ToList();
            var controls = elements
                .Select((element, index) => InspectControl(element, index + 1))
                .ToList();

            await File.WriteAllTextAsync(
                fullPath,
                BuildReport(surface, root, controls),
                cancellationToken);
        }
        finally
        {
            window?.Close();
            if (!string.IsNullOrWhiteSpace(temporaryEditorImage))
            {
                TryDelete(temporaryEditorImage);
            }

            frameExplorerFixture?.Dispose();
        }
    }

    private static (Window Window, string? TemporaryEditorImage) CreateWindow(
        AppServices services,
        string surface,
        string outputPath)
    {
        return surface switch
        {
            "main" or "recording" => (new MainWindow(services, auditMode: true), null),
            "settings" => (new SettingsWindow(services), null),
            "editor" => CreateEditorWindow(services, outputPath),
            "proof-scene" => (new ProofSceneWindow(), null),
            _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, "Unsupported WPF audit surface.")
        };
    }

    private static (Window Window, string TemporaryImage) CreateEditorWindow(AppServices services, string outputPath)
    {
        var imagePath = Path.Combine(
            Path.GetDirectoryName(outputPath) ?? Path.GetTempPath(),
            "wpf-audit-editor-source.png");
        CreateSyntheticEditorImage(imagePath);
        var source = ImageInterop.LoadBitmapImage(imagePath);
        var item = new CaptureItem
        {
            Kind = CaptureKind.Imported,
            FilePath = imagePath,
            Width = source.PixelWidth,
            Height = source.PixelHeight,
            Bytes = new FileInfo(imagePath).Length,
            ThumbnailPath = string.Empty
        };

        return (new EditorWindow(item, services), imagePath);
    }

    private static FrameworkElement SelectAuditRoot(Window window, string surface)
    {
        if (surface != "recording")
        {
            return window;
        }

        return Descendants(window)
            .FirstOrDefault(element => element.Name.Equals("RecordingControlsAuditSection", StringComparison.Ordinal))
            ?? window;
    }

    private static IEnumerable<FrameworkElement> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element)
            {
                yield return element;
            }

            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static bool IsKeyboardRelevant(FrameworkElement element)
    {
        if (!element.IsVisible || element is Separator or ScrollViewer or WpfScrollBar or Thumb)
        {
            return false;
        }

        if (!AutomationProperties.GetLiveSetting(element).Equals(AutomationLiveSetting.Off))
        {
            return true;
        }

        return element.Focusable ||
            element is WpfControl { IsTabStop: true } ||
            element is WpfTextBoxBase ||
            element is PasswordBox ||
            element is WpfComboBox ||
            element is WpfListBox;
    }

    private static WpfAccessibilityItem InspectControl(FrameworkElement element, int index)
    {
        var peerName = UIElementAutomationPeer.CreatePeerForElement(element)?.GetName() ?? string.Empty;
        var automationName = AutomationProperties.GetName(element);
        var name = FirstNonBlank(automationName, peerName, ContentName(element));
        var control = element as WpfControl;
        var focusAccepted = element.IsEnabled && element.Focusable && TryAcceptFocus(element);

        return new WpfAccessibilityItem(
            index,
            element.Name,
            element.GetType().Name,
            name,
            element.IsEnabled,
            element.Focusable,
            control?.IsTabStop,
            control?.TabIndex,
            focusAccepted,
            AutomationProperties.GetLiveSetting(element).ToString());
    }

    private static bool TryAcceptFocus(FrameworkElement element)
    {
        if (element is TabItem tabItem)
        {
            tabItem.IsSelected = true;
        }

        return element.Focus();
    }

    private static string BuildReport(
        string surface,
        FrameworkElement root,
        IReadOnlyList<WpfAccessibilityItem> controls)
    {
        var enabled = controls.Where(control => control.Enabled).ToList();
        var keyboardTargets = enabled
            .Where(control => control.Focusable || control.TabStop == true || !control.LiveSetting.Equals("Off", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var missingNames = keyboardTargets.Where(control => string.IsNullOrWhiteSpace(control.AccessibleName)).ToList();
        var focusRejected = enabled
            .Where(control => control.Focusable && control.TabStop != false && !control.FocusAccepted)
            .ToList();
        var liveRegions = controls.Where(control => !control.LiveSetting.Equals("Off", StringComparison.OrdinalIgnoreCase)).ToList();

        var builder = new StringBuilder();
        builder.AppendLine($"# WPF Accessibility / Focus Audit - {SurfaceLabel(surface)}");
        builder.AppendLine();
        builder.AppendLine($"Date: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine();
        builder.AppendLine($"Scope: app-owned WPF visual-tree audit for {SurfaceLabel(surface)}. Root: {root.GetType().Name}{(string.IsNullOrWhiteSpace(root.Name) ? string.Empty : $" `{root.Name}`")}.");
        builder.AppendLine("Evidence limit: this is keyboard-focus and automation-name evidence from the running WPF app. It is not a full Tab-key traversal recording, narrated screen-reader session, text-scaling proof, WCAG certification, or live mouse interaction proof.");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine($"- Focusable or keyboard-relevant controls reviewed: {controls.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Enabled controls reviewed: {enabled.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Enabled keyboard/live controls missing accessible names: {missingNames.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Enabled focusable controls that rejected programmatic focus: {focusRejected.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Live status regions observed: {liveRegions.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine();

        builder.AppendLine(missingNames.Count == 0
            ? "- Result: no enabled keyboard-relevant controls in this surface were missing an accessible name in this app-owned audit."
            : "- Result: accessible-name gaps remain for " + string.Join(", ", missingNames.Select(ControlLabel)) + ".");
        builder.AppendLine(focusRejected.Count == 0
            ? "- Result: every enabled focusable tab-stop control accepted programmatic focus."
            : "- Result: focus acceptance needs follow-up for " + string.Join(", ", focusRejected.Select(ControlLabel)) + ".");

        builder.AppendLine();
        builder.AppendLine("## Control Evidence");
        builder.AppendLine();
        builder.AppendLine("| # | Element | Type | Accessible name | Enabled | Focusable | Tab stop | Tab index | Focus accepted | Live setting |");
        builder.AppendLine("|---:|---|---|---|---|---|---|---:|---|---|");
        foreach (var control in controls)
        {
            builder.AppendLine(
                $"| {control.Index.ToString(CultureInfo.InvariantCulture)} | {Escape(ControlLabel(control))} | {Escape(control.ControlType)} | {Escape(control.AccessibleName)} | {Bool(control.Enabled)} | {Bool(control.Focusable)} | {Bool(control.TabStop)} | {FormatTabIndex(control.TabIndex)} | {Bool(control.FocusAccepted)} | {Escape(control.LiveSetting)} |");
        }

        return builder.ToString();
    }

    private static string NormalizeSurface(string? surfaceKey)
    {
        var key = (surfaceKey ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "" => "main",
            "main" or "main-window" or "workspace" => "main",
            "recording" or "recording-controls" => "recording",
            "editor" or "editor-window" => "editor",
            "settings" or "settings-window" => "settings",
            "frame-explorer" or "frameexplorer" or "replay" or "replay-receipt" => "frame-explorer",
            "proof" or "proof-scene" or "safe-proof" or "safe-proof-scene" => "proof-scene",
            _ => key
        };
    }

    private static string SurfaceLabel(string surface)
    {
        return surface switch
        {
            "main" => "Main Window",
            "recording" => "Recording Controls",
            "editor" => "Editor",
            "settings" => "Settings",
            "frame-explorer" => "Frame Explorer",
            "proof-scene" => "Safe Proof Scene",
            _ => surface
        };
    }

    private static string ContentName(FrameworkElement element)
    {
        return element switch
        {
            ContentControl { Content: string text } => text,
            HeaderedContentControl { Header: string text } => text,
            TextBlock { Text: string text } => text,
            _ => string.Empty
        };
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static string ControlLabel(WpfAccessibilityItem control)
    {
        return string.IsNullOrWhiteSpace(control.ElementName) ? "(unnamed)" : control.ElementName;
    }

    private static string Bool(bool? value)
    {
        return value switch
        {
            true => "yes",
            false => "no",
            _ => string.Empty
        };
    }

    private static string FormatTabIndex(int? value)
    {
        return value switch
        {
            null => string.Empty,
            int.MaxValue => "default",
            _ => value.Value.ToString(CultureInfo.InvariantCulture)
        };
    }

    private static string Escape(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
    }

    private static void CreateSyntheticEditorImage(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var bitmap = new DrawingBitmap(960, 540);
        using var graphics = DrawingGraphics.FromImage(bitmap);
        graphics.Clear(DrawingColor.FromArgb(16, 24, 32));

        using var titleFont = new DrawingFont("Segoe UI", 28, DrawingFontStyle.Bold, DrawingGraphicsUnit.Pixel);
        using var bodyFont = new DrawingFont("Segoe UI", 16, DrawingFontStyle.Regular, DrawingGraphicsUnit.Pixel);
        using var accentBrush = new DrawingSolidBrush(DrawingColor.FromArgb(48, 230, 195));
        using var mutedBrush = new DrawingSolidBrush(DrawingColor.FromArgb(168, 188, 202));
        using var panelBrush = new DrawingSolidBrush(DrawingColor.FromArgb(28, 42, 54));
        using var linePen = new DrawingPen(DrawingColor.FromArgb(61, 91, 110), 2);

        graphics.DrawString("Synthetic editor audit image", titleFont, DrawingBrushes.White, 52, 46);
        graphics.DrawString("No user capture pixels are used for this focus/name proof.", bodyFont, mutedBrush, 54, 88);
        graphics.FillRectangle(panelBrush, 54, 140, 380, 280);
        graphics.FillRectangle(panelBrush, 470, 140, 420, 120);
        graphics.FillRectangle(accentBrush, 82, 174, 120, 12);
        for (var index = 0; index < 6; index++)
        {
            var y = 220 + index * 30;
            graphics.DrawLine(linePen, 82, y, 390, y);
            graphics.DrawLine(linePen, 500, 178 + index * 14, 850, 178 + index * 14);
        }

        bitmap.Save(path, DrawingImageFormat.Png);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Audit helpers should not fail because a temporary source file was already gone or locked.
        }
    }

    private sealed record WpfAccessibilityItem(
        int Index,
        string ElementName,
        string ControlType,
        string AccessibleName,
        bool Enabled,
        bool Focusable,
        bool? TabStop,
        int? TabIndex,
        bool FocusAccepted,
        string LiveSetting);
}
