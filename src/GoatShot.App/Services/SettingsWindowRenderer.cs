using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GoatShot.App.Windows;

namespace GoatShot.App.Services;

public static class SettingsWindowRenderer
{
    public static async Task RenderAsync(
        AppServices services,
        string? sectionKey,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("A settings screenshot output path is required.", nameof(outputPath));
        }

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var window = new SettingsWindow(services)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            Width = 980,
            Height = 820,
            ShowInTaskbar = false
        };
        // This window is closed programmatically; a modal confirmation would hang the render.
        window.SuppressUnsavedChangesPrompt = true;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            window.Show();
            if (TryReadSearchQuery(sectionKey) is { } searchQuery)
            {
                window.PreviewSettingsSearch(searchQuery);
            }
            else if (sectionKey?.Trim().Equals("Dirty", StringComparison.OrdinalIgnoreCase) == true)
            {
                window.PreviewUnsavedChanges();
            }
            else if (IsAutomationImageEffectSection(sectionKey))
            {
                window.SelectAutomationImageEffectRuleFields();
            }
            else if (IsReplaySection(sectionKey))
            {
                window.SelectReplaySettings();
            }
            else if (IsAutomationAdvancedSection(sectionKey))
            {
                window.SelectAutomationAdvancedRuleFields();
            }
            else
            {
                window.SelectSection(sectionKey, alignSectionToTop: true);
            }

            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();

            var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
            var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using var stream = File.Create(fullPath);
            encoder.Save(stream);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Renders the search results for "Search:&lt;query&gt;" so the filter can be proofed.</summary>
    private static string? TryReadSearchQuery(string? sectionKey)
    {
        const string prefix = "Search:";
        var value = sectionKey?.Trim();
        return value is not null && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value[prefix.Length..].Trim()
            : null;
    }

    private static bool IsAutomationAdvancedSection(string? sectionKey)
    {
        return sectionKey?.Trim() is { } value &&
            (value.Equals("AutomationAdvanced", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("AutomationRules", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("WorkflowRules", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsReplaySection(string? sectionKey)
    {
        return sectionKey?.Trim() is { } value &&
            (value.Equals("Replay", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("ReplayRecording", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAutomationImageEffectSection(string? sectionKey)
    {
        return sectionKey?.Trim() is { } value &&
            (value.Equals("AutomationImageEffect", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("WorkflowImageEffect", StringComparison.OrdinalIgnoreCase));
    }
}
