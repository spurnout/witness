using System.Collections.Concurrent;

namespace GoatShot.App.Services;

/// <summary>
/// Shared bounded microphone/system-audio mixer used by Record Now and Replay.
/// </summary>
internal sealed class StreamingRecordingAudioSession : IAsyncDisposable
{
    public const int SampleRate = 48_000;
    public const int Channels = 2;
    public const int BitsPerSample = 16;
    public const int BytesPerFrame = Channels * (BitsPerSample / 8);
    internal const int MixChunkFrames = 2_048;
    internal const int MaxLeadFrames = SampleRate / 4;

    private readonly object _gate = new();
    private readonly Action<ReadOnlyMemory<byte>> _onMixedPcm;
    private readonly BoundedPcmMixer _mixer;
    private readonly List<IStreamingAudioCaptureSession> _captures = [];
    private readonly ConcurrentQueue<string> _issues = new();
    private readonly SemaphoreSlim _stopGate = new(1, 1);
    private bool _stopped;

    private StreamingRecordingAudioSession(
        IReadOnlyCollection<AudioCaptureSource> expectedSources,
        Action<ReadOnlyMemory<byte>> onMixedPcm)
    {
        _onMixedPcm = onMixedPcm;
        _mixer = new BoundedPcmMixer(expectedSources, EmitMixedPcm);
    }

    public static StreamingRecordingAudioSession Start(
        IAudioCaptureService audioCapture,
        IReadOnlyList<StreamingAudioCaptureRequest> requests,
        Action<ReadOnlyMemory<byte>> onMixedPcm,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioCapture);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(onMixedPcm);
        cancellationToken.ThrowIfCancellationRequested();

        var session = new StreamingRecordingAudioSession(
            requests.Select(request => request.Source).Distinct().ToArray(),
            onMixedPcm);
        if (requests.Count == 0)
        {
            return session;
        }

        if (audioCapture is not IStreamingAudioCaptureService streaming)
        {
            session._issues.Enqueue(
                "The selected audio provider does not implement event-driven streaming PCM capture; requested audio was omitted.");
            return session;
        }

        foreach (var request in requests)
        {
            try
            {
                var capture = streaming.StartStreaming(
                    request,
                    chunk => session.Accept(request.Source, chunk),
                    cancellationToken);
                session._captures.Add(capture);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                session._issues.Enqueue(
                    $"{FormatSource(request.Source)} streaming capture could not start ({ex.GetType().Name}: {ex.Message}).");
                session._mixer.MarkSourceUnavailable(request.Source);
            }
        }

