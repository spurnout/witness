using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using GoatShot.App.Models;

namespace GoatShot.Tests;

/// <summary>
/// The upload queue shipped "{Binding StatusLabel}" on a Run. WPF treats that as TwoWay, which throws
/// against a computed property, and headless renders never materialize item templates -- so it took an
/// interactive launch with queued items to surface. This sweeps every XAML file for the same shape.
///
/// Element/property pairs below were verified against FrameworkPropertyMetadata.BindsTwoWayByDefault;
/// notably TextBlock.Text is NOT two-way (only TextBox, Run and ComboBox Text are), so binding a
/// computed property to a TextBlock is fine and must not be flagged.
/// </summary>
[TestClass]
public sealed class TwoWayBindingAuditTests
{
    private static readonly Dictionary<string, string[]> TwoWayByDefault = new(StringComparer.Ordinal)
    {
        ["TextBox"] = ["Text"],
        ["Run"] = ["Text"],
        ["ComboBox"] = ["Text", "SelectedItem", "SelectedValue", "SelectedIndex"],
        ["CheckBox"] = ["IsChecked"],
        ["RadioButton"] = ["IsChecked"],
        ["ToggleButton"] = ["IsChecked"],
        ["ListBox"] = ["SelectedItem", "SelectedValue", "SelectedIndex"],
        ["ListView"] = ["SelectedItem", "SelectedValue", "SelectedIndex"],
        ["TabControl"] = ["SelectedItem", "SelectedValue", "SelectedIndex"],
        ["ListBoxItem"] = ["IsSelected"],
        ["Slider"] = ["Value"],
        ["ProgressBar"] = ["Value"],
        ["DatePicker"] = ["SelectedDate"]
    };

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GoatShot.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find GoatShot.slnx.");
    }

    /// <summary>Public instance properties anywhere in the app that expose a getter but no setter.</summary>
    private static HashSet<string> GetOnlyPropertyNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in typeof(CaptureItem).Assembly.GetTypes())
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length == 0 && property.CanRead && !property.CanWrite)
                {
                    names.Add(property.Name);
                }
            }
        }

        return names;
    }

    /// <summary>Extracts the bound path from a binding expression, or null if it is not a simple binding.</summary>
    private static string? ReadBindingPath(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("{Binding", StringComparison.OrdinalIgnoreCase) || !trimmed.EndsWith('}'))
        {
            return null;
        }

        if (trimmed.Contains("Mode=", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var body = trimmed[8..^1].Trim();
        var first = body.Split(',')[0].Trim();
        if (first.StartsWith("Path=", StringComparison.OrdinalIgnoreCase))
        {
            first = first[5..].Trim();
        }

        return first.Length > 0 && first.All(c => char.IsLetterOrDigit(c) || c == '_') ? first : null;
    }

    [TestMethod]
    public void NoUnmodedBindingTargetsAGetOnlyPropertyOnATwoWayByDefaultElement()
    {
        var getOnly = GetOnlyPropertyNames();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(RepoRoot(), "src", "GoatShot.App"),
                     "*.xaml",
                     SearchOption.AllDirectories))
        {
            var document = XDocument.Load(file, LoadOptions.SetLineInfo);
            foreach (var element in document.Descendants())
            {
                if (!TwoWayByDefault.TryGetValue(element.Name.LocalName, out var attributes))
                {
                    continue;
                }

                foreach (var attribute in element.Attributes())
                {
                    if (!attributes.Contains(attribute.Name.LocalName, StringComparer.Ordinal))
                    {
                        continue;
                    }

                    if (ReadBindingPath(attribute.Value) is { } path && getOnly.Contains(path))
                    {
                        var line = (attribute as IXmlLineInfo).LineNumber;
                        offenders.Add(
                            $"{Path.GetFileName(file)}:{line} <{element.Name.LocalName} {attribute.Name.LocalName}=\"{{Binding {path}}}\">");
                    }
                }
            }
        }

        Assert.AreEqual(
            0,
            offenders.Count,
            "These bind a get-only property to a TwoWay-by-default target with no Mode, so WPF throws " +
            $"when the template materializes. Add Mode=OneWay:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders));
    }
}
