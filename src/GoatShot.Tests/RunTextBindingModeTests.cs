using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Documents;
using GoatShot.App.Services;

namespace GoatShot.Tests;

/// <summary>
/// <see cref="Run.Text"/> is declared with BindsTwoWayByDefault, so "{Binding Foo}" on a Run is a
/// TwoWay binding. Against a get-only property WPF throws at template-materialization time, which
/// the app's DispatcherUnhandledException handler turns into a modal error box per list item.
/// That shipped: the upload queue template bound StatusLabel (a computed property) without a Mode,
/// so opening the workspace with queued items produced a stack of error dialogs.
/// </summary>
[TestClass]
public sealed class RunTextBindingModeTests
{
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

        throw new DirectoryNotFoundException("Could not find GoatShot.slnx from the test output directory.");
    }

    [TestMethod]
    public void RunText_IsTwoWayByDefault_WhichIsWhyModeMustBeExplicit()
    {
        var metadata = (System.Windows.FrameworkPropertyMetadata)Run.TextProperty
            .GetMetadata(typeof(Run));

        Assert.IsTrue(
            metadata.BindsTwoWayByDefault,
            "If this ever becomes false the rule below can be relaxed, but until then an unmoded " +
            "Run binding is a TwoWay binding.");
    }

    /// <summary>
    /// Runs an action on an STA thread and returns the exception it threw, or null. WPF element
    /// construction requires STA, which the MSTest worker thread is not.
    /// </summary>
    private static Exception? CaptureOnSta(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));
        return captured;
    }

    [TestMethod]
    public void DefaultModeBindingToStatusLabel_ThrowsExactlyAsItDidInTheShippedBuild()
    {
        var captured = CaptureOnSta(() =>
        {
            var run = new Run();
            BindingOperations.SetBinding(
                run,
                Run.TextProperty,
                new Binding(nameof(UploadQueueItem.StatusLabel)) { Source = new UploadQueueItem() });
        });

        Assert.IsInstanceOfType<InvalidOperationException>(
            captured,
            "This is the failure users saw. If it stops throwing, WPF changed and the guard can relax.");
        StringAssert.Contains(captured!.Message, "read-only property");
        StringAssert.Contains(captured.Message, nameof(UploadQueueItem.StatusLabel));
    }

    [TestMethod]
    public void OneWayBindingToStatusLabel_BindsCleanlyAndShowsTheValue()
    {
        string? rendered = null;
        var captured = CaptureOnSta(() =>
        {
            var run = new Run();
            BindingOperations.SetBinding(
                run,
                Run.TextProperty,
                new Binding(nameof(UploadQueueItem.StatusLabel))
                {
                    Source = new UploadQueueItem(),
                    Mode = BindingMode.OneWay
                });
            rendered = run.Text;
        });

        Assert.IsNull(captured, $"OneWay must bind cleanly, but threw: {captured}");
        Assert.IsFalse(string.IsNullOrEmpty(rendered), "The status should still render after the fix.");
    }

    [TestMethod]
    public void EveryRunTextBinding_DeclaresAnExplicitMode()
    {
        var offenders = new List<string>();
        var runBinding = new Regex(@"<Run\b[^>]*Text\s*=\s*""\{\s*Binding(?<body>[^}]*)\}""", RegexOptions.IgnoreCase);

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(RepoRoot(), "src", "GoatShot.App"),
                     "*.xaml",
                     SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                foreach (Match match in runBinding.Matches(lines[index]))
                {
                    if (!match.Groups["body"].Value.Contains("Mode=", StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{Path.GetFileName(file)}:{index + 1} {match.Value.Trim()}");
                    }
                }
            }
        }

        Assert.AreEqual(
            0,
            offenders.Count,
            "Run.Text binds TwoWay by default, so these become TwoWay bindings and crash the moment " +
            $"their target is get-only. Add Mode=OneWay:{Environment.NewLine}" +
            string.Join(Environment.NewLine, offenders));
    }
}
