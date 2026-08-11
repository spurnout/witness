using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using GoatShot.App.Models;
using GoatShot.App.Services;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace GoatShot.App.Windows;

public partial class RegionCaptureWindow : Window
{
    private const double LensWidth = 164;
    private const double LensHeight = 188;
    private const double LensMargin = 18;

    private readonly CaptureBounds _virtualBounds;
    private readonly IReadOnlyList<CaptureOverlayTarget> _targets;
    private readonly int _contextPadding;
    private WpfPoint? _start;
    private CaptureOverlaySelection? _lastSelection;

    public RegionCaptureWindow(
        BitmapSource frozenScreen,
        int contextPadding = 0,
        IReadOnlyList<CaptureOverlayTarget>? targets = null,
        CaptureBounds? virtualBounds = null)
    {
        InitializeComponent();

        _virtualBounds = virtualBounds ?? CaptureOverlayTargetCatalog.GetVirtualScreenBounds();
        _targets = targets ?? CaptureOverlayTargetCatalog.BuildLiveTargets();
        _contextPadding = Math.Clamp(contextPadding, 0, CaptureOverlayGeometry.MaxContextPadding);

        Left = _virtualBounds.X;
        Top = _virtualBounds.Y;
        Width = _virtualBounds.Width;
        Height = _virtualBounds.Height;

        FrozenScreen.Source = frozenScreen;
        LoadChooserTargets();
        Loaded += (_, _) =>
        {
            Root.Focus();
            var width = Math.Min(640d, Math.Max(1d, ActualWidth * 0.5d));
            var height = Math.Min(480d, Math.Max(1d, ActualHeight * 0.5d));
            SetPreviewSelection(
                Math.Max(0d, (ActualWidth - width) / 2d),
                Math.Max(0d, (ActualHeight - height) / 2d),
                width,
                height);
        };
    }

    public CaptureBounds? SelectedBounds { get; private set; }

    public void SetPreviewSelection(double left, double top, double width, double height)
    {
        SelectionRectangle.Visibility = Visibility.Visible;
        SizeBadge.Visibility = Visibility.Visible;
        UpdateSelection(
            new WpfPoint(left, top),
            new WpfPoint(left + Math.Max(1, width), top + Math.Max(1, height)));
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsFromInteractiveElement(e.OriginalSource))
        {
            return;
        }

