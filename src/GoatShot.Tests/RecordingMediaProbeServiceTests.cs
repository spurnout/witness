using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class RecordingMediaProbeServiceTests
{
    [TestMethod]
    public async Task ProbeAsync_ReturnsSkippedWhenFfprobeIsUnavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var input = Path.Combine(root, "recording.mp4");
        await File.WriteAllTextAsync(input, "not a real mp4");
        var oldPath = Environment.GetEnvironmentVariable("PATH");
        var oldFfmpeg = Environment.GetEnvironmentVariable("GOATSHOT_FFMPEG_PATH");
        var oldFfprobe = Environment.GetEnvironmentVariable("GOATSHOT_FFPROBE_PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", string.Empty);
            Environment.SetEnvironmentVariable("GOATSHOT_FFMPEG_PATH", null);
            Environment.SetEnvironmentVariable("GOATSHOT_FFPROBE_PATH", null);

            var result = await RecordingMediaProbeService.ProbeAsync(input);

            Assert.IsTrue(result.Skipped);
            Assert.IsFalse(result.FfprobeAvailable);
            StringAssert.Contains(result.Message, "ffprobe unavailable");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", oldPath);
            Environment.SetEnvironmentVariable("GOATSHOT_FFMPEG_PATH", oldFfmpeg);
            Environment.SetEnvironmentVariable("GOATSHOT_FFPROBE_PATH", oldFfprobe);
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ParseFfprobeJson_ReportsVideoAudioAndDurationDelta()
    {
        const string json = """
        {
          "streams": [
            {
              "codec_type": "video",
              "codec_name": "h264",
              "width": 1920,
              "height": 1080,
              "duration": "3.000000",
              "nb_frames": "90"
            },
            {
              "codec_type": "audio",
              "codec_name": "aac",
              "duration": "2.920000"
            }
          ],
          "format": {
            "duration": "3.010000"
          }
        }
        """;

        var result = RecordingMediaProbeService.ParseFfprobeJson(
            "demo.mp4",
            "ffprobe.exe",
            json);

        Assert.IsTrue(result.Succeeded);
        Assert.IsFalse(result.Skipped);
        Assert.AreEqual("h264", result.VideoCodec);
        Assert.AreEqual(1920, result.Width);
        Assert.AreEqual(1080, result.Height);
        Assert.AreEqual(90, result.VideoFrames);
        Assert.AreEqual(1, result.AudioStreamCount);
        Assert.AreEqual(0.08d, result.MaxAudioVideoDeltaSeconds!.Value, 0.001d);
        StringAssert.Contains(result.Message, "max A/V duration delta");
        StringAssert.Contains(result.SyncSummary, "within");
    }

    [TestMethod]
    public void ParseFfprobeJson_ReportsNoAudioAsNotApplicable()
    {
        const string json = """
        {
          "streams": [
            {
              "codec_type": "video",
              "codec_name": "hevc",
              "width": 1280,
              "height": 720,
              "duration": "1.500000"
            }
          ],
          "format": {
            "duration": "1.500000"
          }
        }
        """;

        var result = RecordingMediaProbeService.ParseFfprobeJson(
            "demo.mp4",
            "ffprobe.exe",
            json);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("hevc", result.VideoCodec);
        Assert.AreEqual(0, result.AudioStreamCount);
        Assert.IsNull(result.MaxAudioVideoDeltaSeconds);
        StringAssert.Contains(result.SyncSummary, "not applicable");
    }
}
