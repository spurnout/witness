using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using GoatShot.App.Models;
using GoatShot.App.Services;
using Microsoft.Win32;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfImage = System.Windows.Controls.Image;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPath = System.Windows.Shapes.Path;
using WpfPoint = System.Windows.Point;
using WpfPolyline = System.Windows.Shapes.Polyline;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfSize = System.Windows.Size;

namespace GoatShot.App.Windows;

public partial class EditorWindow : Window
{
    private readonly CaptureItem _item;
    private readonly AppServices _services;
    private readonly List<UIElement> _history = new();
    private readonly List<UIElement> _redoHistory = new();
    private AnnotationMode _mode = AnnotationMode.Rectangle;
    private WpfPoint? _start;
    private UIElement? _activeElement;
    private WpfRectangle? _cropElement;
    private Rect? _cropRect;
    private int _step = 1;
    private readonly List<UIElement> _sensitiveReviewOverlays = new();

    public EditorWindow(CaptureItem item, AppServices services)
    {
        _item = item;
        _services = services;
        InitializeComponent();
        WpfAccessibilityNameHelper.ApplyGeneratedNames(this);
        LoadImage();
        InitializeToolShortcutHints();
        SetActiveToolFeedback();
    }

    // Surface each tool's keyboard shortcut in its tooltip and accessible name so the
    // shortcuts defined in EditorToolCatalog are discoverable without reading source.
    private void InitializeToolShortcutHints()
    {
        foreach (var button in ToolButtons())
        {
            if (button.Tag is not string tag ||
                !Enum.TryParse<AnnotationMode>(tag, out var mode))
            {
                continue;
            }

            var shortcut = EditorToolCatalog.Shortcuts
                .FirstOrDefault(entry => entry.Mode == mode)?.Shortcut;
            if (string.IsNullOrEmpty(shortcut))
            {
                continue;
            }

            var badge = $" ({shortcut})";
            var name = AutomationProperties.GetName(button);
            if (!string.IsNullOrEmpty(name) && !name.EndsWith(badge, StringComparison.Ordinal))
            {
                AutomationProperties.SetName(button, name + badge);
            }

            if (button.ToolTip is string tip && !tip.EndsWith(badge, StringComparison.Ordinal))
            {
                button.ToolTip = tip + badge;
            }
        }
    }

    public event EventHandler<CaptureItem>? CaptureSaved;

    public void SelectTool(AnnotationMode mode)
    {
        _mode = mode;
        SetActiveToolFeedback();
        ToolStatus.Text = $"{EditorToolCatalog.LabelFor(mode)} tool active.";
    }

