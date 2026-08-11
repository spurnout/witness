using System.Drawing;
using System.Runtime.InteropServices;
using GoatShot.App.Models;
using GoatShot.App.Services;
using NAudio.Wave;

namespace GoatShot.Tests;

[TestClass]
public sealed class StreamingRecordingArchitectureTests
{
    [TestMethod]
    public void PcmNormalizer_ResamplesMonoPcm16IntoBoundedStereoChunks()
    {
        var normalizer = new StreamingPcmNormalizer(new WaveFormat(24_000, 16, 1));
        var input = new byte[2_400 * 2];
        for (var frame = 0; frame < 2_400; frame++)
        {
            var sample = (short)(Math.Sin(frame / 12d) * short.MaxValue * 0.2d);
            input[frame * 2] = (byte)(sample & 0xff);
            input[frame * 2 + 1] = (byte)((sample >> 8) & 0xff);
        }

        var chunks = normalizer.Push(input, input.Length)
            .Concat(normalizer.Flush())
            .ToArray();

        Assert.IsTrue(chunks.Length >= 2);
        Assert.IsTrue(chunks.All(chunk => chunk.Pcm16.Length <=
            StreamingRecordingAudioSession.MixChunkFrames * StreamingRecordingAudioSession.BytesPerFrame));
        Assert.IsTrue(chunks.All(chunk => chunk.SampleRate == 48_000 && chunk.Channels == 2));
        var output = chunks.SelectMany(chunk => chunk.Pcm16.ToArray()).ToArray();
        Assert.AreEqual(0, output.Length % StreamingRecordingAudioSession.BytesPerFrame);
        Assert.IsTrue(output.Length >= input.Length * 3,
            "24 kHz mono should expand to approximately twice the frames and twice the channels.");
        for (var offset = 0; offset < output.Length; offset += 4)
        {
            Assert.AreEqual(output[offset], output[offset + 2]);
            Assert.AreEqual(output[offset + 1], output[offset + 3]);
        }
    }

    [TestMethod]
    public void PcmNormalizer_AcceptsCommonWasapiExtensibleFloatFormat()
    {
        var format = new WaveFormatExtensible(48_000, 32, 2);
        var normalizer = new StreamingPcmNormalizer(format);
        var input = new byte[128 * 2 * sizeof(float)];
        for (var sample = 0; sample < 128 * 2; sample++)
        {
            BitConverter.GetBytes(0.1f).CopyTo(input, sample * sizeof(float));
        }

        var chunks = normalizer.Push(input, input.Length)
            .Concat(normalizer.Flush())
            .ToArray();

        Assert.IsTrue(AudioSampleProcessor.CanProcess(format));
        Assert.IsTrue(chunks.Sum(chunk => chunk.Pcm16.Length) > 0);
    }

    [TestMethod]
    public void BoundedMixer_WaitsForBothSourcesAndMixesFixedChunks()
    {
        var emitted = new List<byte[]>();
        var mixer = new StreamingRecordingAudioSession.BoundedPcmMixer(
            [AudioCaptureSource.Microphone, AudioCaptureSource.SystemAudio],
            pcm => emitted.Add(pcm.ToArray()));
        var microphone = ConstantPcm(StreamingRecordingAudioSession.MixChunkFrames, 12_000);
        var system = ConstantPcm(StreamingRecordingAudioSession.MixChunkFrames, -4_000);

        mixer.Add(AudioCaptureSource.Microphone, microphone);
        Assert.AreEqual(0, emitted.Count);
        mixer.Add(AudioCaptureSource.SystemAudio, system);

        Assert.AreEqual(1, emitted.Count);
        Assert.AreEqual(
            StreamingRecordingAudioSession.MixChunkFrames * StreamingRecordingAudioSession.BytesPerFrame,
            emitted[0].Length);
        Assert.AreEqual(4_000, BitConverter.ToInt16(emitted[0], 0));
        Assert.AreEqual(4_000, BitConverter.ToInt16(emitted[0], 2));
        Assert.AreEqual(emitted[0].Length, mixer.MixedBytesEmitted);
    }

