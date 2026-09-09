using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GoatShot.App.Models;
using GoatShot.App.Services;
using Forms = System.Windows.Forms;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace GoatShot.App.Windows;

public partial class SettingsWindow
{
    private static void ExpandSettingsAncestors(FrameworkElement target)
    {
        for (DependencyObject? parent = target; parent is not null;
             parent = LogicalTreeHelper.GetParent(parent))
        {
            if (parent is Expander expander)
            {
                expander.IsExpanded = true;
            }
        }

        target.UpdateLayout();
    }

    private void ConfigureProviders_Click(object sender, RoutedEventArgs e)
    {
        ProviderConfigurationExpander.IsExpanded = true;
        ProviderJumpBox.BringIntoView();
        ProviderJumpBox.Focus();
    }

    private void RefreshReplaySources_Click(object sender, RoutedEventArgs e)
    {
        RefreshReplaySourceChoices(ReplaySourceIdBox.Text.Trim());
        UpdateReplaySourceHelp();
    }

    private void ReplayBounds_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_settingsLoading && ReplaySourcePreviewCanvas is not null)
        {
            UpdateReplaySourceHelp();
        }
    }

    private void ReplaySourceId_Changed(object sender, TextChangedEventArgs e)
    {
        if (_settingsLoading || ReplaySourcePickerBox is null || ReplaySourcePreviewCanvas is null) return;
        var id = ReplaySourceIdBox.Text.Trim();
        ReplaySourcePickerBox.SelectedItem = ReplaySourcePickerBox.Items.OfType<CaptureOverlayTarget>()
            .FirstOrDefault(target => target.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                target.Id.Equals($"monitor:{id}", StringComparison.OrdinalIgnoreCase));
        UpdateReplaySourceHelp();
    }

    private void UpdateReplaySourcePreview(string source)
    {
        if (ReplaySourcePreviewCanvas is null)
        {
            return;
        }

        var chosen = source is "SelectedMonitor" or "SelectedWindow";
        var region = source is "SelectedRegion" or "FixedRegion";
        ReplayChosenSourcePanel.Visibility = chosen ? Visibility.Visible : Visibility.Collapsed;
        ReplayRegionPanel.Visibility = region ? Visibility.Visible : Visibility.Collapsed;
        var target = ReplaySourcePickerBox.SelectedItem as CaptureOverlayTarget;
        var bounds = region ? ParseReplayBounds(ReplayBoundsBox.Text)
            : chosen && target is not null
                ? new ReplayCaptureBounds(target.Bounds.X, target.Bounds.Y, target.Bounds.Width, target.Bounds.Height)
                : null;
        ReplaySourcePreviewText.Text = bounds is not null
            ? $"{(region ? "Selected region" : target!.DisplayName)} · {bounds.Width} × {bounds.Height} at ({bounds.X}, {bounds.Y})"
            : source switch
            {
                "SelectedMonitor" or "SelectedWindow" => "Choose an available source above. Refresh the list if a window or display is missing.",
                "SelectedRegion" or "FixedRegion" => "Choose a region on screen, or enter valid coordinates.",
                "FollowCursorMonitor" => "Follows the monitor containing your cursor at each segment boundary.",
                "FollowForegroundWindow" => "Follows the active window at each segment boundary.",
                "SeparateMonitorTracks" => "Every display is captured as a separate synchronized track.",
                _ => "All displays are combined into one video."
            };

        var desktop = Forms.SystemInformation.VirtualScreen;
        var scale = Math.Min(300d / Math.Max(1, desktop.Width), 90d / Math.Max(1, desktop.Height));
        var canvas = ReplaySourcePreviewCanvas;
        canvas.Children.Clear();
        canvas.Width = Math.Max(1, desktop.Width * scale);
        canvas.Height = Math.Max(1, desktop.Height * scale);
        foreach (var screen in Forms.Screen.AllScreens)
        {
            AddPreviewBounds(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height,
                source is "AllMonitorsComposite" or "SeparateMonitorTracks");
        }

        if (bounds is not null)
        {
            AddPreviewBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height, true);
        }

        void AddPreviewBounds(int x, int y, int width, int height, bool selected)
        {
            var rectangle = new WpfRectangle
            {
                Width = Math.Max(2, width * scale),
                Height = Math.Max(2, height * scale),
                Stroke = (System.Windows.Media.Brush)FindResource(selected ? "AccentBrush" : "MutedInkBrush"),
                StrokeThickness = selected ? 2 : 1,
                Fill = new SolidColorBrush(selected
                    ? System.Windows.Media.Color.FromArgb(70, 48, 230, 195)
                    : System.Windows.Media.Color.FromArgb(60, 128, 145, 160))
            };
            Canvas.SetLeft(rectangle, (x - desktop.X) * scale);
            Canvas.SetTop(rectangle, (y - desktop.Y) * scale);
            canvas.Children.Add(rectangle);
        }
    }
}
