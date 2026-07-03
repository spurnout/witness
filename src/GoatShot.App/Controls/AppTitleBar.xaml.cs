using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;

namespace GoatShot.App.Controls;

/// <summary>
/// Branded title bar used by app windows that run with <see cref="AppWindowChrome"/>.
/// Caption buttons adapt to the host window's <see cref="Window.ResizeMode"/>.
/// The base class comes from the XAML-generated partial.
/// </summary>
public partial class AppTitleBar
{
    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(
            nameof(Subtitle),
            typeof(string),
            typeof(AppTitleBar),
            new PropertyMetadata(string.Empty));

    private static readonly DependencyPropertyDescriptor? ResizeModeDescriptor =
        DependencyPropertyDescriptor.FromProperty(Window.ResizeModeProperty, typeof(Window));

    private Window? _window;

    public AppTitleBar()
    {
        InitializeComponent();
        Loaded += AppTitleBar_Loaded;
        Unloaded += AppTitleBar_Unloaded;
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    private void AppTitleBar_Loaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        if (_window is null)
        {
            return;
        }

        _window.StateChanged += Window_StateChanged;
        ResizeModeDescriptor?.AddValueChanged(_window, Window_ResizeModeChanged);
        UpdateCaptionButtons();
    }

    private void AppTitleBar_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_window is null)
        {
            return;
        }

        _window.StateChanged -= Window_StateChanged;
        ResizeModeDescriptor?.RemoveValueChanged(_window, Window_ResizeModeChanged);
        _window = null;
    }

    private void Window_ResizeModeChanged(object? sender, EventArgs e)
    {
        UpdateCaptionButtons();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        UpdateCaptionButtons();
    }

    private void UpdateCaptionButtons()
    {
        if (_window is null)
        {
            return;
        }

        var canResize = _window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
        MinimizeButton.Visibility = _window.ResizeMode == ResizeMode.NoResize
            ? Visibility.Collapsed
            : Visibility.Visible;
        MaximizeButton.Visibility = canResize ? Visibility.Visible : Visibility.Collapsed;

        var isMaximized = _window.WindowState == WindowState.Maximized;
        MaximizeGlyph.Text = isMaximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = isMaximized ? "Restore" : "Maximize";
        AutomationProperties.SetName(MaximizeButton, isMaximized ? "Restore window" : "Maximize window");
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            SystemCommands.MinimizeWindow(_window);
        }
    }

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        if (_window is null)
        {
            return;
        }

        if (_window.WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(_window);
        }
        else
        {
            SystemCommands.MaximizeWindow(_window);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_window is not null)
        {
            SystemCommands.CloseWindow(_window);
        }
    }
}
