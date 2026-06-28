using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfSize = System.Windows.Size;

namespace GoatShot.App.Services;

public static class TrayMenuPreviewRenderer
{
    private const double Width = 460;
    private const double HorizontalPadding = 22;

    public static Task RenderAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("A tray menu preview output path is required.", nameof(outputPath));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var root = BuildPreview();
        root.Measure(new WpfSize(Width, double.PositiveInfinity));
        root.Arrange(new Rect(0, 0, Width, root.DesiredSize.Height));
        root.UpdateLayout();

        var pixelWidth = Math.Max(1, (int)Math.Ceiling(root.ActualWidth));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(fullPath);
        encoder.Save(stream);

        return Task.CompletedTask;
    }

    private static FrameworkElement BuildPreview()
    {
        var root = new Border
        {
            Width = Width,
            Background = Brush("#101820"),
            BorderBrush = Brush("#2F4758"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(HorizontalPadding, 20, HorizontalPadding, 18),
            Child = BuildContent()
        };

        return root;
    }

    private static UIElement BuildContent()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "GoatShot tray menu",
            Foreground = Brush("#F5FAFF"),
            FontSize = 21,
            FontWeight = FontWeights.SemiBold
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Diagnostic preview generated from the same catalog as the NotifyIcon menu.",
            Foreground = Brush("#A9BAC8"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 18)
        });

        foreach (var group in TrayMenuActionCatalog.Actions.GroupBy(action => action.Group))
        {
            stack.Children.Add(BuildGroupHeader(group.Key));
            foreach (var definition in group)
            {
                stack.Children.Add(BuildActionRow(definition));
            }
        }

        stack.Children.Add(new TextBlock
        {
            Text = $"{TrayMenuActionCatalog.Actions.Count()} actions across {TrayMenuActionCatalog.Actions.Select(action => action.Group).Distinct().Count()} groups",
            Foreground = Brush("#A9BAC8"),
            FontSize = 11,
            Margin = new Thickness(0, 16, 0, 0)
        });

        return stack;
    }

    private static UIElement BuildActionRow(TrayMenuActionDefinition definition)
    {
        return new Border
        {
            MinHeight = 32,
            Margin = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(10, 0, 10, 0),
            Background = Brush("#111F2A"),
            BorderBrush = Brush("#24394A"),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = definition.Label,
                Foreground = Brush("#F5FAFF"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };
    }

    private static UIElement BuildGroupHeader(string group)
    {
        return new TextBlock
        {
            Text = group,
            Foreground = Brush("#30E6C3"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 5)
        };
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
