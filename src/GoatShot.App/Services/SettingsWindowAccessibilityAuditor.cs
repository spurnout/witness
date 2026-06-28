using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using GoatShot.App.Windows;
using WpfButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfControl = System.Windows.Controls.Control;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using WpfTextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace GoatShot.App.Services;

public static class SettingsWindowAccessibilityAuditor
{
    public static async Task AuditAsync(
        AppServices services,
        string? sectionKey,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("A settings audit output path is required.", nameof(outputPath));
        }

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var section = SettingsSectionCatalog.Find(sectionKey) ?? SettingsSectionCatalog.All.First();
        var window = new SettingsWindow(services)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            Width = 980,
            Height = 820,
            ShowInTaskbar = false
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            window.Show();
            window.SelectSection(section.Key, alignSectionToTop: true);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();

            var controls = FindSectionControls(window, section.Key)
                .Where(IsKeyboardRelevant)
                .Select((element, index) => InspectControl(element, index + 1))
                .ToList();

            await File.WriteAllTextAsync(
                fullPath,
                BuildReport(section, controls),
                cancellationToken);
        }
        finally
        {
            window.Close();
        }
    }

    private static IEnumerable<FrameworkElement> FindSectionControls(Window window, string sectionKey)
    {
        var allElements = Descendants(window).ToList();
        var sectionName = $"{sectionKey}SettingsSection";
        var start = allElements.FirstOrDefault(element => element.Name.Equals(sectionName, StringComparison.Ordinal));
        if (start is null)
        {
            return allElements;
        }

        var nextSection = SettingsSectionCatalog.All
            .SkipWhile(section => !section.Key.Equals(sectionKey, StringComparison.OrdinalIgnoreCase))
            .Skip(1)
            .FirstOrDefault();
        var next = nextSection is null
            ? null
            : allElements.FirstOrDefault(element => element.Name.Equals($"{nextSection.Key}SettingsSection", StringComparison.Ordinal));

        var top = ElementTop(window, start);
        var bottom = next is null ? double.PositiveInfinity : ElementTop(window, next);
        return allElements.Where(element =>
        {
            var elementTop = ElementTop(window, element);
            return elementTop >= top && elementTop < bottom;
        });
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

    private static double ElementTop(Window window, FrameworkElement element)
    {
        try
        {
            return element.TransformToAncestor(window).Transform(new System.Windows.Point(0, 0)).Y;
        }
        catch
        {
            return double.PositiveInfinity;
        }
    }

    private static bool IsKeyboardRelevant(FrameworkElement element)
    {
        if (!element.IsVisible)
        {
            return false;
        }

        if (element is Separator or ScrollViewer or WpfScrollBar)
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

    private static SettingsAccessibilityItem InspectControl(FrameworkElement element, int index)
    {
        var peerName = UIElementAutomationPeer.CreatePeerForElement(element)?.GetName() ?? string.Empty;
        var automationName = AutomationProperties.GetName(element);
        var name = FirstNonBlank(automationName, peerName, ContentName(element));
        var control = element as WpfControl;
        var focusAccepted = element.IsEnabled && element.Focusable && element.Focus();

        return new SettingsAccessibilityItem(
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

    private static string ContentName(FrameworkElement element)
    {
        return element switch
        {
            ContentControl { Content: string text } => text,
            HeaderedContentControl { Header: string text } => text,
            _ => string.Empty
        };
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static string BuildReport(SettingsSection section, IReadOnlyList<SettingsAccessibilityItem> controls)
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
        builder.AppendLine($"# Settings Accessibility / Focus Audit - {section.Label}");
        builder.AppendLine();
        builder.AppendLine($"Date: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine();
        builder.AppendLine("Scope: app-owned WPF visual-tree audit for the requested Settings section.");
        builder.AppendLine("Evidence limit: this is keyboard-focus and automation-name evidence from the running WPF app. It is not a full screen-reader session, WCAG certification, or text-scaling proof.");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine($"- Focusable or keyboard-relevant controls reviewed: {controls.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Enabled controls reviewed: {enabled.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Enabled keyboard/live controls missing accessible names: {missingNames.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Enabled focusable controls that rejected programmatic focus: {focusRejected.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Live status regions observed: {liveRegions.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine();

        if (missingNames.Count == 0)
        {
            builder.AppendLine("- Result: no enabled keyboard-relevant controls in this section were missing an accessible name in this app-owned audit.");
        }
        else
        {
            builder.AppendLine("- Result: accessible-name gaps remain for " + string.Join(", ", missingNames.Select(control => ControlLabel(control))) + ".");
        }

        if (focusRejected.Count == 0)
        {
            builder.AppendLine("- Result: every enabled focusable tab-stop control accepted programmatic focus.");
        }
        else
        {
            builder.AppendLine("- Result: focus acceptance needs follow-up for " + string.Join(", ", focusRejected.Select(control => ControlLabel(control))) + ".");
        }

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

    private static string ControlLabel(SettingsAccessibilityItem control)
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

    private sealed record SettingsAccessibilityItem(
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
