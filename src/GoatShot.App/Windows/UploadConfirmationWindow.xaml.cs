using System.Windows;
using GoatShot.App.Services;

namespace GoatShot.App.Windows;

public partial class UploadConfirmationWindow : Window
{
    public UploadConfirmationWindow(UploadConfirmationViewModel model)
    {
        InitializeComponent();
        Title = model.Title;
        DestinationText.Text = model.Destination;
        FileNameText.Text = model.FileName;
        CaptureTypeText.Text = model.CaptureType;
        SizeText.Text = model.Size;
        PathText.Text = model.FilePath;
        RiskText.Text = model.RiskSummary;
        PrivacyText.Text = model.PrivacySummary;
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