        _start = e.GetPosition(Root);
        SelectionRectangle.Visibility = Visibility.Visible;
        SizeBadge.Visibility = Visibility.Visible;
        LensBorder.Visibility = Visibility.Visible;
        Root.CaptureMouse();
        UpdateSelection(_start.Value, _start.Value);
    }

    private void Root_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_start is not WpfPoint start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        UpdateSelection(start, e.GetPosition(Root));
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_start is not WpfPoint start)
        {
            return;
        }

        Root.ReleaseMouseCapture();
        var end = e.GetPosition(Root);
        UpdateSelection(start, end);
        var selection = _lastSelection;
        if (selection is null || selection.RawBounds.Width < 3 || selection.RawBounds.Height < 3)
        {
            DialogResult = false;
            return;
        }

        SelectedBounds = selection.FinalBounds;
        DialogResult = true;
    }

    private void Window_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && _lastSelection is not null)
        {
            SelectedBounds = _lastSelection.FinalBounds;
            DialogResult = true;
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            MoveOrResizeKeyboardSelection(e.Key, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
    }

    private void MoveOrResizeKeyboardSelection(Key key, bool resize)
    {
        var current = _lastSelection?.FinalBounds ?? new CaptureBounds
        {
            X = _virtualBounds.X,
            Y = _virtualBounds.Y,
            Width = Math.Min(640, _virtualBounds.Width),
            Height = Math.Min(480, _virtualBounds.Height)
        };
        const int step = 10;
        var dx = key == Key.Left ? -step : key == Key.Right ? step : 0;
        var dy = key == Key.Up ? -step : key == Key.Down ? step : 0;
        var left = current.X - _virtualBounds.X;
        var top = current.Y - _virtualBounds.Y;
        var right = left + current.Width;
        var bottom = top + current.Height;
        if (resize)
        {
            right = Math.Clamp(right + dx, left + 3, _virtualBounds.Width);
            bottom = Math.Clamp(bottom + dy, top + 3, _virtualBounds.Height);
        }
        else
        {
            left = Math.Clamp(left + dx, 0, Math.Max(0, _virtualBounds.Width - current.Width));
            top = Math.Clamp(top + dy, 0, Math.Max(0, _virtualBounds.Height - current.Height));
            right = left + current.Width;
            bottom = top + current.Height;
        }

        SetPreviewSelection(left, top, right - left, bottom - top);
    }

    private void UpdateSelection(WpfPoint start, WpfPoint end)
    {
        var options = BuildGeometryOptions(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        var selection = CaptureOverlayGeometry.ResolveSelection(
            _virtualBounds.X + start.X,
            _virtualBounds.Y + start.Y,
            _virtualBounds.X + end.X,
            _virtualBounds.Y + end.Y,
            options);
        _lastSelection = selection;
        DrawSelection(selection);
        UpdateLens(end);
    }

    private void DrawSelection(CaptureOverlaySelection selection)
    {
        var bounds = selection.FinalBounds;
        var left = bounds.X - _virtualBounds.X;
        var top = bounds.Y - _virtualBounds.Y;
        var width = bounds.Width;
        var height = bounds.Height;

        Canvas.SetLeft(SelectionRectangle, left);
        Canvas.SetTop(SelectionRectangle, top);
        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;

        SizeText.Text = $"{width} x {height}";
        SelectionHintText.Text = selection.StatusText;
        Canvas.SetLeft(SizeBadge, left);
        Canvas.SetTop(SizeBadge, Math.Max(0, top - 54));
    }

    private CaptureOverlayGeometryOptions BuildGeometryOptions(bool ignoreSnap)
    {
        return new CaptureOverlayGeometryOptions(
            _virtualBounds,
            ignoreSnap ? Array.Empty<CaptureOverlayTarget>() : _targets,
            ignoreSnap ? 0 : CaptureOverlayGeometry.DefaultSnapThreshold,
            _contextPadding);
    }

    private void UpdateLens(WpfPoint current)
    {
        if (FrozenScreen.Source is not BitmapSource source)
        {
            LensBorder.Visibility = Visibility.Collapsed;
            return;
        }

        var screenX = (int)Math.Round(_virtualBounds.X + current.X);
        var screenY = (int)Math.Round(_virtualBounds.Y + current.Y);
        var crop = CaptureOverlayGeometry.ResolveLensCrop(
            _virtualBounds,
            screenX,
            screenY,
            source.PixelWidth,
            source.PixelHeight);

        LensImage.Source = new CroppedBitmap(
            source,
            new Int32Rect(crop.X, crop.Y, crop.Width, crop.Height));
        LensText.Text = $"{screenX}, {screenY}";
        LensBorder.Visibility = Visibility.Visible;

        var left = current.X + LensMargin;
        if (left + LensWidth > ActualWidth)
        {
            left = current.X - LensWidth - LensMargin;
        }

        var top = current.Y + LensMargin;
        if (top + LensHeight > ActualHeight)
        {
            top = current.Y - LensHeight - LensMargin;
        }

        Canvas.SetLeft(LensBorder, Math.Clamp(left, 0, Math.Max(0, ActualWidth - LensWidth)));
        Canvas.SetTop(LensBorder, Math.Clamp(top, 0, Math.Max(0, ActualHeight - LensHeight)));
    }

    private void LoadChooserTargets()
    {
        var chooserTargets = _targets
            .Where(target => target.ShowInChooser)
            .Take(40)
            .ToList();
        TargetChooserBox.ItemsSource = chooserTargets;
        TargetChooserBox.Text = chooserTargets.Count > 0
            ? "Choose window or monitor..."
            : "No window targets found";
        TargetChooserBox.Visibility = chooserTargets.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TargetChooser_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TargetChooserBox.SelectedItem is not CaptureOverlayTarget target)
        {
            return;
        }

        PreviewTarget(target);
    }

    private void CaptureTarget_Click(object sender, RoutedEventArgs e)
    {
        if (TargetChooserBox.SelectedItem is not CaptureOverlayTarget target)
        {
            return;
        }

        var selection = PreviewTarget(target);
        SelectedBounds = selection.FinalBounds;
        DialogResult = true;
    }

    private CaptureOverlaySelection PreviewTarget(CaptureOverlayTarget target)
    {
        SelectionRectangle.Visibility = Visibility.Visible;
        SizeBadge.Visibility = Visibility.Visible;
        LensBorder.Visibility = Visibility.Collapsed;

        var bounds = target.Bounds;
        var selection = CaptureOverlayGeometry.ResolveSelection(
            bounds.X,
            bounds.Y,
            bounds.X + bounds.Width,
            bounds.Y + bounds.Height,
            new CaptureOverlayGeometryOptions(_virtualBounds, [target], CaptureOverlayGeometry.DefaultSnapThreshold, _contextPadding));
        _lastSelection = selection;
        DrawSelection(selection);
        return selection;
    }

    private static bool IsFromInteractiveElement(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase ||
                current is System.Windows.Controls.ComboBox ||
                current is System.Windows.Controls.TextBox ||
                current is System.Windows.Controls.ScrollViewer)
            {
                return true;
            }

            current = GetParent(current);
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        try
        {
            if (current is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                var visualParent = VisualTreeHelper.GetParent(current);
                if (visualParent is not null)
                {
                    return visualParent;
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Some WPF helper objects are DependencyObjects but not visuals.
        }

        return LogicalTreeHelper.GetParent(current) as DependencyObject;
    }
}
