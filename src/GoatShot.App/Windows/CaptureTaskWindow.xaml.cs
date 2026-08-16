using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.App.Windows;

public partial class CaptureTaskWindow : Window
{
    private readonly CaptureTaskViewModel _model;
    private readonly int _autoDismissSeconds;
    private bool _autoDismissCancelled;

    public CaptureTaskWindow(CaptureTaskViewModel model, int autoDismissSeconds = 0)
    {
        _model = model;
        _autoDismissSeconds = CaptureTaskAutoDismiss.NormalizeSeconds(autoDismissSeconds);
        InitializeComponent();
        EscapeKeyCloseBehavior.Attach(this);
        Title = model.Title;
        TitleText.Text = model.Title;
        FileNameText.Text = model.FileName;
        CaptureTypeText.Text = model.CaptureType;
        SizeText.Text = model.Size;
        PrivacyText.Text = model.PrivacySummary;
        PathText.Text = model.FilePath;

        Configure(OpenButton, CaptureTaskActionKind.OpenFile);
        Configure(EditButton, CaptureTaskActionKind.OpenEditor);
        Configure(CopyButton, CaptureTaskActionKind.Copy);
        Configure(ShareButton, CaptureTaskActionKind.Share);
        Configure(AiButton, CaptureTaskActionKind.AiExplain);
        Configure(DocumentButton, CaptureTaskActionKind.ExportDocument);
        Configure(ScriptDryRunButton, CaptureTaskActionKind.DryRunScript);
        Configure(WebhookDryRunButton, CaptureTaskActionKind.DryRunWebhook);
        Configure(DeleteButton, CaptureTaskActionKind.DeleteLocalFile);

        StartAutoDismiss();
    }

    public event EventHandler<CaptureTaskActionKind>? ActionRequested;

    /// <summary>
    /// Begins the countdown to the fade-out. The window is a convenience surface, so it clears
    /// itself rather than accumulating on screen across a run of quick captures.
    /// </summary>
    private void StartAutoDismiss()
    {
        if (!CaptureTaskAutoDismiss.IsEnabled(_autoDismissSeconds))
        {
            return;
        }

        // Any sign the window is actually being used stops the countdown for good. A window that
        // dissolves while the pointer is travelling toward Share is worse than one that lingers.
        MouseEnter += (_, _) => CancelAutoDismiss();
        PreviewMouseDown += (_, _) => CancelAutoDismiss();
        PreviewKeyDown += (_, _) => CancelAutoDismiss();

        // Escape closes via EscapeKeyCloseBehavior, which marks the key event handled before the
        // PreviewKeyDown hook above can see it — so cancellation also rides window close itself,
        // or the drain animation would keep the closed window's object graph alive until it fired.
        Closed += (_, _) => CancelAutoDismiss();

        // The draining bar is the countdown clock: its Completed starts the fade, so the warning
        // the user sees can never drift out of sync with the dismissal it is warning about.
        AutoDismissIndicator.Visibility = Visibility.Visible;
        var drain = new DoubleAnimation(1d, 0d, new Duration(TimeSpan.FromSeconds(_autoDismissSeconds)));
        drain.Completed += (_, _) => BeginFadeOut();
        AutoDismissIndicatorScale.BeginAnimation(ScaleTransform.ScaleXProperty, drain);
    }

    private void CancelAutoDismiss()
    {
        if (_autoDismissCancelled)
        {
            return;
        }

        _autoDismissCancelled = true;
        AutoDismissIndicatorScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        AutoDismissIndicator.Visibility = Visibility.Collapsed;

        // Cancelling mid-fade has to undo the animation, not just stop the clock. Clearing the
        // animation first is what lets the local Opacity value take effect again.
        BeginAnimation(OpacityProperty, null);
        Opacity = 1d;
    }

    private void BeginFadeOut()
    {
        if (_autoDismissCancelled)
        {
            return;
        }

        var fade = new DoubleAnimation(1d, 0d, new Duration(CaptureTaskAutoDismiss.FadeDuration));
        fade.Completed += (_, _) =>
        {
            if (!_autoDismissCancelled)
            {
                Close();
            }
        };

        BeginAnimation(OpacityProperty, fade);
    }

    private void Configure(System.Windows.Controls.Button button, CaptureTaskActionKind kind)
    {
        var action = _model.Actions.First(item => item.Kind == kind);
        button.Tag = kind;
        button.Content = action.Title;
        button.IsEnabled = action.IsEnabled;
        button.ToolTip = action.IsEnabled
            ? action.Description
            : $"{action.Description} {action.DisabledReason}".Trim();
        if (!action.IsEnabled)
        {
            button.Opacity = 0.45;
        }
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: CaptureTaskActionKind kind })
        {
            return;
        }

        var action = _model.Actions.First(item => item.Kind == kind);
        ActionHintText.Text = action.Description;
        ActionRequested?.Invoke(this, kind);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
