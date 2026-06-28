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

        foreach (var definition in TrayMenuActionCatalog.All)
        {
            stack.Children.Add(definition.IsSeparator ? BuildSeparator() : BuildActionRow(definition));
        }

        stack.Children.Add(new TextBlock
        {
            Text = $"{TrayMenuActionCatalog.Actions.Count()} actions, {TrayMenuActionCatalog.All.Count(item => item.IsSeparator)} separators",
            Foreground = Brush("#A9BAC8"),
            FontSize = 11,
            Margin = new Thickness(0, 16, 0, 0)
        });

        return stack;
    }

    private static UIElement BuildActionRow(TrayMenuActionDefinition definition)
    {
        var grid = new Grid
        {
            MinHeight = 32,
            Margin = new Thickness(0, 1, 0, 1)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = definition.Label,
            Foreground = Brush("#F5FAFF"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var group = new Border
        {
            Background = Brush("#162836"),
            BorderBrush = Brush("#36566D"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = definition.Group,
                Foreground = Brush("#AEE9F7"),
                FontSize = 10
            }
        };
        Grid.SetColumn(group, 1);
        grid.Children.Add(group);

        return grid;
    }

    private static UIElement BuildSeparator()
    {
        return new Border
        {
            Height = 1,
            Background = Brush("#31475A"),
            Margin = new Thickness(0, 8, 0, 8)
        };
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
