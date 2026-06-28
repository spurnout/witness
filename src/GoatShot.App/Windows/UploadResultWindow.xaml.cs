using System.Diagnostics;
using System.Windows;
using GoatShot.App.Services;

namespace GoatShot.App.Windows;

public partial class UploadResultWindow : Window
{
    private readonly UploadResultViewModel _model;

    public UploadResultWindow(UploadResultViewModel model)
    {
        _model = model;
        InitializeComponent();
        EscapeKeyCloseBehavior.Attach(this, cancelDialog: true);
        Title = model.Title;
        TitleText.Text = model.Title;
        DestinationText.Text = model.Destination;
        FileNameText.Text = model.FileName;
        MessageText.Text = model.Message;
        UrlBox.Text = model.Url ?? string.Empty;
        MarkdownBox.Text = model.MarkdownLink ?? string.Empty;

        CopyLinkButton.IsEnabled = model.Url is not null;
        OpenLinkButton.IsEnabled = model.CanOpenLink;
        CopyMarkdownButton.IsEnabled = model.MarkdownLink is not null;
        CopyQrPathButton.IsEnabled = !string.IsNullOrWhiteSpace(model.QrCodePath);
        RetryButton.Visibility = model.CanRetry ? Visibility.Visible : Visibility.Collapsed;
        DeleteRemoteButton.IsEnabled = model.CanDeleteRemote;
        DeleteRemoteButton.Opacity = model.CanDeleteRemote ? 1 : 0.45;
        DeleteRemoteButton.ToolTip = model.CanDeleteRemote
            ? "Delete the remote upload."
            : "Remote delete is not available for this provider yet.";

        if (!string.IsNullOrWhiteSpace(model.QrCodePath) && File.Exists(model.QrCodePath))
        {
            QrImage.Source = ImageInterop.LoadBitmapImage(model.QrCodePath);
            QrStatusText.Text = model.QrCodePath;
        }
        else
        {
            QrBorder.Visibility = Visibility.Collapsed;
            QrStatusText.Text = model.Url is null
                ? "No share URL returned."
                : "QR code was not generated.";
        }
    }

    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        if (_model.Url is not null)
        {
            ClipboardInterop.SetText(_model.Url);
        }
    }

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (_model.Url is not null && _model.CanOpenLink)
        {
            Process.Start(new ProcessStartInfo(_model.Url) { UseShellExecute = true });
        }
    }

    private void CopyMarkdown_Click(object sender, RoutedEventArgs e)
    {
        if (_model.MarkdownLink is not null)
        {
            ClipboardInterop.SetText(_model.MarkdownLink);
        }
    }

    private void CopyQrPath_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_model.QrCodePath))
        {
            ClipboardInterop.SetText(_model.QrCodePath);
        }
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
