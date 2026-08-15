using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using GoatShot.App.Services;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBox = System.Windows.Controls.ListBox;
using Point = System.Windows.Point;
using TextBox = System.Windows.Controls.TextBox;

namespace GoatShot.App.Windows;

/// <summary>
/// Settings search. The window is one long scroll of roughly 240 controls, so this indexes every
/// labelled control once and jumps to the chosen one rather than making the reader know which
/// section owns it.
/// </summary>
public partial class SettingsWindow
{
    private const int MaxSearchResults = 12;

    private readonly List<(SettingsSearchEntry Entry, FrameworkElement Target)> _searchIndex = new();
    private bool _searchIndexBuilt;

    /// <summary>Drives the search box from the diagnostic renderer so the results can be proofed.</summary>
    internal void PreviewSettingsSearch(string query)
    {
        SettingsSearchBox.Text = query;
        SettingsSearchBox.Focus();
    }

    /// <summary>Number of indexed controls, used by tests and diagnostics.</summary>
    internal int SettingsSearchIndexCount
    {
        get
        {
            BuildSettingsSearchIndex();
            return _searchIndex.Count;
        }
    }

    private void BuildSettingsSearchIndex()
    {
        if (_searchIndexBuilt)
        {
            return;
        }

        _searchIndexBuilt = true;
        var headings = new Dictionary<DependencyObject, SettingsSection>();
        foreach (var section in SettingsSectionCatalog.All)
        {
            if (FindSettingsSectionTarget(section.Key) is { } heading)
            {
                headings[heading] = section;
            }
        }

        var current = SettingsSectionCatalog.All[0];
        string? pendingLabel = null;
        Walk(SettingsScroll);

        void Walk(DependencyObject node)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            {
                if (headings.TryGetValue(child, out var section))
                {
                    current = section;
                    pendingLabel = null;
                    continue;
                }

                // The window's convention is a caption TextBlock immediately before its input, so the
                // most recent caption is the best label for the next unlabelled control.
                if (child is TextBlock caption)
                {
                    var text = caption.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(text) && text.Length <= 80)
                    {
                        pendingLabel = text;
                    }
                }
                else if (child is FrameworkElement element && IsSearchableControl(element))
                {
                    var label = DescribeSearchTarget(element, pendingLabel);
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        _searchIndex.Add((new SettingsSearchEntry(label, current.Key, current.Label), element));
                    }

                    pendingLabel = null;
                }

                Walk(child);
            }
        }
    }

    private static bool IsSearchableControl(FrameworkElement element) =>
        element is CheckBox or TextBox or ComboBox or PasswordBox or Button &&
        element is not ListBoxItem;

    private static string? DescribeSearchTarget(FrameworkElement element, string? pendingLabel)
    {
        var automationName = System.Windows.Automation.AutomationProperties.GetName(element);
        if (!string.IsNullOrWhiteSpace(automationName))
        {
            return automationName.Trim();
        }

        if (element is CheckBox { Content: string content } && !string.IsNullOrWhiteSpace(content))
        {
            return content.Trim();
        }

        if (element is Button { Content: string buttonContent } && !string.IsNullOrWhiteSpace(buttonContent))
        {
            return buttonContent.Trim();
        }

        return pendingLabel;
    }

    private void SettingsSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        BuildSettingsSearchIndex();
        var query = SettingsSearchBox.Text;
        var matches = SettingsSearchIndex.Match(
            _searchIndex.Select(item => item.Entry),
            query,
            MaxSearchResults);

        SettingsSearchResults.Items.Clear();
        foreach (var match in matches)
        {
            SettingsSearchResults.Items.Add(new ListBoxItem
            {
                Content = new TextBlock
                {
                    Text = match.Display,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                Tag = match,
                ToolTip = match.Display
            });
        }

        SettingsSearchPopup.IsOpen = matches.Count > 0;
        SettingsSearchHintText.Text = string.IsNullOrWhiteSpace(query)
            ? "Search settings"
            : matches.Count == 0
                ? "No setting matches that."
                : $"{matches.Count} match{(matches.Count == 1 ? string.Empty : "es")}. Press Down to pick one.";
    }

    private void SettingsSearch_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClearSettingsSearch();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Down or Key.Enter && SettingsSearchResults.Items.Count > 0)
        {
            SettingsSearchResults.SelectedIndex = 0;
            (SettingsSearchResults.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
    }

    private void SettingsSearchResults_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClearSettingsSearch();
            e.Handled = true;
        }
    }

    private void SettingsSearchResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsSearchResults.SelectedItem is not ListBoxItem { Tag: SettingsSearchEntry entry })
        {
            return;
        }

        var target = _searchIndex
            .FirstOrDefault(item => ReferenceEquals(item.Entry, entry) ||
                (item.Entry.Label == entry.Label && item.Entry.SectionKey == entry.SectionKey))
            .Target;
        if (target is null)
        {
            return;
        }

        SettingsSearchPopup.IsOpen = false;
        SelectSection(entry.SectionKey);
        Dispatcher.BeginInvoke(
            () =>
            {
                target.BringIntoView();
                target.UpdateLayout();
                var top = target.TransformToAncestor(SettingsScroll).Transform(new Point(0, 0)).Y +
                    SettingsScroll.VerticalOffset;
                // Leave headroom so the caption above the control stays visible.
                SettingsScroll.ScrollToVerticalOffset(Math.Max(0, top - 90));
                target.Focus();
            },
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ClearSettingsSearch()
    {
        SettingsSearchBox.Clear();
        SettingsSearchResults.Items.Clear();
        SettingsSearchPopup.IsOpen = false;
        SettingsSearchHintText.Text = "Search settings";
        SettingsSearchBox.Focus();
    }
}
