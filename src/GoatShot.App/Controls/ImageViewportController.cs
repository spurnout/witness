using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfPoint = System.Windows.Point;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using Cursors = System.Windows.Input.Cursors;

namespace GoatShot.App.Controls;

/// <summary>Zooms an outer container, leaving the image's pixel-space drawing and export surface unchanged.</summary>
internal sealed class ImageViewportController
{
    private readonly ScrollViewer _viewport;
    private readonly FrameworkElement _container;
    private readonly ScaleTransform _transform = new(1, 1);
    private bool _fit = true;
    private bool _updating;
    private WpfPoint? _panStart;
    private WpfPoint _panOffset;

    public ImageViewportController(ScrollViewer viewport, FrameworkElement container)
    {
        _viewport = viewport;
        _container = container;
        container.LayoutTransform = _transform;
        viewport.Loaded += (_, _) => Fit();
        viewport.SizeChanged += (_, _) => { if (_fit) Fit(); };
        viewport.ScrollChanged += (_, e) =>
        {
            if (!_updating && (e.HorizontalChange != 0 || e.VerticalChange != 0))
            {
                ViewChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        viewport.PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            var pointer = e.GetPosition(viewport);
            var insetX = container.HorizontalAlignment == System.Windows.HorizontalAlignment.Center
                ? Math.Max(0, (viewport.ViewportWidth - container.ActualWidth * Scale) / 2) : 0;
            var insetY = container.VerticalAlignment == VerticalAlignment.Center
                ? Math.Max(0, (viewport.ViewportHeight - container.ActualHeight * Scale) / 2) : 0;
            var imagePoint = new WpfPoint((viewport.HorizontalOffset + pointer.X - insetX) / Scale,
                (viewport.VerticalOffset + pointer.Y - insetY) / Scale);
            SetScale(Scale * (e.Delta > 0 ? 1.25 : 0.8), false, imagePoint, pointer);
            e.Handled = true;
        };
        viewport.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle && !(PanEnabled && e.ChangedButton == MouseButton.Left)) return;
            for (var target = e.OriginalSource as DependencyObject; target is not null && target != viewport;
                 target = target is Visual ? VisualTreeHelper.GetParent(target) : LogicalTreeHelper.GetParent(target))
            {
                if (target is System.Windows.Controls.Primitives.ScrollBar) return;
            }
            _panStart = e.GetPosition(viewport);
            _panOffset = new WpfPoint(viewport.HorizontalOffset, viewport.VerticalOffset);
            viewport.CaptureMouse();
            viewport.Cursor = Cursors.Hand;
            e.Handled = true;
        };
        viewport.PreviewMouseMove += Pan;
        viewport.PreviewMouseUp += (_, e) =>
        {
            if (_panStart is null || e.ChangedButton is not (MouseButton.Left or MouseButton.Middle)) return;
            _panStart = null;
            viewport.ReleaseMouseCapture();
            viewport.Cursor = PanEnabled ? Cursors.Hand : null;
            e.Handled = true;
        };
        viewport.LostMouseCapture += (_, _) =>
        {
            _panStart = null;
            viewport.Cursor = PanEnabled ? Cursors.Hand : null;
        };
    }

    public double Scale => _transform.ScaleX;
    public bool IsFit => _fit;
    public bool PanEnabled { get; set; }
    public event EventHandler? ViewChanged;

    public WpfPoint RelativeCenter => new(
        Math.Clamp((_viewport.HorizontalOffset + Math.Min(_viewport.ViewportWidth, _container.ActualWidth * Scale) / 2) / Math.Max(1, _container.ActualWidth * Scale), 0, 1),
        Math.Clamp((_viewport.VerticalOffset + Math.Min(_viewport.ViewportHeight, _container.ActualHeight * Scale) / 2) / Math.Max(1, _container.ActualHeight * Scale), 0, 1));

    public void Fit()
    {
        var availableWidth = Math.Max(1, _viewport.ActualWidth - SystemParameters.VerticalScrollBarWidth - 2);
        var availableHeight = Math.Max(1, _viewport.ActualHeight - SystemParameters.HorizontalScrollBarHeight - 2);
        SetScale(FitScale(_container.ActualWidth, _container.ActualHeight, availableWidth, availableHeight), true);
    }

    internal static double FitScale(double width, double height, double viewportWidth, double viewportHeight) =>
        Math.Min(1, Math.Min(Math.Max(1, viewportWidth) / Math.Max(1, width), Math.Max(1, viewportHeight) / Math.Max(1, height)));

    public void Zoom(double scale) => SetScale(scale, false);

    public void ApplyView(double scale, WpfPoint center)
    {
        SetScale(scale, false, new WpfPoint(center.X * _container.ActualWidth, center.Y * _container.ActualHeight));
    }

    private void SetScale(double scale, bool fit, WpfPoint? imagePoint = null, WpfPoint? anchor = null)
    {
        if (_updating) return;
        _updating = true;
        try
        {
            var center = imagePoint ?? new WpfPoint(
                RelativeCenter.X * _container.ActualWidth,
                RelativeCenter.Y * _container.ActualHeight);
            _fit = fit;
            // Fit must also work for very large captures; manual zoom stays bounded.
            var next = fit ? Math.Clamp(scale, 0.001, 1) : Math.Clamp(scale, 0.05, 8);
            _transform.ScaleX = _transform.ScaleY = next;
            _viewport.UpdateLayout();
            _viewport.ScrollToHorizontalOffset(fit ? 0 : center.X * next - (anchor?.X ?? _viewport.ViewportWidth / 2));
            _viewport.ScrollToVerticalOffset(fit ? 0 : center.Y * next - (anchor?.Y ?? _viewport.ViewportHeight / 2));
            _viewport.UpdateLayout();
        }
        finally
        {
            _updating = false;
        }
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Pan(object sender, WpfMouseEventArgs e)
    {
        if (_panStart is not { } start) return;
        var point = e.GetPosition(_viewport);
        _viewport.ScrollToHorizontalOffset(_panOffset.X + start.X - point.X);
        _viewport.ScrollToVerticalOffset(_panOffset.Y + start.Y - point.Y);
        e.Handled = true;
    }
}
