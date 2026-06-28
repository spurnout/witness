using System.Windows;
using GoatShot.App.Models;

namespace GoatShot.App.Windows;

public partial class CaptureTaskWindow : Window
{
    private readonly CaptureTaskViewModel _model;

    public CaptureTaskWindow(CaptureTaskViewModel model)
    {
        _model = model;
        InitializeComponent();
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
    }

    public event EventHandler<CaptureTaskActionKind>? ActionRequested;

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
