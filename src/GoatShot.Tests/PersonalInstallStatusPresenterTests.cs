using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class PersonalInstallStatusPresenterTests
{
    private static PersonalInstallState State(
        bool startupRegistered = true,
        bool startupCommandCurrent = true,
        bool runningInstalledCopy = true,
        string installedVersion = "0.3.0") =>
        new(
            InstalledPath: @"C:\Users\dev\AppData\Local\Programs\Receipts\Receipts.exe",
            CurrentVersion: "0.3.0",
            InstalledVersion: installedVersion,
            BuildId: "local",
            StartupRegistered: startupRegistered,
            StartupCommandCurrent: startupCommandCurrent,
            RollbackAvailable: false,
            RunningInstalledCopy: runningInstalledCopy,
            RepairHealthy: true);

    private static IReadOnlyList<InstallStatusRow> Build(
        PersonalInstallState? state = null,
        bool updateAvailable = false,
        bool runtimeReady = true,
        string runtimeMessage = "Bundled runtime assets are ready.",
        string? ffmpegVersion = "7.1",
        bool segmentationReady = true,
        string segmentationMessage = "Bundled person segmentation is ready.",
        string transcriptionProvider = "external-whisper",
        string whisperState = "External Whisper not configured") =>
        PersonalInstallStatusPresenter.Build(
            state ?? State(),
            updateAvailable,
            runtimeReady,
            runtimeMessage,
            ffmpegVersion,
            segmentationReady,
            segmentationMessage,
            transcriptionProvider,
            whisperState);

    private static InstallStatusRow Row(IReadOnlyList<InstallStatusRow> rows, string label) =>
        rows.Single(row => row.Label == label);

    [TestMethod]
    public void Build_ReturnsOneRowPerFactWithLabelsAndValues()
    {
        var rows = Build();

        Assert.IsTrue(rows.Count >= 6, "The block should read as a table of distinct facts.");
        Assert.IsTrue(rows.All(row => !string.IsNullOrWhiteSpace(row.Label)));
        Assert.IsTrue(rows.All(row => !string.IsNullOrWhiteSpace(row.Value)));
        Assert.AreEqual(
            rows.Count,
            rows.Select(row => row.Label).Distinct(StringComparer.Ordinal).Count(),
            "Labels must be unique so the grid reads unambiguously.");
    }

    [TestMethod]
    public void Build_FlagsAnAvailableUpdateAsNeedingAttention()
    {
        Assert.AreEqual(InstallStatusTone.Attention, Row(Build(updateAvailable: true), "Update").Tone);
        Assert.AreEqual(InstallStatusTone.Ok, Row(Build(updateAvailable: false), "Update").Tone);
    }

    [TestMethod]
    public void Build_FlagsBrokenStartupRegistration()
    {
        Assert.AreEqual(InstallStatusTone.Ok, Row(Build(), "Startup").Tone);

        var stale = Build(State(startupCommandCurrent: false));
        Assert.AreEqual(InstallStatusTone.Attention, Row(stale, "Startup").Tone);
        StringAssert.Contains(Row(stale, "Startup").Value, "repair");
    }

    [TestMethod]
    public void Build_ReportsAnUninstalledCopyWithoutInventingAVersion()
    {
        var rows = Build(State(installedVersion: string.Empty, runningInstalledCopy: false));

        Assert.AreEqual("Not installed", Row(rows, "Installed").Value);
        Assert.AreEqual(InstallStatusTone.Attention, Row(rows, "Installed").Tone);
        // With nothing installed there is no update to offer, so that row must not cry wolf.
        Assert.AreNotEqual(InstallStatusTone.Attention, Row(rows, "Update").Tone);
    }

    [TestMethod]
    public void Build_CarriesRuntimeAndSegmentationHealthIntoTone()
    {
        var unhealthy = Build(
            runtimeReady: false,
            runtimeMessage: "Bundled runtime assets are missing.",
            segmentationReady: false,
            segmentationMessage: "The bundled person-segmentation model is unavailable; run Repair.");

        Assert.AreEqual(InstallStatusTone.Attention, Row(unhealthy, "Bundled runtime").Tone);
        Assert.AreEqual(InstallStatusTone.Attention, Row(unhealthy, "Person segmentation").Tone);
        Assert.AreEqual(InstallStatusTone.Ok, Row(Build(), "Bundled runtime").Tone);
    }

    [TestMethod]
    public void Build_DescribesFfmpegResolutionWhenNoBundledVersionExists()
    {
        StringAssert.Contains(Row(Build(ffmpegVersion: null), "FFmpeg").Value, "PATH");
        Assert.AreEqual("7.1", Row(Build(ffmpegVersion: "7.1"), "FFmpeg").Value);
    }

    [TestMethod]
    public void Build_KeepsEachValueShortEnoughToReadInAGrid()
    {
        foreach (var row in Build())
        {
            Assert.IsTrue(
                row.Value.Length <= 120,
                $"'{row.Label}' value is {row.Value.Length} characters; the old block failed because it ran on.");
        }
    }
}
