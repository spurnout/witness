using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.App.Windows;

public partial class CaptureGalleryWindow : Window
{
    private readonly DispatcherTimer _dismissTimer;
    private int _dismissRevision;
    private bool _keyboardInteraction;
    private bool _closed;
    private IReadOnlyList<CaptureGalleryEntry> _entries = [];
    private CaptureGalleryEntry? _selectedEntry;

    public static readonly DependencyProperty GalleryColumnsProperty = DependencyProperty.Register(
        nameof(GalleryColumns), typeof(int), typeof(CaptureGalleryWindow), new PropertyMetadata(6));

    public int GalleryColumns
    {
        get => (int)GetValue(GalleryColumnsProperty);
        private set => SetValue(GalleryColumnsProperty, value);
    }

    public CaptureGalleryWindow(int autoDismissSeconds = CaptureTaskAutoDismiss.DefaultSeconds)
    {
        InitializeComponent();
        EscapeKeyCloseBehavior.Attach(this);
        var seconds = CaptureTaskAutoDismiss.NormalizeSeconds(autoDismissSeconds);
        _dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _dismissTimer.Tick += DismissTimer_Tick;
        if (seconds == 0)
        {
            DismissHintText.Text = "Double-click an image for actions · Esc to close";
        }

        Loaded += (_, _) => RestartAutoDismiss();
        SizeChanged += (_, _) => ReflowRows();
        MouseEnter += (_, _) => PauseAutoDismiss();
        MouseLeave += (_, _) => RestartAutoDismiss();
        PreviewKeyDown += (_, _) =>
        {
            _keyboardInteraction = true;
            PauseAutoDismiss();
        };
        Deactivated += (_, _) =>
        {
            _keyboardInteraction = false;
            RestartAutoDismiss();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            PauseAutoDismiss();
            _dismissTimer.Tick -= DismissTimer_Tick;
        };
    }

    public event EventHandler<CaptureItem>? CaptureRequested;
    public event EventHandler? LibraryRequested;

    public void ShowNearPointer()
    {
        var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        var handle = new WindowInteropHelper(this).EnsureHandle();
        // Native screen coordinates remain physical pixels on monitors with different scaling.
        // Move the hidden HWND first so its DPI is that of the destination monitor.
        SetWindowPos(handle, IntPtr.Zero, area.Left, area.Top, 0, 0, 0x0015); // NOSIZE | NOZORDER | NOACTIVATE
        var dpi = GetDpiForWindow(handle);
        var scale = (dpi == 0 ? 96u : dpi) / 96d;
        MinWidth = Math.Min(480d, area.Width / scale - 24d);
        MinHeight = Math.Min(320d, area.Height / scale - 24d);
        Width = Math.Min(Width, area.Width / scale - 24d);
        Height = Math.Min(Height, area.Height / scale - 24d);
        var width = (int)Math.Ceiling(Width * scale);
        var height = (int)Math.Ceiling(Height * scale);
        var margin = (int)Math.Ceiling(12d * scale);
        SetWindowPos(handle, IntPtr.Zero, area.Right - width - margin, area.Bottom - height - margin,
            width, height, 0x0014); // NOZORDER | NOACTIVATE
        Show();
        RestartAutoDismiss();
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    public void UpdateCaptures(IEnumerable<CaptureItem> captures, CaptureItem latest)
    {
        _entries = CaptureGalleryModels.BuildItems(captures, latest);
        _selectedEntry = _entries.FirstOrDefault(entry => entry.IsSelected);
        ReflowRows(force: true);
        CaptureCountText.Text = latest.IsPrivate
            ? "Private capture · Temporary"
            : $"{_entries.Count} screenshots · Newest first";
        if (CaptureRows.Items.Count > 0)
        {
            CaptureRows.ScrollIntoView(CaptureRows.Items[0]);
        }

        RestartAutoDismiss();
    }

    private void ReflowRows(bool force = false)
    {
        var columns = Math.Max(1, (int)(((ActualWidth > 0 ? ActualWidth : Width) - 36) / 156));
        if (force || columns != GalleryColumns)
        {
            GalleryColumns = columns;
            CaptureRows.ItemsSource = _entries.Chunk(columns).ToArray();
        }
    }

    private void PauseAutoDismiss()
    {
        _dismissRevision++;
        _dismissTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        Opacity = 1d;
    }

    private void RestartAutoDismiss()
    {
        PauseAutoDismiss();
        if (!_closed && IsLoaded && !IsMouseOver && !_keyboardInteraction && _dismissTimer.Interval > TimeSpan.Zero)
        {
            _dismissTimer.Start();
        }
    }

    private void DismissTimer_Tick(object? sender, EventArgs e)
    {
        _dismissTimer.Stop();
        if (IsMouseOver || _keyboardInteraction || _closed)
        {
            return;
        }

        var revision = _dismissRevision;
        var fade = new DoubleAnimation(1d, 0d, new Duration(CaptureTaskAutoDismiss.FadeDuration));
        fade.Completed += (_, _) =>
        {
            // Hovering or another capture can cancel a fade whose completion is already queued.
            if (!_closed && revision == _dismissRevision && !IsMouseOver && !_keyboardInteraction)
            {
                Close();
            }
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CaptureGalleryEntry entry })
        {
            if (_selectedEntry is not null) _selectedEntry.IsSelected = false;
            entry.IsSelected = true;
            _selectedEntry = entry;
        }
    }

    private void Capture_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            e.Handled = true;
            OpenCapture(sender);
        }
    }

    private void Capture_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            OpenCapture(sender);
        }
    }

    private void OpenCapture(object sender)
    {
        if (sender is FrameworkElement { DataContext: CaptureGalleryEntry entry })
        {
            Close();
            CaptureRequested?.Invoke(this, entry.Item);
        }
    }

    private void OpenLibrary_Click(object sender, RoutedEventArgs e)
    {
        Close();
        LibraryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed class CaptureGalleryThumbnailConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CaptureItem item)
        {
            return null;
        }

        foreach (var path in new[] { item.ThumbnailPath, item.FilePath }.Distinct())
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 280;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception exception) when (CaptureFeedbackPolicy.IsRecoverableClipboardCopyFailure(exception))
            {
                // A stale or damaged thumbnail must not prevent capture feedback or history access.
            }
        }

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}
