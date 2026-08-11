using GoatShot.App.Models;
using GoatShot.App.Windows;

namespace GoatShot.Tests;

[TestClass]
public sealed class FrameExplorerVerificationTests
{
    [TestMethod]
    [DataRow(ReceiptVerificationStatus.IntactKnownDevice, "Intact — known device key")]
    [DataRow(ReceiptVerificationStatus.IntactUnknownDevice, "Intact — unknown device key")]
    [DataRow(ReceiptVerificationStatus.Modified, "Modified")]
    [DataRow(ReceiptVerificationStatus.Incomplete, "Incomplete")]
    [DataRow(ReceiptVerificationStatus.Unverifiable, "Unverifiable")]
    public void VerificationLabel_UsesApprovedReceiptStatusText(
        ReceiptVerificationStatus status,
        string expected)
    {
        Assert.AreEqual(expected, FrameExplorerWindow.VerificationLabel(status));
    }

    [TestMethod]
    [DataRow(ReceiptVerificationStatus.IntactKnownDevice, true)]
    [DataRow(ReceiptVerificationStatus.IntactUnknownDevice, true)]
    [DataRow(ReceiptVerificationStatus.Modified, false)]
    [DataRow(ReceiptVerificationStatus.Incomplete, false)]
    [DataRow(ReceiptVerificationStatus.Unverifiable, false)]
    public void CanUseOriginal_AllowsOnlyIntactVerificationResults(
        ReceiptVerificationStatus status,
        bool expected)
    {
        Assert.AreEqual(expected, FrameExplorerWindow.CanUseOriginal(status));
    }

    [TestMethod]
    public void CalculateTrackDuration_UsesReceiptTimelineWhenMediaDurationIsUnavailable()
    {
        var firstStart = 500L;
        var manifest = new ReceiptManifest
        {
            Segments =
            [
                new ReceiptSegmentManifest
                {
                    TrackId = "track-a",
                    StartMonotonicTicks = firstStart,
                    DurationTicks = TimeSpan.FromSeconds(2).Ticks
                },
                new ReceiptSegmentManifest
                {
                    TrackId = "track-a",
                    StartMonotonicTicks = firstStart + TimeSpan.FromSeconds(3).Ticks,
                    DurationTicks = TimeSpan.FromSeconds(1).Ticks
                },
                new ReceiptSegmentManifest
                {
                    TrackId = "track-b",
                    StartMonotonicTicks = firstStart,
                    DurationTicks = TimeSpan.FromSeconds(20).Ticks
                }
            ]
        };

        var duration = FrameExplorerWindow.CalculateTrackDuration(manifest, "track-a");
        var displayedPosition = FrameExplorerWindow.ClampPosition(
            TimeSpan.FromSeconds(5),
            duration);

        Assert.AreEqual(TimeSpan.FromSeconds(4), duration, "The fallback must preserve the one-second timeline gap.");
        Assert.AreEqual(duration, displayedPosition);
        Assert.IsTrue(displayedPosition <= duration);
    }

    [TestMethod]
    public void CalculateTrackDuration_WithoutSelectedTrackDataReturnsZero()
    {
        var manifest = new ReceiptManifest();

        Assert.AreEqual(TimeSpan.Zero, FrameExplorerWindow.CalculateTrackDuration(manifest, "missing"));
        Assert.AreEqual(
            TimeSpan.Zero,
            FrameExplorerWindow.ClampPosition(TimeSpan.FromSeconds(2), TimeSpan.Zero));
    }

    [TestMethod]
    public void NormalizeSelectedRange_OrdersMarksAndClampsNegativePositions()
    {
        var reversed = FrameExplorerWindow.NormalizeSelectedRange(
            TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(2));
        var negative = FrameExplorerWindow.NormalizeSelectedRange(
            TimeSpan.FromSeconds(-1),
            null);

        Assert.AreEqual(TimeSpan.FromSeconds(2), reversed.Start);
        Assert.AreEqual(TimeSpan.FromSeconds(8), reversed.End);
        Assert.AreEqual(TimeSpan.Zero, negative.Start);
        Assert.IsNull(negative.End);
    }

    [TestMethod]
    public void FindNearestHoverFrame_UsesExistingLocalSceneIndexForSelectedTrack()
    {
        var origin = 1_000L;
        var analysis = new ReceiptLocalAnalysis
        {
            Scenes =
            [
                new ReceiptSceneMarker
                {
                    TrackId = "track-a",
                    RelativeFramePath = "frames/start.png",
                    MonotonicTicks = origin,
                    IsSourceTransition = true,
                    IsVisuallyDistinct = true
                },
                new ReceiptSceneMarker
                {
                    TrackId = "track-a",
                    RelativeFramePath = "frames/edit.png",
                    MonotonicTicks = origin + TimeSpan.FromSeconds(2).Ticks,
                    IsVisuallyDistinct = true
                },
                new ReceiptSceneMarker
                {
                    TrackId = "track-b",
                    RelativeFramePath = "frames/other-track.png",
                    MonotonicTicks = origin + TimeSpan.FromSeconds(1.9d).Ticks,
                    IsVisuallyDistinct = true
                }
            ]
        };

        var frame = FrameExplorerWindow.FindNearestHoverFrame(
            analysis,
            "track-a",
            origin,
            TimeSpan.FromSeconds(1.8d));

        Assert.IsNotNull(frame);
        Assert.AreEqual("frames/edit.png", frame.RelativeFramePath);
        Assert.AreEqual("Scene", frame.Label);
    }

    [TestMethod]
    public void FindNearestHoverFrame_FallsBackToSavedOcrFrameWithoutRunningAnalysis()
    {
        var analysis = new ReceiptLocalAnalysis
        {
            Frames =
            [
                new ReceiptOcrFrame
                {
                    TrackId = "track-a",
                    RelativeFramePath = "frames/saved-ocr-frame.png",
                    MonotonicTicks = TimeSpan.FromSeconds(1).Ticks,
                    Text = "Already indexed text",
                    OcrSucceeded = true
                }
            ]
        };

        var frame = FrameExplorerWindow.FindNearestHoverFrame(
            analysis,
            "track-a",
            trackOriginMonotonicTicks: 0,
            position: TimeSpan.FromSeconds(1));

        Assert.IsNotNull(frame);
        Assert.AreEqual("frames/saved-ocr-frame.png", frame.RelativeFramePath);
        Assert.AreEqual("Indexed frame", frame.Label);
    }
}