    private void LoadImage()
    {
        var source = ImageInterop.LoadBitmapImage(_item.FilePath);
        BaseImage.Source = source;
        BaseImage.Width = source.PixelWidth;
        BaseImage.Height = source.PixelHeight;
        AnnotationCanvas.Width = source.PixelWidth;
        AnnotationCanvas.Height = source.PixelHeight;
        EditorSurface.Width = source.PixelWidth;
        EditorSurface.Height = source.PixelHeight;
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: string tag } &&
            Enum.TryParse<AnnotationMode>(tag, out var mode))
        {
            SelectTool(mode);
        }
    }

    private void SetActiveToolFeedback()
    {
        ActiveToolText.Text = _mode.ToString();
        foreach (var button in ToolButtons())
        {
            if (button.Tag is not string tag ||
                !Enum.TryParse<AnnotationMode>(tag, out var buttonMode))
            {
                continue;
            }

            var active = buttonMode == _mode;
            button.ClearValue(BackgroundProperty);
            button.ClearValue(BorderBrushProperty);
            button.ClearValue(BorderThicknessProperty);
            button.ClearValue(ForegroundProperty);
            // Expose the selected/not-selected state to assistive tech since these are
            // plain buttons acting as a single-select tool group, not toggle buttons.
            AutomationProperties.SetItemStatus(button, active ? "Selected" : "Not selected");
            AutomationProperties.SetHelpText(button, EditorToolCatalog.IsPrivacyTool(buttonMode)
                ? "Privacy masking editor tool"
                : "Editor annotation tool");

            if (!active)
            {
                continue;
            }

            button.BorderThickness = new Thickness(2);
            button.BorderBrush = EditorToolCatalog.IsPrivacyTool(buttonMode)
                ? TryFindResource("WarnBrush") as System.Windows.Media.Brush ?? WpfBrushes.Orange
                : TryFindResource("AccentBrush") as System.Windows.Media.Brush ?? WpfBrushes.DeepSkyBlue;
            button.Background = EditorToolCatalog.IsPrivacyTool(buttonMode)
                ? new SolidColorBrush(WpfColor.FromRgb(82, 42, 24))
                : new SolidColorBrush(WpfColor.FromRgb(25, 65, 72));
            button.Foreground = WpfBrushes.White;
            AutomationProperties.SetHelpText(button, "Active editor tool");
        }
    }

    private IEnumerable<WpfButton> ToolButtons()
    {
        yield return RectangleToolButton;
        yield return EllipseToolButton;
        yield return LineToolButton;
        yield return ArrowToolButton;
        yield return FreehandToolButton;
        yield return TextToolButton;
        yield return CalloutToolButton;
        yield return StepToolButton;
        yield return HighlightToolButton;
        yield return SpotlightToolButton;
        yield return CropToolButton;
        yield return RedactToolButton;
        yield return BlurToolButton;
        yield return PixelateToolButton;
    }

    private void EditorWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Key == Key.Z)
            {
                Undo_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.C)
            {
                Copy_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Y)
            {
                Redo_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S)
            {
                Save_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }

            return;
        }

        if (modifiers != ModifierKeys.None ||
            !EditorToolCatalog.TryGetModeForShortcut(e.Key.ToString(), out var mode))
        {
            return;
        }

        SelectTool(mode);
        EditorSurface.Focus();
        e.Handled = true;
    }

    private void UseLastAiPrompt_Click(object sender, RoutedEventArgs e)
    {
        var prompt = _services.AiHistory.LoadPromptHistory(AiActionKind.ImageEdit, 1).FirstOrDefault();
        if (prompt is null)
        {
            ToolStatus.Text = "No previous AI edit prompt in local history.";
            return;
        }

        AiPromptBox.Text = prompt.Prompt;
        AiPromptBox.Focus();
        AiPromptBox.CaretIndex = AiPromptBox.Text.Length;
        ToolStatus.Text = "Loaded the latest AI edit prompt from local history.";
    }

    private void EditorSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(AnnotationCanvas);

        if (_mode == AnnotationMode.Text)
        {
            AddText(point);
            return;
        }

        if (_mode == AnnotationMode.Callout)
        {
            AddCallout(point);
            return;
        }

        if (_mode == AnnotationMode.Step)
        {
            AddStep(point);
            return;
        }

        _start = point;
        _activeElement = CreateDragElement(point);
        AnnotationCanvas.Children.Add(_activeElement);
        AnnotationCanvas.CaptureMouse();
    }

    private void EditorSurface_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_start is not WpfPoint start || _activeElement is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(AnnotationCanvas);
        if (_activeElement is WpfPolyline polyline && _mode == AnnotationMode.Freehand)
        {
            polyline.Points.Add(current);
            return;
        }

        UpdateDragElement(_activeElement, start, current);
    }

    private void EditorSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_activeElement is null)
        {
            ResetDrag();
            return;
        }

        if (_activeElement is WpfRectangle cropRectangle && _mode == AnnotationMode.Crop)
        {
            FinalizeCrop(cropRectangle);
        }
        else if (_activeElement is WpfRectangle spotlightRectangle && _mode == AnnotationMode.Spotlight)
        {
            var spotlight = CreateSpotlightOverlay(spotlightRectangle);
            AnnotationCanvas.Children.Remove(spotlightRectangle);
            if (spotlight is not null)
            {
                AnnotationCanvas.Children.Add(spotlight);
                TrackElement(spotlight);
            }
        }
        else if (_activeElement is WpfRectangle rectangle && IsEffectMode(_mode))
        {
            var effect = CreateEffectOverlay(rectangle, _mode);
            AnnotationCanvas.Children.Remove(rectangle);
            if (effect is not null)
            {
                AnnotationCanvas.Children.Add(effect);
                TrackElement(effect);
            }
        }
        else if (_activeElement is WpfPolyline polyline)
        {
            if (polyline.Points.Count < 2)
            {
                AnnotationCanvas.Children.Remove(polyline);
            }
            else
            {
                TrackElement(polyline);
            }
        }
        else
        {
            TrackElement(_activeElement);
        }

        ResetDrag();
    }

    private UIElement CreateDragElement(WpfPoint point)
    {
        if (_mode == AnnotationMode.Freehand)
        {
            return new WpfPolyline
            {
                Stroke = WpfBrushes.DeepSkyBlue,
                StrokeThickness = 5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Points = new PointCollection { point }
            };
        }

        if (_mode is AnnotationMode.Arrow or AnnotationMode.Line)
        {
            return new Line
            {
                X1 = point.X,
                Y1 = point.Y,
                X2 = point.X,
                Y2 = point.Y,
                Stroke = WpfBrushes.DeepSkyBlue,
                StrokeThickness = 5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = _mode == AnnotationMode.Arrow
                    ? PenLineCap.Triangle
                    : PenLineCap.Round
            };
        }

        if (_mode == AnnotationMode.Ellipse)
        {
            var ellipse = new WpfEllipse
            {
                StrokeThickness = 4,
                Stroke = WpfBrushes.DeepSkyBlue,
                Fill = WpfBrushes.Transparent
            };
            Canvas.SetLeft(ellipse, point.X);
            Canvas.SetTop(ellipse, point.Y);
            return ellipse;
        }

        var isPrivacyEffect = _mode is AnnotationMode.Redact or AnnotationMode.Blur or AnnotationMode.Pixelate;
        var isHighlight = _mode == AnnotationMode.Highlight;
        var isSpotlight = _mode == AnnotationMode.Spotlight;
        var rectangle = new WpfRectangle
        {
            StrokeThickness = _mode == AnnotationMode.Redact || isHighlight ? 0 : CropStrokeThickness(),
            Stroke = isSpotlight ? new SolidColorBrush(WpfColor.FromRgb(255, 221, 74)) : CropStrokeBrush(isPrivacyEffect),
            Fill = _mode == AnnotationMode.Redact
                ? WpfBrushes.Black
                : isHighlight
                    ? new SolidColorBrush(WpfColor.FromArgb(112, 255, 221, 74))
                    : isSpotlight
                        ? new SolidColorBrush(WpfColor.FromArgb(36, 255, 221, 74))
                    : WpfBrushes.Transparent
        };
        if (_mode is AnnotationMode.Crop or AnnotationMode.Spotlight)
        {
            rectangle.StrokeDashArray = new DoubleCollection { 6, 4 };
        }

        Canvas.SetLeft(rectangle, point.X);
        Canvas.SetTop(rectangle, point.Y);
        return rectangle;
    }

    private static void UpdateDragElement(UIElement element, WpfPoint start, WpfPoint current)
    {
        if (element is Line line)
        {
            line.X2 = current.X;
            line.Y2 = current.Y;
            return;
        }

        if (element is WpfRectangle rectangle)
        {
            UpdateBoxElement(rectangle, start, current);
            return;
        }

        if (element is WpfEllipse ellipse)
        {
            UpdateBoxElement(ellipse, start, current);
        }
    }

    private static void UpdateBoxElement(FrameworkElement element, WpfPoint start, WpfPoint current)
    {
        var left = Math.Min(start.X, current.X);
        var top = Math.Min(start.Y, current.Y);
        var width = Math.Abs(current.X - start.X);
        var height = Math.Abs(current.Y - start.Y);

        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        element.Width = width;
        element.Height = height;
    }

    private System.Windows.Media.Brush CropStrokeBrush(bool isPrivacyEffect)
    {
        if (_mode == AnnotationMode.Crop)
        {
            return new SolidColorBrush(WpfColor.FromRgb(48, 230, 195));
        }

        return isPrivacyEffect ? WpfBrushes.Orange : WpfBrushes.DeepSkyBlue;
    }

    private double CropStrokeThickness()
    {
        return _mode == AnnotationMode.Crop ? 2 : 4;
    }

    private WpfImage? CreateEffectOverlay(WpfRectangle rectangle, AnnotationMode mode)
    {
        var left = Canvas.GetLeft(rectangle);
        var top = Canvas.GetTop(rectangle);
        if (double.IsNaN(left) || double.IsNaN(top) || rectangle.Width < 2 || rectangle.Height < 2)
        {
            return null;
        }

        var effectMode = mode == AnnotationMode.Pixelate
            ? VisualRedactionMode.Pixelate
            : VisualRedactionMode.Blur;
        using var bitmap = ImageRegionEffects.CreateRegionEffectBitmap(
            _item.FilePath,
            new System.Drawing.RectangleF((float)left, (float)top, (float)rectangle.Width, (float)rectangle.Height),
            effectMode);
        var source = ImageInterop.ToBitmapSource(bitmap);
        var image = new WpfImage
        {
            Source = source,
            Width = rectangle.Width,
            Height = rectangle.Height,
            Stretch = Stretch.Fill
        };
        Canvas.SetLeft(image, left);
        Canvas.SetTop(image, top);
        return image;
    }

    private static bool IsEffectMode(AnnotationMode mode)
    {
        return mode is AnnotationMode.Blur or AnnotationMode.Pixelate;
    }

    private UIElement? CreateSpotlightOverlay(WpfRectangle rectangle)
    {
        var bounds = ElementBounds(rectangle);
        if (bounds.Width < 4 || bounds.Height < 4)
        {
            return null;
        }

        var outer = new Rect(0, 0, AnnotationCanvas.Width, AnnotationCanvas.Height);
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(outer));
        geometry.Children.Add(new RectangleGeometry(bounds, 8, 8));

        var overlay = new Canvas
        {
            Width = AnnotationCanvas.Width,
            Height = AnnotationCanvas.Height,
            IsHitTestVisible = false
        };
        overlay.Children.Add(new WpfPath
        {
            Data = geometry,
            Fill = new SolidColorBrush(WpfColor.FromArgb(176, 0, 0, 0))
        });

        var border = new WpfRectangle
        {
            Width = bounds.Width,
            Height = bounds.Height,
            RadiusX = 8,
            RadiusY = 8,
            Fill = WpfBrushes.Transparent,
            Stroke = new SolidColorBrush(WpfColor.FromRgb(255, 221, 74)),
            StrokeThickness = 4
        };
        Canvas.SetLeft(border, bounds.Left);
        Canvas.SetTop(border, bounds.Top);
        overlay.Children.Add(border);
        AutomationProperties.SetName(overlay, "Spotlight annotation overlay");
        return overlay;
    }

    private void AddText(WpfPoint point)
    {
        var dialog = new InputDialog("Text to place on the screenshot")
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.ResponseText))
        {
            return;
        }

        var text = new Border
        {
            Background = new SolidColorBrush(WpfColor.FromArgb(230, 8, 16, 22)),
            BorderBrush = WpfBrushes.DeepSkyBlue,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 5),
            Child = new TextBlock
            {
                Text = dialog.ResponseText,
                Foreground = WpfBrushes.White,
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420
            }
        };

        Canvas.SetLeft(text, point.X);
        Canvas.SetTop(text, point.Y);
        AnnotationCanvas.Children.Add(text);
        TrackElement(text);
    }

    private void AddCallout(WpfPoint point)
    {
        var dialog = new InputDialog("Callout text")
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.ResponseText))
        {
            return;
        }

        var callout = new Canvas
        {
            Width = 360,
            Height = 116
        };
        var leader = new Line
        {
            X1 = 4,
            Y1 = 100,
            X2 = 48,
            Y2 = 64,
            Stroke = WpfBrushes.DeepSkyBlue,
            StrokeThickness = 4,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Triangle
        };
        var box = new Border
        {
            Background = new SolidColorBrush(WpfColor.FromArgb(235, 8, 16, 22)),
            BorderBrush = WpfBrushes.DeepSkyBlue,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 9),
            Child = new TextBlock
            {
                Text = dialog.ResponseText,
                Foreground = WpfBrushes.White,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 280
            }
        };

        Canvas.SetLeft(box, 40);
        Canvas.SetTop(box, 0);
        callout.Children.Add(leader);
        callout.Children.Add(box);

        Canvas.SetLeft(callout, point.X);
        Canvas.SetTop(callout, point.Y - 100);
        AnnotationCanvas.Children.Add(callout);
        TrackElement(callout);
    }

    private void AddStep(WpfPoint point)
    {
        var badge = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Background = new SolidColorBrush(WpfColor.FromRgb(48, 230, 195)),
            Child = new TextBlock
            {
                Text = _step.ToString(),
                Foreground = new SolidColorBrush(WpfColor.FromRgb(6, 17, 22)),
                FontWeight = FontWeights.Black,
                FontSize = 17,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };

        Canvas.SetLeft(badge, point.X - 17);
        Canvas.SetTop(badge, point.Y - 17);
        AnnotationCanvas.Children.Add(badge);
        TrackElement(badge);
        _step++;
    }

    private void FinalizeCrop(WpfRectangle cropRectangle)
    {
        if (cropRectangle.Width < 2 || cropRectangle.Height < 2)
        {
            AnnotationCanvas.Children.Remove(cropRectangle);
            ToolStatus.Text = "Crop selection was too small.";
            return;
        }

        if (_cropElement is not null)
        {
            AnnotationCanvas.Children.Remove(_cropElement);
            _history.Remove(_cropElement);
        }

        _cropElement = cropRectangle;
        _cropRect = ElementBounds(cropRectangle);
        TrackElement(cropRectangle);
        ToolStatus.Text = "Crop set. Copy or save will remove pixels outside the selection.";
    }

    private static Rect ElementBounds(FrameworkElement element)
    {
        return new Rect(Canvas.GetLeft(element), Canvas.GetTop(element), element.Width, element.Height);
    }

    private void ResetDrag()
    {
        _start = null;
        _activeElement = null;
        AnnotationCanvas.ReleaseMouseCapture();
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_history.Count == 0)
        {
            ToolStatus.Text = "Nothing to undo.";
            return;
        }

        var last = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        AnnotationCanvas.Children.Remove(last);
        _redoHistory.Add(last);
        if (ReferenceEquals(last, _cropElement))
        {
            _cropElement = null;
            _cropRect = null;
            ToolStatus.Text = "Crop cleared.";
            return;
        }

        ToolStatus.Text = "Undid last annotation.";
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_redoHistory.Count == 0)
        {
            ToolStatus.Text = "Nothing to redo.";
            return;
        }

        var next = _redoHistory[^1];
        _redoHistory.RemoveAt(_redoHistory.Count - 1);
        AnnotationCanvas.Children.Add(next);
        _history.Add(next);
        if (next is WpfRectangle rectangle && IsCropElement(rectangle))
        {
            _cropElement = rectangle;
            _cropRect = ElementBounds(rectangle);
            ToolStatus.Text = "Crop restored.";
            return;
        }

        ToolStatus.Text = "Redid last annotation.";
    }

    private void TrackElement(UIElement element)
    {
        _history.Add(element);
        _redoHistory.Clear();
    }

    private static bool IsCropElement(WpfRectangle rectangle)
    {
        return rectangle.StrokeDashArray.Count > 0 &&
            rectangle.Fill == WpfBrushes.Transparent &&
            rectangle.Width > 0 &&
            rectangle.Height > 0;
    }

    private void ReviewSensitiveRegions_Click(object sender, RoutedEventArgs e)
    {
        ClearSensitiveReviewOverlays();
        var review = SensitiveRegionReviewPlanner.Build(_item);
        if (!review.Succeeded)
        {
            ToolStatus.Text = review.Message;
            return;
        }

        foreach (var box in review.Boxes)
        {
            var overlay = new WpfRectangle
            {
                Width = box.Width,
                Height = box.Height,
                Stroke = new SolidColorBrush(WpfColor.FromRgb(255, 190, 92)),
                StrokeThickness = 3,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Fill = new SolidColorBrush(WpfColor.FromArgb(42, 255, 190, 92)),
                IsHitTestVisible = false
            };
            Canvas.SetLeft(overlay, box.X);
            Canvas.SetTop(overlay, box.Y);
            AutomationProperties.SetName(overlay, $"Sensitive OCR region preview: {box.Kind}");
            AnnotationCanvas.Children.Add(overlay);
            _sensitiveReviewOverlays.Add(overlay);
        }

        ToolStatus.Text = $"{review.Message} Select Redact, Blur, or Pixelate, then Apply OCR.";
    }

    private async void ApplySensitiveRegions_Click(object sender, RoutedEventArgs e)
    {
        var review = SensitiveRegionReviewPlanner.Build(_item);
        if (!review.Succeeded)
        {
            ToolStatus.Text = review.Message;
            return;
        }

        var mode = SelectedVisualRedactionMode();
        var confirm = System.Windows.MessageBox.Show(
            $"Flatten {review.Boxes.Count} detected sensitive OCR region(s) using {mode}? This creates a new edited copy.",
            "Apply OCR redaction",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            ToolStatus.Text = "OCR redaction canceled.";
            return;
        }

        var result = await _services.VisualRedactions.RedactSensitiveOcrAsync(_item, mode: mode);
        if (result.Succeeded && result.Item is not null)
        {
            ClearSensitiveReviewOverlays();
            CaptureSaved?.Invoke(this, result.Item);
        }

        ToolStatus.Text = result.Message;
    }

    private VisualRedactionMode SelectedVisualRedactionMode()
    {
        return _mode switch
        {
            AnnotationMode.Blur => VisualRedactionMode.Blur,
            AnnotationMode.Pixelate => VisualRedactionMode.Pixelate,
            _ => VisualRedactionMode.Solid
        };
    }

    private void ClearSensitiveReviewOverlays()
    {
        foreach (var overlay in _sensitiveReviewOverlays)
        {
            AnnotationCanvas.Children.Remove(overlay);
        }

        _sensitiveReviewOverlays.Clear();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        ClipboardInterop.SetImage(Render());
        ToolStatus.Text = _cropRect is null
            ? "Flattened edited image copied to clipboard."
            : "Cropped, flattened edited image copied to clipboard.";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var target = System.IO.Path.Combine(
            _services.Paths.ImagesRoot,
            $"{System.IO.Path.GetFileNameWithoutExtension(_item.FilePath)}-edited-{DateTimeOffset.Now:HHmmss}.png");

        SaveRenderedImage(target);
        var saved = await _services.WorkspaceStore.AddImageFileAsync(target, CaptureKind.EditedImage);
        CaptureSaved?.Invoke(this, saved);
        ToolStatus.Text = $"Saved edited copy: {System.IO.Path.GetFileName(target)}";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export edited image",
            Filter = "PNG image (*.png)|*.png",
            FileName = $"{System.IO.Path.GetFileNameWithoutExtension(_item.FilePath)}-edited.png",
            AddExtension = true,
            DefaultExt = ".png",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            ToolStatus.Text = "Export canceled.";
            return;
        }

        SaveRenderedImage(dialog.FileName);
        ToolStatus.Text = $"Exported edited image: {System.IO.Path.GetFileName(dialog.FileName)}";
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Controls.PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            ToolStatus.Text = "Print canceled.";
            return;
        }

        var rendered = Render();
        var scale = Math.Min(
            dialog.PrintableAreaWidth / Math.Max(1, rendered.Width),
            dialog.PrintableAreaHeight / Math.Max(1, rendered.Height));
        var image = new WpfImage
        {
            Source = rendered,
            Width = rendered.Width,
            Height = rendered.Height,
            Stretch = Stretch.Uniform,
            LayoutTransform = new ScaleTransform(scale, scale)
        };
        image.Measure(new WpfSize(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight));
        image.Arrange(new Rect(new WpfSize(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight)));
        dialog.PrintVisual(image, "GoatShot edited capture");
        ToolStatus.Text = "Sent edited capture to printer.";
    }

    private async void AiEdit_Click(object sender, RoutedEventArgs e)
    {
        if (!_services.Settings.AiEnabled)
        {
            ToolStatus.Text = "AI is disabled. Enable Gemini in Settings first.";
            return;
        }

        var prompt = AiPromptBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            ToolStatus.Text = "Enter a Gemini edit prompt first.";
            return;
        }

        if (_services.Settings.ConfirmBeforeAiRequest)
        {
            var confirm = System.Windows.MessageBox.Show(
                "This sends the current image and prompt to Gemini. Continue?",
                "Confirm Gemini edit",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                ToolStatus.Text = "Gemini edit canceled.";
                return;
            }
        }

        ToolStatus.Text = "Sending screenshot to Gemini...";
        var model = _services.Settings.GeminiDefaultModelId;
        var result = await _services.Gemini.EditImageAsync(
            new AiImageEditRequest(
                _item.FilePath,
                prompt,
                model,
                _services.Settings.ConfirmBeforeAiRequest),
            CancellationToken.None);

        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.OutputImagePath))
        {
            await _services.AiHistory.RecordAsync(
                AiActionKind.ImageEdit,
                _item,
                model,
                prompt,
                succeeded: false,
                result.Message);
            ToolStatus.Text = result.Message;
            return;
        }

        await _services.AiHistory.RecordAsync(
            AiActionKind.ImageEdit,
            _item,
            model,
            prompt,
            succeeded: true,
            result.Message,
            result.OutputImagePath);

        var saved = await _services.WorkspaceStore.AddImageFileAsync(
            result.OutputImagePath,
            CaptureKind.EditedImage,
            $"Gemini edit from prompt: {prompt}");
        CaptureSaved?.Invoke(this, saved);
        await _services.Automation.ProcessAiActionCompletedAsync(saved);
        ToolStatus.Text = result.Message;
    }

    private BitmapSource Render()
    {
        var restoreCropVisibility = _cropElement?.Visibility == Visibility.Visible;
        var visibleReviewOverlays = _sensitiveReviewOverlays
            .Where(overlay => overlay.Visibility == Visibility.Visible)
            .ToList();
        if (_cropElement is not null)
        {
            _cropElement.Visibility = Visibility.Hidden;
        }

        foreach (var overlay in visibleReviewOverlays)
        {
            overlay.Visibility = Visibility.Hidden;
        }

        EditorSurface.Measure(new WpfSize(EditorSurface.Width, EditorSurface.Height));
        EditorSurface.Arrange(new Rect(new WpfSize(EditorSurface.Width, EditorSurface.Height)));
        EditorSurface.UpdateLayout();

        try
        {
            var render = new RenderTargetBitmap(
                (int)EditorSurface.Width,
                (int)EditorSurface.Height,
                96,
                96,
                PixelFormats.Pbgra32);
            render.Render(EditorSurface);
            render.Freeze();

            if (_cropRect is not Rect crop)
            {
                return render;
            }

            var cropped = CreateCroppedBitmap(render, crop);
            return cropped ?? render;
        }
        finally
        {
            if (_cropElement is not null && restoreCropVisibility)
            {
                _cropElement.Visibility = Visibility.Visible;
            }

            foreach (var overlay in visibleReviewOverlays)
            {
                overlay.Visibility = Visibility.Visible;
            }
        }
    }

    private static BitmapSource? CreateCroppedBitmap(BitmapSource source, Rect crop)
    {
        var left = Math.Max(0, (int)Math.Floor(crop.Left));
        var top = Math.Max(0, (int)Math.Floor(crop.Top));
        var right = Math.Min(source.PixelWidth, (int)Math.Ceiling(crop.Right));
        var bottom = Math.Min(source.PixelHeight, (int)Math.Ceiling(crop.Bottom));
        var width = Math.Max(0, right - left);
        var height = Math.Max(0, bottom - top);
        if (width == 0 || height == 0)
        {
            return null;
        }

        var cropped = new CroppedBitmap(source, new Int32Rect(left, top, width, height));
        cropped.Freeze();
        return cropped;
    }

    private void SaveRenderedImage(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Render()));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
