using System.Windows;
using System.Windows.Input;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.App.Windows;

public partial class PinnedCaptureWindow : Window
{
    public PinnedCaptureWindow(CaptureItem item)
    {
        InitializeComponent();
        Title = $"Pinned - {item.FileName}";
        PinnedTitle.Text = item.FileName;
        PinnedImage.Source = ImageInterop.LoadBitmapImage(item.FilePath);
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        Opacity = e.NewValue;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
