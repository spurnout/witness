using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfPanel = System.Windows.Controls.Panel;
using WpfTextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;

namespace GoatShot.App.Services;

public static class WpfAccessibilityNameHelper
{
    public static void ApplyGeneratedNames(DependencyObject root)
    {
        foreach (var panel in Descendants(root).OfType<WpfPanel>())
        {
            ApplyPanelNames(panel);
        }
    }

    private static void ApplyPanelNames(WpfPanel panel)
    {
        string? currentLabel = null;
        foreach (UIElement child in panel.Children)
        {
            if (child is TextBlock textBlock)
            {
                var text = textBlock.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text) && LooksLikeFieldLabel(text))
                {
                    currentLabel = text;
                }

                continue;
            }

            if (child is FrameworkElement element)
            {
                if (IsInput(element) && string.IsNullOrWhiteSpace(AutomationProperties.GetName(element)))
                {
                    var name = FirstNonBlank(currentLabel, ContentName(element));
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        AutomationProperties.SetName(element, name);
                    }
                }

                if (IsInput(element))
                {
                    currentLabel = null;
                }
            }
        }
    }

    private static bool IsInput(FrameworkElement element)
    {
        return element is WpfTextBoxBase or PasswordBox or WpfComboBox or WpfListBox;
    }

    private static bool LooksLikeFieldLabel(string text)
    {
        if (text.Length > 96)
        {
            return false;
        }

        return !text.Contains("{", StringComparison.Ordinal) &&
            !text.EndsWith(".", StringComparison.Ordinal);
    }

    private static string ContentName(FrameworkElement element)
    {
        return element switch
        {
            ContentControl { Content: string text } => text,
            HeaderedContentControl { Header: string text } => text,
            _ => string.Empty
        };
    }

    private static string FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (!seen.Add(child))
            {
                continue;
            }

            yield return child;

            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }

        if (root is not Visual and not Visual3D)
        {
            yield break;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (!seen.Add(child))
            {
                continue;
            }

            yield return child;

            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
