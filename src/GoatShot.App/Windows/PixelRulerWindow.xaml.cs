using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Forms = System.Windows.Forms;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace GoatShot.App.Windows;

public partial class PixelRulerWindow : Window
{
    private WpfPoint? _startCanvasPoint;
    private WpfPoint? _endCanvasPoint;
    private ScreenPoint? _startScreenPoint;
    private ScreenPoint? _endScreenPoint;
    private bool _locked;

    public PixelRulerWindow()
    {
        InitializeComponent();

        var bounds = Forms.SystemInformation.VirtualScreen;
        Left = bounds.Left;
        Top = bounds.Top;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Activate();
        Focus();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Right)
        {
            Close();
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var canvasPoint = e.GetPosition(RulerCanvas);
        var screenPoint = GetCursorScreenPoint();

        if (_startCanvasPoint is null || _locked)
        {
            _startCanvasPoint = canvasPoint;
            _startScreenPoint = screenPoint;
            _endCanvasPoint = canvasPoint;
            _endScreenPoint = screenPoint;
            _locked = false;
            UpdateOverlay();
            return;
        }

        _endCanvasPoint = canvasPoint;
        _endScreenPoint = screenPoint;
        _locked = true;
        UpdateOverlay();
    }

    private void Window_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_startCanvasPoint is null || _locked)
        {
            return;
        }

        _endCanvasPoint = e.GetPosition(RulerCanvas);
        _endScreenPoint = GetCursorScreenPoint();
        UpdateOverlay();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.R:
                Reset();
                break;
            case Key.C:
                CopyMeasurement();
                break;
        }
    }

    private void Reset()
    {
        _startCanvasPoint = null;
        _endCanvasPoint = null;
        _startScreenPoint = null;
        _endScreenPoint = null;
        _locked = false;
        MeasureLine.Visibility = Visibility.Collapsed;
        StartMarker.Visibility = Visibility.Collapsed;
        EndMarker.Visibility = Visibility.Collapsed;
        InfoBubble.Visibility = Visibility.Collapsed;
    }

    private void UpdateOverlay()
    {
        if (_startCanvasPoint is not { } start || _endCanvasPoint is not { } end)
        {
            Reset();
            return;
        }

        MeasureLine.X1 = start.X;
        MeasureLine.Y1 = start.Y;
        MeasureLine.X2 = end.X;
        MeasureLine.Y2 = end.Y;
        MeasureLine.Visibility = Visibility.Visible;

        PlaceMarker(StartMarker, start);
        PlaceMarker(EndMarker, end);
        StartMarker.Visibility = Visibility.Visible;
        EndMarker.Visibility = Visibility.Visible;

        if (_startScreenPoint is not { } screenStart || _endScreenPoint is not { } screenEnd)
        {
            return;
        }

        var dx = screenEnd.X - screenStart.X;
        var dy = screenEnd.Y - screenStart.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        var angle = Math.Atan2(dy, dx) * 180d / Math.PI;

        DistanceText.Text = $"{distance:0.#} px";
        DeltaText.Text = $"box {Math.Abs(dx)} x {Math.Abs(dy)} px | dx {FormatSigned(dx)}, dy {FormatSigned(dy)} | {angle:0.#} deg";
        InfoBubble.Visibility = Visibility.Visible;
        PlaceInfoBubble(start, end);
    }

    private void CopyMeasurement()
    {
        if (_startScreenPoint is not { } start || _endScreenPoint is not { } end)
        {
            return;
        }

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        var angle = Math.Atan2(dy, dx) * 180d / Math.PI;
        System.Windows.Clipboard.SetText(
            $"distance={distance:0.#}px; box={Math.Abs(dx)}x{Math.Abs(dy)}px; dx={FormatSigned(dx)}; dy={FormatSigned(dy)}; angle={angle:0.#}deg");
    }

    private void PlaceInfoBubble(WpfPoint start, WpfPoint end)
    {
        var width = InfoBubble.ActualWidth > 0 ? InfoBubble.ActualWidth : 230d;
        var height = InfoBubble.ActualHeight > 0 ? InfoBubble.ActualHeight : 54d;
        var x = ((start.X + end.X) / 2d) + 14d;
        var y = ((start.Y + end.Y) / 2d) + 14d;

        Canvas.SetLeft(InfoBubble, Clamp(x, 8d, Math.Max(8d, ActualWidth - width - 8d)));
        Canvas.SetTop(InfoBubble, Clamp(y, 8d, Math.Max(8d, ActualHeight - height - 8d)));
    }

    private static void PlaceMarker(FrameworkElement marker, WpfPoint point)
    {
        Canvas.SetLeft(marker, point.X - (marker.Width / 2d));
        Canvas.SetTop(marker, point.Y - (marker.Height / 2d));
    }

    private static string FormatSigned(int value)
    {
        return value >= 0 ? $"+{value}" : value.ToString();
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Min(Math.Max(value, min), max);
    }

    private static ScreenPoint GetCursorScreenPoint()
    {
        return GetCursorPos(out var point)
            ? new ScreenPoint(point.X, point.Y)
            : new ScreenPoint(0, 0);
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private readonly record struct ScreenPoint(int X, int Y);
}
