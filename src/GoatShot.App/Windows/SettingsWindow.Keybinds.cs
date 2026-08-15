using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GoatShot.App.Models;
using GoatShot.App.Services;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MediaBrush = System.Windows.Media.Brush;

namespace GoatShot.App.Windows;

/// <summary>
/// Keybinds section: renders one row per rebindable global hotkey and records new chords straight
/// from the keyboard. Global registrations are suspended while recording so pressing the chord being
/// rebound does not fire the action it is bound to.
/// </summary>
public partial class SettingsWindow
{
    private const string KeybindListeningPrompt = "Press keys…";

    private readonly Dictionary<HotkeyAction, string> _keybindDrafts = new();
    private readonly Dictionary<HotkeyAction, Button> _keybindGestureButtons = new();
    private readonly Dictionary<HotkeyAction, TextBlock> _keybindStatusTexts = new();
    private HotkeyAction? _listeningKeybindAction;

    /// <summary>
    /// Guarantees the global hotkeys come back even if the window is closed mid-recording; otherwise
    /// they would stay suspended for the rest of the session.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        EndKeybindCapture();
        base.OnClosed(e);
    }

    private void LoadKeybinds(AppSettings settings)
    {
        _keybindDrafts.Clear();
        foreach (var keybind in KeybindCatalog.Resolve(settings.Keybinds))
        {
            _keybindDrafts[keybind.Action] = keybind.Gesture;
        }

        BuildKeybindRows();
        RefreshKeybindVisuals();
    }

    private void ApplyKeybindSettings(AppSettings settings)
    {
        settings.Keybinds = KeybindCatalog.ToAssignments(ResolveKeybindDrafts()).ToList();
        KeybindCatalog.SyncReplayHotkeyMirror(settings);
    }

    private IReadOnlyList<ResolvedKeybind> ResolveKeybindDrafts() =>
        KeybindCatalog.Resolve(_keybindDrafts
            .Select(draft => new KeybindAssignment { Action = draft.Key, Gesture = draft.Value }));

    private void BuildKeybindRows()
    {
        CancelKeybindCapture();
        KeybindRowsPanel.Children.Clear();
        _keybindGestureButtons.Clear();
        _keybindStatusTexts.Clear();

        foreach (var group in KeybindCatalog.Definitions.GroupBy(definition => definition.Group))
        {
            KeybindRowsPanel.Children.Add(new TextBlock
            {
                Text = group.Key,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                Foreground = (MediaBrush)FindResource("MutedInkBrush"),
                Margin = new Thickness(0, 10, 0, 6)
            });

            foreach (var definition in group)
            {
                KeybindRowsPanel.Children.Add(CreateKeybindRow(definition));
            }
        }
    }

    private Border CreateKeybindRow(KeybindDefinition definition)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock
        {
            Text = definition.Label,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        labels.Children.Add(new TextBlock
        {
            Text = definition.Description,
            Foreground = (MediaBrush)FindResource("MutedInkBrush"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 12, 0)
        });

        var status = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 12, 0),
            Visibility = Visibility.Collapsed
        };
        System.Windows.Automation.AutomationProperties.SetLiveSetting(
            status,
            System.Windows.Automation.AutomationLiveSetting.Polite);
        labels.Children.Add(status);
        _keybindStatusTexts[definition.Action] = status;

        Grid.SetColumn(labels, 0);
        grid.Children.Add(labels);

        var gestureButton = new Button
        {
            Tag = definition.Action,
            MinWidth = 168,
            Style = (Style)FindResource("SecondaryButton"),
            VerticalAlignment = VerticalAlignment.Center
        };
        gestureButton.Click += KeybindGesture_Click;
        _keybindGestureButtons[definition.Action] = gestureButton;

        var clearButton = new Button
        {
            Content = "Clear",
            Tag = definition.Action,
            Style = (Style)FindResource("SecondaryButton"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        clearButton.Click += KeybindClear_Click;
        System.Windows.Automation.AutomationProperties.SetName(clearButton, $"Clear the {definition.Label} shortcut");

        var resetButton = new Button
        {
            Content = "Reset",
            Tag = definition.Action,
            Style = (Style)FindResource("SecondaryButton"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        resetButton.Click += KeybindReset_Click;
        System.Windows.Automation.AutomationProperties.SetName(
            resetButton,
            $"Reset the {definition.Label} shortcut to {KeybindCatalog.ToDisplay(definition.DefaultGesture)}");

        var actions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(gestureButton);
        actions.Children.Add(clearButton);
        actions.Children.Add(resetButton);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);

        return new Border
        {
            Background = (MediaBrush)FindResource("PanelBrush"),
            BorderBrush = (MediaBrush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 6),
            Child = grid
        };
    }

    private void KeybindGesture_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: HotkeyAction action })
        {
            return;
        }

        if (_listeningKeybindAction == action)
        {
            CancelKeybindCapture();
            return;
        }

        BeginKeybindCapture(action);
    }

    private void KeybindClear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HotkeyAction action })
        {
            CommitKeybind(action, string.Empty);
        }
    }

    private void KeybindReset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HotkeyAction action })
        {
            CommitKeybind(action, KeybindCatalog.DefaultGesture(action));
        }
    }

    private void RestoreDefaultKeybinds_Click(object sender, RoutedEventArgs e)
    {
        CancelKeybindCapture();
        foreach (var definition in KeybindCatalog.Definitions)
        {
            _keybindDrafts[definition.Action] = KeybindCatalog.NormalizeGesture(definition.DefaultGesture);
        }

        MarkSettingsDirty();
        RefreshKeybindVisuals();
    }

    private void BeginKeybindCapture(HotkeyAction action)
    {
        CancelKeybindCapture();
        _listeningKeybindAction = action;

        // Release the OS registrations so the chord reaches this window instead of firing its action.
        _services.Hotkeys.Suspend();
        PreviewKeyDown += Keybind_PreviewKeyDown;
        PreviewKeyUp += Keybind_PreviewKeyUp;

        if (_keybindGestureButtons.TryGetValue(action, out var button))
        {
            button.Content = KeybindListeningPrompt;
            button.Focus();
        }

        KeybindCaptureHintText.Text =
            "Recording: press a modifier plus a letter, digit, or function key. PrintScreen and F-keys work on their own. Esc cancels, Backspace clears.";
    }

    private void CancelKeybindCapture()
    {
        if (_listeningKeybindAction is null)
        {
            return;
        }

        EndKeybindCapture();
        RefreshKeybindVisuals();
    }

    private void EndKeybindCapture()
    {
        if (_listeningKeybindAction is null)
        {
            return;
        }

        _listeningKeybindAction = null;
        PreviewKeyDown -= Keybind_PreviewKeyDown;
        PreviewKeyUp -= Keybind_PreviewKeyUp;
        _services.Hotkeys.Resume();
        KeybindCaptureHintText.Text =
            "Esc cancels while recording. Backspace clears a shortcut so the action has no global key.";
    }

    private void Keybind_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_listeningKeybindAction is not { } action)
        {
            return;
        }

        // Swallow everything while recording: Tab, arrows, and Space would otherwise move focus or
        // re-trigger the button instead of being read as part of the chord.
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            CancelKeybindCapture();
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            CommitKeybind(action, string.Empty);
            return;
        }

        // PrintScreen usually only surfaces on key-up; wait for Keybind_PreviewKeyUp to handle it.
        if (key == Key.Snapshot || IsModifierKey(key))
        {
            return;
        }

        TryCommitCapturedChord(action, key, Keyboard.Modifiers);
    }

    private void Keybind_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (_listeningKeybindAction is not { } action)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Key.Snapshot)
        {
            return;
        }

        e.Handled = true;
        TryCommitCapturedChord(action, key, Keyboard.Modifiers);
    }

    private void TryCommitCapturedChord(HotkeyAction action, Key key, ModifierKeys modifiers)
    {
        var gesture = KeybindGestureReader.TryBuild(key, modifiers);
        if (gesture is null)
        {
            KeybindCaptureHintText.Text = KeybindGestureReader.Describe(key) is { } name
                ? $"{name} cannot be used on its own. Add Ctrl, Alt, or Shift, or pick a function key."
                : "That key cannot be used as a global shortcut. Use a letter, digit, function key, or PrintScreen.";
            return;
        }

        CommitKeybind(action, gesture);
    }

    private void CommitKeybind(HotkeyAction action, string gesture)
    {
        EndKeybindCapture();
        _keybindDrafts[action] = KeybindCatalog.NormalizeGesture(gesture);
        // Keybind rows are buttons, so they are outside the input subscriptions dirty tracking uses.
        MarkSettingsDirty();
        RefreshKeybindVisuals();
    }

    private void RefreshKeybindVisuals()
    {
        var resolved = ResolveKeybindDrafts();
        var conflicts = KeybindCatalog.FindConflicts(resolved);
        var conflicting = conflicts.SelectMany(conflict => conflict.Actions).ToHashSet();

        foreach (var keybind in resolved)
        {
            if (_keybindGestureButtons.TryGetValue(keybind.Action, out var button) &&
                _listeningKeybindAction != keybind.Action)
            {
                button.Content = keybind.DisplayGesture;
                System.Windows.Automation.AutomationProperties.SetName(
                    button,
                    $"{keybind.Label} shortcut, currently {keybind.DisplayGesture}. Activate, then press the keys you want.");
            }

            if (!_keybindStatusTexts.TryGetValue(keybind.Action, out var status))
            {
                continue;
            }

            var (message, brushKey) = DescribeKeybindStatus(keybind, conflicting.Contains(keybind.Action));
            status.Text = message;
            status.Foreground = (MediaBrush)FindResource(brushKey);
            status.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        RefreshReplayHotkeySummary(resolved);

        KeybindConflictText.Text = conflicts.Count == 0
            ? string.Empty
            : string.Join(
                Environment.NewLine,
                conflicts.Select(conflict =>
                    $"{conflict.DisplayGesture} is assigned to {DescribeActions(conflict.Actions)}. Only the first will register."));
        KeybindConflictBanner.Visibility = conflicts.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Keeps the Recording section's replay blurb honest: those two gestures now live in Keybinds, so
    /// this mirrors whatever is currently drafted there rather than re-editing it in two places.
    /// </summary>
    private void RefreshReplayHotkeySummary(IReadOnlyList<ResolvedKeybind>? resolved = null)
    {
        resolved ??= ResolveKeybindDrafts();
        ReplayHotkeySummaryText.Text =
            $"Shortcuts: arm/pause {DescribeKeybind(resolved, HotkeyAction.ToggleReplay)}, " +
            $"save {DescribeKeybind(resolved, HotkeyAction.SaveReplay)}. Edit them in the Keybinds section.";
    }

    private static string DescribeKeybind(IReadOnlyList<ResolvedKeybind> resolved, HotkeyAction action) =>
        resolved.FirstOrDefault(keybind => keybind.Action == action)?.DisplayGesture ?? "Not set";

    private void EditReplayKeybinds_Click(object sender, RoutedEventArgs e)
    {
        SelectSection("Keybinds", alignSectionToTop: true);
        Dispatcher.BeginInvoke(
            () => _keybindGestureButtons.TryGetValue(HotkeyAction.ToggleReplay, out var button) ? button.Focus() : false,
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static (string Message, string BrushKey) DescribeKeybindStatus(ResolvedKeybind keybind, bool conflicting)
    {
        if (!keybind.IsValid)
        {
            return ($"'{keybind.Gesture}' is not a shortcut Receipts can register. Record it again or reset it.", "WarnBrush");
        }

        if (conflicting)
        {
            return ("Conflicts with another action below.", "WarnBrush");
        }

        if (!keybind.IsBound)
        {
            return ($"No global shortcut. Default is {keybind.DisplayDefaultGesture}.", "MutedInkBrush");
        }

        return keybind.IsDefault
            ? (string.Empty, "MutedInkBrush")
            : ($"Customized. Default is {keybind.DisplayDefaultGesture}.", "MutedInkBrush");
    }

    private static string DescribeActions(IReadOnlyList<HotkeyAction> actions) =>
        string.Join(
            " and ",
            actions.Select(action =>
                KeybindCatalog.Definitions.FirstOrDefault(definition => definition.Action == action)?.Label
                ?? action.ToString()));

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin or
        Key.System;
}
