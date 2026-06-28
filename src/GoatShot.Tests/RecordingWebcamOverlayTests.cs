using System.Drawing;
using System.Drawing.Imaging;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class RecordingWebcamOverlayTests
{
    [TestMethod]
    public void ParseFfmpegDirectShowVideoDevices_ReturnsOnlyVideoDevices()
    {
        const string output = """
            [dshow @ 000001] "EOS Webcam Utility" (video)
            [dshow @ 000001]   Alternative name "@device_pnp_foo"
            [dshow @ 000001] "OBSBOT Virtual Camera" (none)
            [dshow @ 000001] "Microphone (Studio)" (audio)
            [dshow @ 000001] "OBS Virtual Camera" (video)
            """;

        var devices = RecordingService.ParseFfmpegDirectShowVideoDevices(output);

        CollectionAssert.AreEqual(
            new[] { "EOS Webcam Utility", "OBS Virtual Camera" },
            devices.ToArray());
    }

    [TestMethod]
    public void ResolveFfmpegCameraDeviceName_PrefersExactThenFuzzyName()
    {
        var exact = RecordingService.ResolveFfmpegCameraDeviceName(
            new CameraOverlayDevice("camera-1", "OBSBOT Tiny 4K Camera", IsDefault: true),
            new[] { "EOS Webcam Utility", "OBSBOT Tiny 4K Camera" });
        var fuzzy = RecordingService.ResolveFfmpegCameraDeviceName(
            new CameraOverlayDevice("camera-2", "OBSBOT Tiny 4K", IsDefault: false),
            new[] { "OBSBOT Tiny 4K Camera" });

        Assert.AreEqual("OBSBOT Tiny 4K Camera", exact);
        Assert.AreEqual("OBSBOT Tiny 4K Camera", fuzzy);
    }

    [TestMethod]
    public void BuildWebcamOverlayFilter_UsesCircleMirrorAndRequestedPosition()
    {
        var filter = RecordingService.BuildWebcamOverlayFilter(
            2,
            new RecordingService.Mp4WebcamInput(
                "OBSBOT Tiny 4K Camera",
                "OBSBOT Tiny 4K Camera",
                "TopLeft",
                "Circle",
                Mirror: true));

        StringAssert.Contains(filter, "[2:v:0]setpts=PTS-STARTPTS,hflip,crop=");
        StringAssert.Contains(filter, "geq=");
        StringAssert.Contains(filter, "[0:v:0][webcam_overlay]overlay=24:24:eof_action=pass[webcam_video]");
    }

    [TestMethod]
    public void BuildWebcamOverlayPosition_NormalizesCornerAliases()
    {
        Assert.AreEqual("W-w-24:24", RecordingService.BuildWebcamOverlayPosition("top-right"));
        Assert.AreEqual("24:H-h-24", RecordingService.BuildWebcamOverlayPosition("left bottom"));
        Assert.AreEqual("W-w-24:H-h-24", RecordingService.BuildWebcamOverlayPosition("unknown"));
    }

    [TestMethod]
    public void ResolveOverlayBadgeOrigin_UsesConfiguredPositionAndClampsInsideFrame()
    {
        var topRight = RecordingService.ResolveOverlayBadgeOrigin(
            frameWidth: 1920,
            frameHeight: 1080,
            badgeWidth: 200,
            badgeHeight: 40,
            position: "top-right",
            margin: 16);
        var bottomCenter = RecordingService.ResolveOverlayBadgeOrigin(
            frameWidth: 1920,
            frameHeight: 1080,
            badgeWidth: 200,
            badgeHeight: 40,
            position: "bottom center",
            margin: 16);
        var oversized = RecordingService.ResolveOverlayBadgeOrigin(
            frameWidth: 100,
            frameHeight: 80,
            badgeWidth: 200,
            badgeHeight: 100,
            position: "bottom-right",
            margin: 16);

        Assert.AreEqual(1704, topRight.X);
        Assert.AreEqual(16, topRight.Y);
        Assert.AreEqual(860, bottomCenter.X);
        Assert.AreEqual(1024, bottomCenter.Y);
        Assert.AreEqual(0, oversized.X);
        Assert.AreEqual(0, oversized.Y);
    }

    [TestMethod]
    public void ResolveNativeWebcamOverlayBounds_UsesRequestedCorner()
    {
        var bounds = RecordingService.ResolveNativeWebcamOverlayBounds(
            frameWidth: 1000,
            frameHeight: 600,
            sourceWidth: 640,
            sourceHeight: 480,
            shape: "rectangle",
            position: "top-left");

        Assert.AreEqual(14, bounds.X);
        Assert.AreEqual(14, bounds.Y);
        Assert.IsTrue(bounds.Width >= 140);
        Assert.IsTrue(bounds.Height >= 80);
    }

    [TestMethod]
    public void ComposeNativeWebcamOverlay_DrawsIntoRequestedCorner()
    {
        using var frame = new Bitmap(400, 240, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(frame))
        {
            graphics.Clear(Color.Black);
        }

        using var camera = new Bitmap(80, 80, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(camera))
        {
            graphics.Clear(Color.Red);
        }

        using var overlay = new RecordingService.NativeWebcamOverlay(
            camera,
            "Test camera",
            "TopLeft",
            "rectangle",
            Mirror: false);

        using var composited = RecordingService.ComposeNativeWebcamOverlay(frame, overlay);
        var bounds = RecordingService.ResolveNativeWebcamOverlayBounds(
            frame.Width,
            frame.Height,
            camera.Width,
            camera.Height,
            "rectangle",
            "TopLeft");
        var pixel = composited.GetPixel(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);

        Assert.IsTrue(pixel.R > 180);
        Assert.IsTrue(pixel.G < 80);
        Assert.IsTrue(pixel.B < 80);
    }

    [TestMethod]
    public async Task NativeWebcamOverlaySource_RefreshesWhenDueAndKeepsLastGoodFrameOnFailure()
    {
        using var red = CreateSolidBitmap(Color.Red);
        using var blue = CreateSolidBitmap(Color.Blue);
        var camera = new FakeCameraOverlayService(
            new CameraOverlayFrameResult(true, (Bitmap)red.Clone(), new CameraOverlayDevice("cam-1", "Test Camera", true), "red frame"),
            new CameraOverlayFrameResult(true, (Bitmap)blue.Clone(), new CameraOverlayDevice("cam-1", "Test Camera", true), "blue frame"),
            new CameraOverlayFrameResult(false, null, new CameraOverlayDevice("cam-1", "Test Camera", true), "camera temporarily busy"));

        using var source = new RecordingService.NativeWebcamOverlaySource(
            camera,
            "cam-1",
            "TopLeft",
            "rectangle",
            mirror: false,
            TimeSpan.FromMilliseconds(100));

        var first = await source.RefreshNowAsync(DateTimeOffset.UnixEpoch, CancellationToken.None);
        var second = await source.RefreshIfDueAsync(DateTimeOffset.UnixEpoch.AddMilliseconds(120), CancellationToken.None);
        var failed = await source.RefreshIfDueAsync(DateTimeOffset.UnixEpoch.AddMilliseconds(240), CancellationToken.None);

        Assert.IsTrue(first);
        Assert.IsTrue(second);
        Assert.IsFalse(failed);
        Assert.AreEqual(2, source.CapturedFrameCount);
        Assert.AreEqual(1, source.RefreshFailureCount);
        Assert.IsNotNull(source.Current);
        var pixel = source.Current.Frame.GetPixel(8, 8);
        Assert.IsTrue(pixel.B > 180);
        StringAssert.Contains(source.Summary, "captured 2 native camera frames");
        StringAssert.Contains(source.Summary, "kept the last good frame");
    }

    private static Bitmap CreateSolidBitmap(Color color)
    {
        var bitmap = new Bitmap(24, 24, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        return bitmap;
    }

    private sealed class FakeCameraOverlayService : ICameraOverlayService
    {
        private readonly Queue<CameraOverlayFrameResult> _results;

        public FakeCameraOverlayService(params CameraOverlayFrameResult[] results)
        {
            _results = new Queue<CameraOverlayFrameResult>(results);
        }

        public Task<IReadOnlyList<CameraOverlayDevice>> ListDevicesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CameraOverlayDevice>>(
                new[] { new CameraOverlayDevice("cam-1", "Test Camera", true) });
        }

        public Task<CameraOverlayFrameResult> CaptureFrameAsync(string deviceId, CancellationToken cancellationToken)
        {
            var result = _results.Dequeue();
            var clone = result.Frame is null ? null : (Bitmap)result.Frame.Clone();
            return Task.FromResult(result with { Frame = clone });
        }

        public Task<ProviderHealth> ValidateAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new ProviderHealth(true, "ok"));
        }
    }
}
