using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace GoatShot.App.Windows;

public partial class ProofSceneWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private bool _dragging;
    private WpfPoint _dragOffset;

    public ProofSceneWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _timer.Tick += Timer_Tick;
        Loaded += ProofSceneWindow_Loaded;
        Closed += ProofSceneWindow_Closed;
    }

    private void ProofSceneWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _timer.Start();
        UpdateStatus("Proof scene ready");
        UpdateDragPosition();
    }

    private void ProofSceneWindow_Closed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var elapsed = DateTimeOffset.Now - _startedAt;
        TimerText.Text = elapsed.ToString(@"mm\:ss\.f", CultureInfo.InvariantCulture);
        MotionProgress.Value = elapsed.TotalSeconds % 10 * 10;
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        _timer.Start();
        UpdateStatus("Proof timer running");
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        UpdateStatus("Proof timer paused");
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        TimerText.Text = "00:00.0";
        MotionProgress.Value = 0;
        UpdateStatus("Proof timer reset");
    }

    private void DragTarget_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragOffset = e.GetPosition(DragTarget);
        DragTarget.Focus();
        DragTarget.CaptureMouse();
        UpdateStatus("Drag target selected");
        e.Handled = true;
    }

    private void DragTarget_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var point = e.GetPosition(DragCanvas);
        MoveTarget(point.X - _dragOffset.X, point.Y - _dragOffset.Y);
        e.Handled = true;
    }

    private void DragTarget_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        DragTarget.ReleaseMouseCapture();
        UpdateStatus("Drag target released");
        e.Handled = true;
    }

    private void DragTarget_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 16 : 4;
        var left = GetTargetLeft();
        var top = GetTargetTop();

        switch (e.Key)
        {
            case Key.Left:
                MoveTarget(left - step, top);
                e.Handled = true;
                break;
            case Key.Right:
                MoveTarget(left + step, top);
                e.Handled = true;
                break;
            case Key.Up:
                MoveTarget(left, top - step);
                e.Handled = true;
                break;
            case Key.Down:
                MoveTarget(left, top + step);
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Space:
                UpdateStatus("Drag target confirmed");
                e.Handled = true;
                break;
        }
    }

    private void MoveTarget(double left, double top)
    {
        var maxLeft = Math.Max(0, DragCanvas.ActualWidth - DragTarget.ActualWidth);
        var maxTop = Math.Max(0, DragCanvas.ActualHeight - DragTarget.ActualHeight);
        var nextLeft = Math.Clamp(left, 0, maxLeft);
        var nextTop = Math.Clamp(top, 0, maxTop);
        Canvas.SetLeft(DragTarget, nextLeft);
        Canvas.SetTop(DragTarget, nextTop);
        UpdateDragPosition();
        UpdateStatus("Drag target moved");
    }

    private double GetTargetLeft()
    {
        var value = Canvas.GetLeft(DragTarget);
        return double.IsNaN(value) ? 0 : value;
    }

    private double GetTargetTop()
    {
        var value = Canvas.GetTop(DragTarget);
        return double.IsNaN(value) ? 0 : value;
    }

    private void UpdateDragPosition()
    {
        DragPositionText.Text = string.Format(
            CultureInfo.InvariantCulture,
            "x{0:0} y{1:0}",
            GetTargetLeft(),
            GetTargetTop());
    }

    private void UpdateStatus(string message)
    {
        StatusText.Text = message;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
