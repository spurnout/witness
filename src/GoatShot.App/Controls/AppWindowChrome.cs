using System.Windows;
using System.Windows.Shell;

namespace GoatShot.App.Controls;

/// <summary>
/// Attached behavior that swaps the native title bar for app-drawn chrome while
/// keeping native resize borders, edge snapping, and the system menu.
/// </summary>
public static class AppWindowChrome
{
    private const double DefaultCaptionHeight = 40d;

    // Approximates SM_CXPADDEDBORDER, which SystemParameters does not expose.
    private const double PaddedBorderCompensation = 4d;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(AppWindowChrome),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty CaptionHeightProperty =
        DependencyProperty.RegisterAttached(
            "CaptionHeight",
            typeof(double),
            typeof(AppWindowChrome),
            new PropertyMetadata(DefaultCaptionHeight, OnCaptionHeightChanged));

    private static readonly DependencyProperty BaseContentMarginProperty =
        DependencyProperty.RegisterAttached(
            "BaseContentMargin",
            typeof(Thickness?),
            typeof(AppWindowChrome),
            new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static double GetCaptionHeight(DependencyObject element) => (double)element.GetValue(CaptionHeightProperty);

    public static void SetCaptionHeight(DependencyObject element, double value) => element.SetValue(CaptionHeightProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window || e.NewValue is not true)
        {
            return;
        }

        window.WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = Math.Max(0d, GetCaptionHeight(window)),
            ResizeBorderThickness = new Thickness(6),
            // A hair of glass on one edge keeps the DWM drop shadow alive.
            GlassFrameThickness = new Thickness(0, 0, 0, 1),
            UseAeroCaptionButtons = false,
        });

        window.Loaded += Window_Loaded;
        window.StateChanged += Window_StateChanged;
    }

    private static void OnCaptionHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window && WindowChrome.GetWindowChrome(window) is { } chrome)
        {
            chrome.CaptionHeight = Math.Max(0d, (double)e.NewValue);
        }
    }

    private static void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyMaximizedContentMargin((Window)sender);
    }

    private static void Window_StateChanged(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            ApplyMaximizedContentMargin(window);
        }
    }

    private static void ApplyMaximizedContentMargin(Window window)
    {
        if (window.Content is not FrameworkElement root)
        {
            return;
        }

        var baseMargin = (Thickness?)window.GetValue(BaseContentMarginProperty);
        if (baseMargin is null)
        {
            baseMargin = root.Margin;
            window.SetValue(BaseContentMarginProperty, baseMargin);
        }

        if (window.WindowState == WindowState.Maximized)
        {
            // A frameless maximized window overflows the work area by the resize
            // border on every side; pad the content back into view.
            var resize = SystemParameters.WindowResizeBorderThickness;
            root.Margin = new Thickness(
                baseMargin.Value.Left + resize.Left + PaddedBorderCompensation,
                baseMargin.Value.Top + resize.Top + PaddedBorderCompensation,
                baseMargin.Value.Right + resize.Right + PaddedBorderCompensation,
                baseMargin.Value.Bottom + resize.Bottom + PaddedBorderCompensation);
        }
        else
        {
            root.Margin = baseMargin.Value;
        }
    }
}