    [TestMethod]
    public async Task SharedAudioSession_StopRetainsPartialPcmDeliveredAtRotation()
    {
        var sink = new List<byte[]>();
        var provider = new PartialStreamingAudioProvider();
        await using var audio = StreamingRecordingAudioSession.Start(
            provider,
            [new StreamingAudioCaptureRequest(
                AudioCaptureSource.Microphone,
                "fixture-mic",
                new AudioCaptureProcessingSettings(1.25d, -30d, Muted: false))],
            pcm => sink.Add(pcm.ToArray()));

        var result = await audio.StopAsync();

        Assert.AreEqual(1, provider.Requests.Count);
        Assert.AreEqual("fixture-mic", provider.Requests.Single().DeviceId);
        Assert.AreEqual(1, result.SourcesWithPayload.Count);
        Assert.IsTrue(result.SourcesWithPayload.Contains(AudioCaptureSource.Microphone));
        Assert.IsTrue(result.MixedPcmBytes > 0);
        Assert.IsTrue(sink.Sum(chunk => chunk.Length) > 0,
            "Stopping an early-rotated segment must flush already captured partial PCM.");
    }

    [TestMethod]
    [TestCategory("RecordingSmoke")]
    public async Task MediaFoundationSyntheticPcm_ProducesH264WithAacMetadata()
    {
        var root = CreateTempRoot();
        var output = Path.Combine(root, "native-streaming-av.mp4");
        try
        {
            var settings = RecordingSettingsNormalizer.Normalize(new RecordingSettings
            {
                FramesPerSecond = 10,
                IncludeMicrophone = true,
                ShowRecordingBorder = false,
                ShowRecordingTimer = false
            });
            var encoder = new ProductionVideoEncoderSelection(
                ProductionVideoEncoderChoice.SoftwareH264,
                IsAvailable: true,
                IsHardwareAccelerated: false,
                Codec: "H.264",
                Provider: "Media Foundation synthetic smoke",
                Summary: "synthetic H.264 smoke");

            try
            {
                using var session = NativeMediaFoundationMp4Encoder.StartStreamingVideo(
                    output,
                    settings,
                    encoder,
                    width: 320,
                    height: 180,
                    reserveAudioStream: true);
                var audio = SinePcm(frames: StreamingRecordingAudioSession.SampleRate, frequency: 440d);
                for (var frameIndex = 0; frameIndex < 10; frameIndex++)
                {
                    using var frame = SolidFrame(320, 180, frameIndex % 2 == 0 ? Color.Navy : Color.Teal);
                    session.WriteFrame(frame);
                    var offset = frameIndex * audio.Length / 10;
                    var next = (frameIndex + 1) * audio.Length / 10;
                    session.WriteAudioPcm(audio.AsMemory(offset, next - offset));
                }

                var result = session.Complete(includeAudio: true, audioSourceCount: 1);
                Assert.IsTrue(result.Succeeded, result.Message);
                Assert.IsTrue(session.AudioPcmBytes > 0);
            }
            catch (Exception ex) when (ex is InvalidOperationException or COMException)
            {
                Assert.Inconclusive($"Media Foundation H.264/AAC transforms are unavailable on this Windows host: {ex.Message}");
            }

            var probe = await RecordingMediaProbeService.ProbeAsync(output);
            if (probe.Skipped)
            {
                Assert.Inconclusive(probe.Message);
            }

            Assert.IsTrue(probe.Succeeded, probe.Message);
            Assert.AreEqual("h264", probe.VideoCodec);
            Assert.AreEqual(320, probe.Width);
            Assert.AreEqual(180, probe.Height);
            Assert.AreEqual(1, probe.AudioStreamCount);
            Assert.IsTrue(probe.MaxAudioVideoDeltaSeconds.GetValueOrDefault() <= 0.25d, probe.SyncSummary);
            Assert.IsFalse(Directory.EnumerateFiles(root, "*.pcm.tmp", SearchOption.AllDirectories).Any());
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [TestMethod]
    [TestCategory("RecordingSmoke")]
    public async Task FfmpegFallback_RawVideoPipeAndPcmMuxProducePlayableAv()
    {
        var ffmpeg = RecordingService.FindFfmpeg();
        if (string.IsNullOrWhiteSpace(ffmpeg))
        {
            Assert.Inconclusive("FFmpeg is unavailable on this host.");
        }

        var root = CreateTempRoot();
        var videoOnly = Path.Combine(root, "rawvideo-only.mp4");
        var pcm = Path.Combine(root, "audio.pcm.tmp");
        var output = Path.Combine(root, "rawvideo-av.mp4");
        try
        {
            var settings = RecordingSettingsNormalizer.Normalize(new RecordingSettings
            {
                FramesPerSecond = 10,
                ShowRecordingBorder = false,
                ShowRecordingTimer = false
            });
            await using (var session = FfmpegRawVideoSession.Start(
                ffmpeg,
                videoOnly,
                settings,
                "libx264",
                "bundled FFmpeg smoke",
                hardwareAccelerated: false,
                targetBitrateKbps: 1_500,
                width: 320,
                height: 180))
            {
                for (var index = 0; index < 10; index++)
                {
                    using var frame = SolidFrame(320, 180, index % 2 == 0 ? Color.DarkGreen : Color.DarkBlue);
                    await session.WriteFrameAsync(frame);
                }

                var encoded = await session.CompleteAsync();
                Assert.IsTrue(encoded.Succeeded, encoded.Message);
                Assert.AreEqual(10, session.FrameCount);
            }

            await File.WriteAllBytesAsync(
                pcm,
                SinePcm(frames: StreamingRecordingAudioSession.SampleRate, frequency: 523.25d));
            var muxed = await RecordingService.MuxRawPcmIntoMp4Async(
                ffmpeg,
                videoOnly,
                pcm,
                output,
                CancellationToken.None);
            Assert.IsTrue(muxed.Succeeded, muxed.Message);

            var probe = await RecordingMediaProbeService.ProbeAsync(output);
            if (probe.Skipped)
            {
                Assert.Inconclusive(probe.Message);
            }

            Assert.IsTrue(probe.Succeeded, probe.Message);
            Assert.AreEqual("h264", probe.VideoCodec);
            Assert.AreEqual(320, probe.Width);
            Assert.AreEqual(180, probe.Height);
            Assert.AreEqual(1, probe.AudioStreamCount);
            Assert.IsTrue(probe.VideoFrames is null or 10);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static byte[] ConstantPcm(int frames, short value)
    {
        var pcm = new byte[frames * StreamingRecordingAudioSession.BytesPerFrame];
        for (var frame = 0; frame < frames; frame++)
        {
            for (var channel = 0; channel < StreamingRecordingAudioSession.Channels; channel++)
            {
                var offset = frame * StreamingRecordingAudioSession.BytesPerFrame + channel * 2;
                pcm[offset] = (byte)(value & 0xff);
                pcm[offset + 1] = (byte)((value >> 8) & 0xff);
            }
        }

        return pcm;
    }

    private static byte[] SinePcm(int frames, double frequency)
    {
        var pcm = new byte[frames * StreamingRecordingAudioSession.BytesPerFrame];
        for (var frame = 0; frame < frames; frame++)
        {
            var value = (short)(Math.Sin(2d * Math.PI * frequency * frame /
                StreamingRecordingAudioSession.SampleRate) * short.MaxValue * 0.18d);
            for (var channel = 0; channel < StreamingRecordingAudioSession.Channels; channel++)
            {
                var offset = frame * StreamingRecordingAudioSession.BytesPerFrame + channel * 2;
                pcm[offset] = (byte)(value & 0xff);
                pcm[offset + 1] = (byte)((value >> 8) & 0xff);
            }
        }

        return pcm;
    }

    private static Bitmap SolidFrame(int width, int height, Color color)
    {
        var frame = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(frame);
        graphics.Clear(color);
        return frame;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "Receipts.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class PartialStreamingAudioProvider : IAudioCaptureService, IStreamingAudioCaptureService
    {
        public List<StreamingAudioCaptureRequest> Requests { get; } = [];

        public IStreamingAudioCaptureSession StartStreaming(
            StreamingAudioCaptureRequest request,
            Action<StreamingAudioChunk> onChunk,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return new PartialSession(request, onChunk);
        }

        public Task<IReadOnlyList<AudioCaptureDevice>> ListInputDevicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AudioCaptureDevice>>([]);

        public Task<IReadOnlyList<AudioCaptureDevice>> ListLoopbackDevicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AudioCaptureDevice>>([]);

        public Task<AudioCaptureResult> CaptureWavAsync(AudioCaptureRequest request, CancellationToken cancellationToken) =>
            throw new AssertFailedException("Recording must not route through fixed-duration WAV capture.");

        public Task<ProviderHealth> ValidateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderHealth(true, "fixture"));

        private sealed class PartialSession(
            StreamingAudioCaptureRequest request,
            Action<StreamingAudioChunk> onChunk) : IStreamingAudioCaptureSession
        {
            private bool _stopped;
            public AudioCaptureSource Source => request.Source;

            public Task<StreamingAudioCaptureResult> StopAsync(CancellationToken cancellationToken = default)
            {
                if (!_stopped)
                {
                    onChunk(new StreamingAudioChunk(ConstantPcm(512, 2_000), TimeSpan.Zero));
                    _stopped = true;
                }

                return Task.FromResult(new StreamingAudioCaptureResult(
                    true,
                    "partial fixture",
                    TimeSpan.FromMilliseconds(11),
                    512 * StreamingRecordingAudioSession.BytesPerFrame,
                    new AudioCaptureDevice(request.DeviceId, request.DeviceId, false, false)));
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
