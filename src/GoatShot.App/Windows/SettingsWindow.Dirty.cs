using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using MediaBrush = System.Windows.Media.Brush;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace GoatShot.App.Windows;

/// <summary>
/// Unsaved-change tracking. The window offered Cancel and Save but never signalled that anything had
/// changed, so Cancel gave no hint of what it would discard.
/// </summary>
public partial class SettingsWindow
{
    private bool _settingsDirty;
    private bool _dirtyTrackingArmed;

    /// <summary>
    /// Set by the diagnostic renderer and the accessibility auditor. Those close the window
    /// programmatically, and a modal confirmation would hang them.
    /// </summary>
    internal bool SuppressUnsavedChangesPrompt { get; set; }

    /// <summary>
    /// Subscribes to every input inside the settings scroll. Called once the deferred startup work
    /// has finished populating controls, so only real edits count as changes.
    /// </summary>
    private void ArmDirtyTracking()
    {
        if (_dirtyTrackingArmed)
        {
            return;
        }

        _dirtyTrackingArmed = true;
        Walk(SettingsScroll);
        UpdateDirtyIndicator();

        void Walk(DependencyObject node)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            {
                switch (child)
                {
                    case TextBox textBox:
                        textBox.TextChanged += (_, _) => MarkSettingsDirty();
                        break;
                    case CheckBox checkBox:
                        checkBox.Checked += (_, _) => MarkSettingsDirty();
                        checkBox.Unchecked += (_, _) => MarkSettingsDirty();
                        break;
                    case ComboBox comboBox:
                        comboBox.SelectionChanged += (_, _) => MarkSettingsDirty();
                        break;
                    case PasswordBox passwordBox:
                        passwordBox.PasswordChanged += (_, _) => MarkSettingsDirty();
                        break;
                }

                Walk(child);
            }
        }
    }

    /// <summary>
    /// Diagnostic hook: arms tracking and simulates one edit so the indicator can be proofed by the
    /// renderer. Returns false if tracking never armed, which is itself the interesting failure.
    /// </summary>
    internal bool PreviewUnsavedChanges()
    {
        ArmDirtyTracking();
        if (!_dirtyTrackingArmed)
        {
            return false;
        }

        PrivateModeBox.IsChecked = PrivateModeBox.IsChecked != true;
        return _settingsDirty;
    }

    internal void MarkSettingsDirty()
    {
        if (_settingsLoading || !_dirtyTrackingArmed || _settingsDirty)
        {
            return;
        }

        _settingsDirty = true;
        UpdateDirtyIndicator();
    }

    private void ClearSettingsDirty()
    {
        _settingsDirty = false;
        UpdateDirtyIndicator();
    }

    private void UpdateDirtyIndicator()
    {
        SettingsDirtyText.Text = _settingsDirty
            ? "Unsaved changes"
            : "No unsaved changes";
        SettingsDirtyText.Foreground = _settingsDirty
            ? (MediaBrush)FindResource("WarnBrush")
            : (MediaBrush)FindResource("MutedInkBrush");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // DialogResult is true only after a successful save, so anything else with pending edits is a
        // discard the user should get to reconsider.
        if (_settingsDirty && DialogResult != true && !SuppressUnsavedChangesPrompt)
        {
            var confirmation = MessageBox.Show(
                this,
                "Discard your unsaved settings changes?",
                "Unsaved changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        base.OnClosing(e);
    }
}
