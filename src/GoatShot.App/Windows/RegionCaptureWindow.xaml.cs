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
    private readonly bool _hoverAutoSelectEnabled;
    private readonly Func<CaptureOverlayTarget, IReadOnlyList<CaptureOverlayTarget>> _childTargetProvider;
    // Keyed by target id, not native handle: synthetic targets all share handle zero, and ids are
    // unique for live windows too.
    private readonly Dictionary<string, IReadOnlyList<CaptureOverlayTarget>> _childTargetCache = new(StringComparer.Ordinal);
    private WpfPoint? _start;
    private WpfPoint _lastHoverPosition;
    private CaptureOverlaySelection? _lastSelection;
    private CaptureOverlayTarget? _hoverTarget;
    private string? _lensColorHex;

    public RegionCaptureWindow(
        BitmapSource frozenScreen,
        int contextPadding = 0,
        IReadOnlyList<CaptureOverlayTarget>? targets = null,
        CaptureBounds? virtualBounds = null,
        bool enableHoverAutoSelect = true,
        Func<CaptureOverlayTarget, IReadOnlyList<CaptureOverlayTarget>>? childTargetProvider = null)
    {
        InitializeComponent();

        _virtualBounds = virtualBounds ?? CaptureOverlayTargetCatalog.GetVirtualScreenBounds();
        _targets = targets ?? CaptureOverlayTargetCatalog.BuildLiveTargets();
        _contextPadding = Math.Clamp(contextPadding, 0, CaptureOverlayGeometry.MaxContextPadding);
        _hoverAutoSelectEnabled = enableHoverAutoSelect;
        _childTargetProvider = childTargetProvider ?? (window => CaptureOverlayTargetCatalog.BuildChildTargets(window));

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

            // Seed the hover from the real cursor position so the window under it is highlighted
            // the moment the overlay opens, and so a Ctrl/Shift press that arrives before the
            // first mouse move refreshes against the true position instead of (0,0).
            UpdateHover(Mouse.GetPosition(Root));
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
        HoverRectangle.Visibility = Visibility.Collapsed;
        Root.CaptureMouse();
        UpdateSelection(_start.Value, _start.Value);
    }

    private void Root_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_start is not WpfPoint start || e.LeftButton != MouseButtonState.Pressed)
        {
            UpdateHover(e.GetPosition(Root));
            return;
        }

        UpdateSelection(start, e.GetPosition(Root));
    }

    /// <summary>
    /// Resolves what a click would capture and draws it. Control drill-down is scoped to the
    /// hovered window so the expensive child enumeration only ever runs for one window at a time.
    /// </summary>
    private void UpdateHover(WpfPoint position)
    {
        _lastHoverPosition = position;

        // The lens follows plain pointing as well as dragging, so the overlay doubles as a pixel
        // inspector: coordinates and color track the cursor even before any selection starts.
        UpdateLens(position);

        var mode = ResolveHoverMode();
        if (mode == CaptureOverlayHoverMode.Off)
        {
            ClearHover();
            return;
        }

        var screenX = (int)Math.Round(_virtualBounds.X + position.X);
        var screenY = (int)Math.Round(_virtualBounds.Y + position.Y);
        var target = CaptureOverlayGeometry.ResolveHoverTarget(
            screenX,
            screenY,
            _targets,
            CaptureOverlayHoverMode.Window);

        if (target is not null &&
            mode == CaptureOverlayHoverMode.Control &&
            target.Kind == CaptureOverlayTargetKind.Window)
        {
            var scoped = new List<CaptureOverlayTarget> { target };
            scoped.AddRange(GetChildTargets(target));
            target = CaptureOverlayGeometry.ResolveHoverTarget(
                screenX,
                screenY,
                scoped,
                CaptureOverlayHoverMode.Control) ?? target;
        }

        if (target is null)
        {
            ClearHover();
            return;
        }

        _hoverTarget = target;
        DrawHover(ResolveTargetSelection(target));
    }

    private CaptureOverlayHoverMode ResolveHoverMode()
    {
        if (!_hoverAutoSelectEnabled || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return CaptureOverlayHoverMode.Off;
        }

        return Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            ? CaptureOverlayHoverMode.Control
            : CaptureOverlayHoverMode.Window;
    }

    private IReadOnlyList<CaptureOverlayTarget> GetChildTargets(CaptureOverlayTarget window)
    {
        if (_childTargetCache.TryGetValue(window.Id, out var cached))
        {
            return cached;
        }

        var children = _childTargetProvider(window);
        _childTargetCache[window.Id] = children;
        return children;
    }

    private void DrawHover(CaptureOverlaySelection selection)
    {
        var bounds = selection.FinalBounds;
        var left = bounds.X - _virtualBounds.X;
        var top = bounds.Y - _virtualBounds.Y;

        Canvas.SetLeft(HoverRectangle, left);
        Canvas.SetTop(HoverRectangle, top);
        HoverRectangle.Width = bounds.Width;
        HoverRectangle.Height = bounds.Height;
        HoverRectangle.Visibility = Visibility.Visible;

        SizeText.Text = $"{bounds.Width} x {bounds.Height}";
        SelectionHintText.Text = selection.StatusText;
        Canvas.SetLeft(SizeBadge, left);
        Canvas.SetTop(SizeBadge, Math.Max(0, top - 54));
        SizeBadge.Visibility = Visibility.Visible;
    }

    private void ClearHover()
    {
        _hoverTarget = null;
        HoverRectangle.Visibility = Visibility.Collapsed;
        if (_lastSelection is null)
        {
            SizeBadge.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Runs a whole target through the drag geometry so padding and status text match.</summary>
    private CaptureOverlaySelection ResolveTargetSelection(CaptureOverlayTarget target)
    {
        var bounds = target.Bounds;
        return CaptureOverlayGeometry.ResolveSelection(
            bounds.X,
            bounds.Y,
            bounds.X + bounds.Width,
            bounds.Y + bounds.Height,
            new CaptureOverlayGeometryOptions(
                _virtualBounds,
                [target],
                CaptureOverlayGeometry.DefaultSnapThreshold,
                _contextPadding));
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
            // A click with no drag captures whatever the hover highlight was showing. From here on
            // only Esc cancels, so a stray click no longer throws the capture away.
            if (_hoverTarget is { } hovered)
            {
                SelectedBounds = ResolveTargetSelection(hovered).FinalBounds;
                DialogResult = true;
                return;
            }

            DialogResult = false;
            return;
        }

        SelectedBounds = selection.FinalBounds;
        DialogResult = true;
    }

    private void Window_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift)
        {
            RefreshHover();
            return;
        }

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

        // The interactive-element guard keeps typing "C" into the window chooser's search from
        // silently overwriting the clipboard with a color.
        if (e.Key == Key.C &&
            !IsFromInteractiveElement(e.OriginalSource) &&
            LensBorder.Visibility == Visibility.Visible &&
            _lensColorHex is { } hex)
        {
            CopyLensColor(hex);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            MoveOrResizeKeyboardSelection(e.Key, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        }
    }

    private void Window_KeyUp(object sender, WpfKeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift)
        {
            RefreshHover();
        }
    }

    /// <summary>Re-resolves the highlight after a modifier change, without needing a mouse move.</summary>
    private void RefreshHover()
    {
        if (_start is not null && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            return;
        }

        UpdateHover(_lastHoverPosition);
    }

    /// <summary>
    /// Feedback lands in the lens itself because it is the one surface guaranteed to be visible
    /// and next to the cursor when the copy happens. The next mouse move restores the readout.
    /// </summary>
    private void CopyLensColor(string hex)
    {
        try
        {
            ClipboardInterop.SetText(hex);
            LensText.Text = $"Copied {hex}";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            LensText.Text = "Clipboard is busy";
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
        _lensColorHex = CaptureOverlayColorReader.TryReadHex(source, crop.PixelX, crop.PixelY);
        LensText.Text = _lensColorHex is null
            ? $"{screenX}, {screenY}"
            : $"{screenX}, {screenY}  {_lensColorHex}";
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
        HoverRectangle.Visibility = Visibility.Collapsed;

        var selection = ResolveTargetSelection(target);
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