        return session;
    }

    public async Task<StreamingRecordingAudioResult> StopAsync(
        CancellationToken cancellationToken = default)
    {
        await _stopGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stopped)
            {
                return BuildResult();
            }

            foreach (var capture in _captures)
            {
                try
                {
                    var result = await capture.StopAsync(cancellationToken).ConfigureAwait(false);
                    lock (_gate)
                    {
                        _mixer.MarkSourceComplete(capture.Source, result.PcmBytesProduced > 0);
                    }

                    if (!result.Succeeded)
                    {
                        _issues.Enqueue($"{FormatSource(capture.Source)} capture stopped with an error ({result.Message}).");
                    }
                    else if (result.PcmBytesProduced == 0)
                    {
                        _issues.Enqueue($"{FormatSource(capture.Source)} produced no PCM samples and was omitted.");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lock (_gate)
                    {
                        _mixer.MarkSourceComplete(capture.Source, producedPayload: false);
                    }

                    _issues.Enqueue(
                        $"{FormatSource(capture.Source)} capture failed while stopping ({ex.GetType().Name}: {ex.Message}).");
                }
            }

            lock (_gate)
            {
                _mixer.Complete();
                _stopped = true;
                return BuildResult();
            }
        }
        finally
        {
            _stopGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Best effort: caller reports the causal recording failure.
        }

        foreach (var capture in _captures)
        {
            await capture.DisposeAsync().ConfigureAwait(false);
        }

        _stopGate.Dispose();
    }

    private void Accept(AudioCaptureSource source, StreamingAudioChunk chunk)
    {
        if (chunk.Pcm16.IsEmpty)
        {
            return;
        }

        if (chunk.SampleRate != SampleRate || chunk.Channels != Channels ||
            chunk.BitsPerSample != BitsPerSample)
        {
            _issues.Enqueue(
                $"{FormatSource(source)} emitted an unsupported PCM format; the chunk was omitted.");
            return;
        }

        lock (_gate)
        {
            if (!_stopped)
            {
                _mixer.Add(source, chunk.Pcm16.Span);
            }
        }
    }

    private void EmitMixedPcm(ReadOnlyMemory<byte> pcm)
    {
        try
        {
            _onMixedPcm(pcm);
        }
        catch (Exception ex)
        {
            _issues.Enqueue($"The streaming audio encoder rejected a PCM chunk ({ex.GetType().Name}: {ex.Message}).");
            throw;
        }
    }

    private StreamingRecordingAudioResult BuildResult() => new(
        _mixer.SourcesWithPayload.ToArray(),
        _mixer.MixedBytesEmitted,
        _issues.ToArray());

    private static string FormatSource(AudioCaptureSource source) =>
        source == AudioCaptureSource.SystemAudio ? "System audio" : "Microphone audio";

    internal sealed class BoundedPcmMixer
    {
        private readonly Dictionary<AudioCaptureSource, Queue<short>> _queues;
        private readonly HashSet<AudioCaptureSource> _completed = [];
        private readonly HashSet<AudioCaptureSource> _sourcesWithPayload = [];
        private readonly Action<ReadOnlyMemory<byte>> _emit;

        public BoundedPcmMixer(
            IReadOnlyCollection<AudioCaptureSource> expectedSources,
            Action<ReadOnlyMemory<byte>> emit)
        {
            _queues = expectedSources
                .Distinct()
                .ToDictionary(source => source, _ => new Queue<short>());
            _emit = emit;
        }

        public IReadOnlySet<AudioCaptureSource> SourcesWithPayload => _sourcesWithPayload;
        public long MixedBytesEmitted { get; private set; }

        public void Add(AudioCaptureSource source, ReadOnlySpan<byte> pcm16)
        {
            if (!_queues.TryGetValue(source, out var queue))
            {
                return;
            }

            var byteCount = pcm16.Length - pcm16.Length % BytesPerFrame;
            if (byteCount == 0)
            {
                return;
            }

            _sourcesWithPayload.Add(source);
            for (var offset = 0; offset < byteCount; offset += 2)
            {
                queue.Enqueue((short)(pcm16[offset] | pcm16[offset + 1] << 8));
            }

            Drain(force: false);
        }

        public void MarkSourceUnavailable(AudioCaptureSource source)
        {
            _completed.Add(source);
            Drain(force: false);
        }

        public void MarkSourceComplete(AudioCaptureSource source, bool producedPayload)
        {
            if (producedPayload)
            {
                _sourcesWithPayload.Add(source);
            }

            _completed.Add(source);
            Drain(force: false);
        }

        public void Complete()
        {
            foreach (var source in _queues.Keys)
            {
                _completed.Add(source);
            }

            Drain(force: true);
        }

        private void Drain(bool force)
        {
            if (_queues.Count == 0)
            {
                return;
            }

            while (true)
            {
                var availableFrames = _queues.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Count / Channels);
                var maximum = availableFrames.Values.DefaultIfEmpty().Max();
                if (maximum == 0)
                {
                    return;
                }

                var allReady = _queues.Keys.All(source =>
                    availableFrames[source] >= MixChunkFrames ||
                    _completed.Contains(source));
                var leadLimitReached = maximum >= MaxLeadFrames;
                if (!force && !allReady && !leadLimitReached)
                {
                    return;
                }

                var frames = Math.Min(MixChunkFrames, maximum);
                var output = new byte[frames * BytesPerFrame];
                for (var frame = 0; frame < frames; frame++)
                {
                    var contributors = 0;
                    var left = 0;
                    var right = 0;
                    foreach (var queue in _queues.Values)
                    {
                        if (queue.Count < Channels)
                        {
                            continue;
                        }

                        left += queue.Dequeue();
                        right += queue.Dequeue();
                        contributors++;
                    }

                    if (contributors > 0)
                    {
                        left = Math.Clamp(left / contributors, short.MinValue, short.MaxValue);
                        right = Math.Clamp(right / contributors, short.MinValue, short.MaxValue);
                    }

                    var offset = frame * BytesPerFrame;
                    output[offset] = (byte)(left & 0xff);
                    output[offset + 1] = (byte)((left >> 8) & 0xff);
                    output[offset + 2] = (byte)(right & 0xff);
                    output[offset + 3] = (byte)((right >> 8) & 0xff);
                }

                _emit(output);
                MixedBytesEmitted += output.Length;
            }
        }
    }
}

internal sealed record StreamingRecordingAudioResult(
    IReadOnlyCollection<AudioCaptureSource> SourcesWithPayload,
    long MixedPcmBytes,
    IReadOnlyList<string> Issues)
{
    public string Message => SourcesWithPayload.Count == 0
        ? Issues.Count == 0 ? "not requested." : string.Join(" ", Issues)
        : $"Streamed {MixedPcmBytes} mixed PCM byte(s) from " +
          string.Join(" and ", SourcesWithPayload.Select(source =>
              source == AudioCaptureSource.SystemAudio ? "system audio" : "microphone audio")) +
          "." + (Issues.Count == 0 ? string.Empty : " " + string.Join(" ", Issues));
}
