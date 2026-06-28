using System.Windows;

namespace GoatShot.App.Windows;

public partial class InputDialog : Window
{
    public InputDialog(string prompt, string defaultValue = "")
    {
        InitializeComponent();
        PromptText.Text = prompt;
        ResponseBox.Text = defaultValue;
        ResponseBox.Focus();
        ResponseBox.SelectAll();
    }

    public string ResponseText => ResponseBox.Text;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
