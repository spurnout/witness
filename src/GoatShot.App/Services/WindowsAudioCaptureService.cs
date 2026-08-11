using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace GoatShot.App.Services;

public sealed class WindowsAudioCaptureService : IAudioCaptureService, IStreamingAudioCaptureService
{
    public async Task<IReadOnlyList<AudioCaptureDevice>> ListInputDevicesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return ListWasapiDevices(DataFlow.Capture, supportsLoopback: false, cancellationToken);
        }
        catch
        {
            var defaultId = SafeDefaultAudioCaptureId();
            return await ListWinRtAudioDevicesAsync(DeviceClass.AudioCapture, defaultId, supportsLoopback: false, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<AudioCaptureDevice>> ListLoopbackDevicesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return ListWasapiDevices(DataFlow.Render, supportsLoopback: true, cancellationToken);
        }
        catch
        {
            var defaultId = SafeDefaultAudioRenderId();
            return await ListWinRtAudioDevicesAsync(DeviceClass.AudioRender, defaultId, supportsLoopback: true, cancellationToken);
        }
    }

    public async Task<AudioCaptureResult> CaptureWavAsync(AudioCaptureRequest request, CancellationToken cancellationToken)
    {
        if (request.Duration <= TimeSpan.Zero)
        {
            return AudioCaptureFailed(request, "Audio capture duration must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            return AudioCaptureFailed(request, "Audio capture output path is required.");
        }

        var outputPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputPath));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var enumerator = new MMDeviceEnumerator();
        MMDevice device;
        try
        {
            device = ResolveWasapiDevice(enumerator, request.Source, request.DeviceId);
        }
        catch (Exception ex)
        {
            return AudioCaptureFailed(request, $"WASAPI device resolution failed: {ex.GetType().Name}: {ex.Message}");
        }

        using (device)
        {
            using WasapiCapture capture = request.Source == AudioCaptureSource.SystemAudio
                ? new WasapiLoopbackCapture(device)
                : new WasapiCapture(device);
            var processing = AudioSampleProcessor.Normalize(request.Processing);
            var processingRequested = AudioSampleProcessor.HasProcessing(processing);
            var processingSupported = AudioSampleProcessor.CanProcess(capture.WaveFormat) || processing.Muted;
            var stopwatch = Stopwatch.StartNew();
            var stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var writeGate = new object();
            long bytesWritten = 0;
            long processedSamples = 0;

            using var writer = new WaveFileWriter(outputPath, capture.WaveFormat);
            capture.DataAvailable += (_, args) =>
            {
                if (args.BytesRecorded <= 0)
                {
                    return;
                }

                var processed = AudioSampleProcessor.ApplyInPlace(
                    args.Buffer,
                    args.BytesRecorded,
                    capture.WaveFormat,
                    processing);

                lock (writeGate)
                {
                    writer.Write(args.Buffer, 0, args.BytesRecorded);
                    bytesWritten += args.BytesRecorded;
                    processedSamples += processed;
                }
            };
            capture.RecordingStopped += (_, args) => stopped.TrySetResult(args.Exception);

            try
            {
                capture.StartRecording();
            }
            catch (Exception ex)
            {
                return AudioCaptureFailed(request, $"WASAPI audio capture could not start: {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                await Task.Delay(request.Duration, cancellationToken);
            }
            catch
            {
                SafeStop(capture);
                try
                {
                    // Drain the WASAPI capture thread before the using blocks dispose the
                    // writer, otherwise a late DataAvailable callback races writer disposal.
                    await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
                }
                catch (TimeoutException)
                {
                    // Best effort: disposal below finalizes whatever was captured.
                }
                throw;
            }

            SafeStop(capture);
            var stopException = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            if (stopException is not null)
            {
                return AudioCaptureFailed(request, $"WASAPI audio capture stopped with an error: {stopException.GetType().Name}: {stopException.Message}");
            }

            lock (writeGate)
            {
                writer.Flush();
            }

            stopwatch.Stop();
            var deviceRecord = CreateDeviceRecord(device, request.Source == AudioCaptureSource.SystemAudio);
            var silentNote = bytesWritten == 0
                ? " No audio payload bytes were captured; the selected device may have been silent."
                : string.Empty;
            var processingNote = processingRequested
                ? processingSupported
                    ? $" Audio processing: {AudioSampleProcessor.Describe(processing)} ({processedSamples} sample(s) touched)."
                    : $" Audio processing requested ({AudioSampleProcessor.Describe(processing)}) but the WASAPI format {capture.WaveFormat.Encoding}/{capture.WaveFormat.BitsPerSample}-bit is not supported for sample processing; raw audio was captured."
                : " Audio processing: disabled.";
            return new AudioCaptureResult(
                Succeeded: true,
                OutputPath: outputPath,
                Message: $"Captured {FormatAudioSource(request.Source)} to WAV from {deviceRecord.DisplayName} for {stopwatch.Elapsed.TotalSeconds:0.0}s ({bytesWritten} audio byte(s)).{silentNote}{processingNote}",
                Duration: stopwatch.Elapsed,
                BytesWritten: bytesWritten,
                Device: deviceRecord);
        }
    }

    /// <summary>
    /// Starts event-driven WASAPI capture and normalizes each callback to the shared
    /// recording PCM contract. No WAV or whole-recording memory buffer is created.
    /// </summary>
    public IStreamingAudioCaptureSession StartStreaming(
        StreamingAudioCaptureRequest request,
        Action<StreamingAudioChunk> onChunk,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(onChunk);
        cancellationToken.ThrowIfCancellationRequested();

        using var enumerator = new MMDeviceEnumerator();
        var device = ResolveWasapiDevice(enumerator, request.Source, request.DeviceId);
        try
        {
            WasapiCapture capture = request.Source == AudioCaptureSource.SystemAudio
                ? new WasapiLoopbackCapture(device)
                : new WasapiCapture(device);
            try
            {
                var session = new WasapiStreamingAudioCaptureSession(
                    request,
                    device,
                    capture,
                    onChunk);
                device = null!;
                return session;
            }
            catch
            {
                capture.Dispose();
                throw;
            }
        }
        finally
        {
            device?.Dispose();
        }
    }

    public async Task<ProviderHealth> ValidateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var inputs = await ListInputDevicesAsync(cancellationToken);
            var loopback = await ListLoopbackDevicesAsync(cancellationToken);
            var meterSummary = SummarizeMeters(inputs, loopback);
            return new ProviderHealth(
                inputs.Count > 0 || loopback.Count > 0,
                $"Windows WASAPI audio discovery found {inputs.Count} microphone input(s) and {loopback.Count} system-audio render endpoint(s). {meterSummary}");
        }
        catch (Exception ex)
        {
            return new ProviderHealth(false, $"Windows audio device discovery failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static IReadOnlyList<AudioCaptureDevice> ListWasapiDevices(
        DataFlow dataFlow,
        bool supportsLoopback,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var enumerator = new MMDeviceEnumerator();
        var defaultId = SafeDefaultEndpointId(enumerator, dataFlow);
        var endpoints = enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active);
        cancellationToken.ThrowIfCancellationRequested();

        return endpoints
            .Select(endpoint => CreateDeviceRecord(endpoint, supportsLoopback, defaultId))
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<IReadOnlyList<AudioCaptureDevice>> ListWinRtAudioDevicesAsync(
        DeviceClass deviceClass,
        string defaultId,
        bool supportsLoopback,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var devices = await DeviceInformation.FindAllAsync(deviceClass);
        cancellationToken.ThrowIfCancellationRequested();

        return devices
            .Select(device => new AudioCaptureDevice(
                device.Id,
                string.IsNullOrWhiteSpace(device.Name) ? device.Id : device.Name,
                IsDefaultDevice(device.Id, defaultId),
                supportsLoopback,
                PeakLevel: null,
                MeterStatus: "WASAPI meter unavailable; listed through WinRT discovery fallback."))
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static MMDevice ResolveWasapiDevice(
        MMDeviceEnumerator enumerator,
        AudioCaptureSource source,
        string deviceId)
    {
        var flow = source == AudioCaptureSource.SystemAudio ? DataFlow.Render : DataFlow.Capture;
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
        }

        var endpoints = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
        foreach (var endpoint in endpoints)
        {
            if (IsDeviceMatch(endpoint, deviceId))
            {
                return endpoint;
            }
        }

        throw new InvalidOperationException($"No active WASAPI {FormatAudioSource(source)} endpoint matched {deviceId}.");
    }

    private static bool IsDeviceMatch(MMDevice endpoint, string requestedId)
    {
        return endpoint.ID.Equals(requestedId, StringComparison.OrdinalIgnoreCase) ||
            endpoint.FriendlyName.Equals(requestedId, StringComparison.OrdinalIgnoreCase) ||
            requestedId.Contains(endpoint.ID, StringComparison.OrdinalIgnoreCase) ||
            endpoint.ID.Contains(requestedId, StringComparison.OrdinalIgnoreCase);
    }

    private static AudioCaptureDevice CreateDeviceRecord(
        MMDevice endpoint,
        bool supportsLoopback,
        string? defaultId = null)
    {
        return new AudioCaptureDevice(
            endpoint.ID,
            string.IsNullOrWhiteSpace(endpoint.FriendlyName) ? endpoint.ID : endpoint.FriendlyName,
            !string.IsNullOrWhiteSpace(defaultId) && endpoint.ID.Equals(defaultId, StringComparison.OrdinalIgnoreCase),
            supportsLoopback,
            SafePeakLevel(endpoint),
            "WASAPI endpoint active.");
    }

    private static double? SafePeakLevel(MMDevice endpoint)
    {
        try
        {
            return Math.Clamp(endpoint.AudioMeterInformation.MasterPeakValue, 0f, 1f);
        }
        catch
        {
            return null;
        }
    }

    private static string SafeDefaultEndpointId(MMDeviceEnumerator enumerator, DataFlow dataFlow)
    {
        try
        {
            using var endpoint = enumerator.GetDefaultAudioEndpoint(dataFlow, Role.Multimedia);
            return endpoint.ID;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsDefaultDevice(string id, string defaultId)
    {
        return !string.IsNullOrWhiteSpace(defaultId) &&
            id.Equals(defaultId, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeDefaultAudioCaptureId()
    {
        try
        {
            return MediaDevice.GetDefaultAudioCaptureId(AudioDeviceRole.Default) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SafeDefaultAudioRenderId()
    {
        try
        {
            return MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SummarizeMeters(
        IReadOnlyList<AudioCaptureDevice> inputs,
        IReadOnlyList<AudioCaptureDevice> loopback)
    {
        var metered = inputs.Concat(loopback).Where(device => device.PeakLevel.HasValue).ToList();
        if (metered.Count == 0)
        {
            return "No WASAPI peak meters were available.";
        }

        var loudest = metered
            .OrderByDescending(device => device.PeakLevel.GetValueOrDefault())
            .First();
        return $"Peak meters available for {metered.Count} endpoint(s); current loudest endpoint is {loudest.DisplayName} at {loudest.PeakLevel.GetValueOrDefault():P0}.";
    }

    private static string FormatAudioSource(AudioCaptureSource source)
    {
        return source == AudioCaptureSource.SystemAudio ? "system-audio loopback" : "microphone";
    }

    private static void SafeStop(WasapiCapture capture)
    {
        try
        {
            capture.StopRecording();
        }
        catch
        {
            // Stop is best-effort once cancellation or a normal duration boundary is reached.
        }
    }

    private static AudioCaptureResult AudioCaptureFailed(AudioCaptureRequest request, string message)
    {
        return new AudioCaptureResult(
            Succeeded: false,
            OutputPath: null,
            Message: message,
            Duration: request.Duration,
            BytesWritten: 0,
            Device: null);
    }

    private sealed class WasapiStreamingAudioCaptureSession : IStreamingAudioCaptureSession
    {
        private readonly object _gate = new();
        private readonly MMDevice _device;
        private readonly WasapiCapture _capture;
        private readonly Action<StreamingAudioChunk> _onChunk;
        private readonly StreamingPcmNormalizer _normalizer;
        private readonly AudioCaptureProcessingSettings _processing;
        private readonly bool _processingSupported;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly TaskCompletionSource<Exception?> _stopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private StreamingAudioCaptureResult? _result;
        private Exception? _callbackException;
        private long _bytesProduced;
        private long _processedSamples;
        private int _stopRequested;
        private bool _disposed;

        public WasapiStreamingAudioCaptureSession(
            StreamingAudioCaptureRequest request,
            MMDevice device,
            WasapiCapture capture,
            Action<StreamingAudioChunk> onChunk)
        {
            Source = request.Source;
            _device = device;
            _capture = capture;
            _onChunk = onChunk;
            _normalizer = new StreamingPcmNormalizer(capture.WaveFormat);
            _processing = AudioSampleProcessor.Normalize(request.Processing);
            _processingSupported = AudioSampleProcessor.CanProcess(capture.WaveFormat) || _processing.Muted;

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            try
            {
                _capture.StartRecording();
            }
            catch
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                throw;
            }
        }

        public AudioCaptureSource Source { get; }

        public async Task<StreamingAudioCaptureResult> StopAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_result is not null)
                {
                    return _result;
                }
            }

            if (Interlocked.Exchange(ref _stopRequested, 1) == 0)
            {
                SafeStop(_capture);
            }

            Exception? stopException;
            try
            {
                stopException = await _stopped.Task
                    .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                stopException = ex;
            }

            lock (_gate)
            {
                if (_result is not null)
                {
                    return _result;
                }

                try
                {
                    Emit(_normalizer.Flush());
                }
                catch (Exception ex)
                {
                    _callbackException ??= ex;
                }

                _stopwatch.Stop();
                var device = CreateDeviceRecord(
                    _device,
                    Source == AudioCaptureSource.SystemAudio);
                var failure = _callbackException ?? stopException;
                var processing = AudioSampleProcessor.HasProcessing(_processing)
                    ? _processingSupported
                        ? $" Processing: {AudioSampleProcessor.Describe(_processing)} ({_processedSamples} sample(s) touched)."
                        : $" Processing requested but unsupported by {_capture.WaveFormat.Encoding}/{_capture.WaveFormat.BitsPerSample}-bit input; raw samples were normalized."
                    : " Processing: disabled.";
                var partial = _bytesProduced > 0 ? " Captured PCM remains usable as a partial segment." : string.Empty;
                _result = new StreamingAudioCaptureResult(
                    failure is null,
                    failure is null
                        ? $"Streamed {FormatAudioSource(Source)} from {device.DisplayName} ({_bytesProduced} PCM byte(s)).{processing}"
                        : $"Streaming {FormatAudioSource(Source)} stopped with {failure.GetType().Name}: {failure.Message}.{partial}",
                    _stopwatch.Elapsed,
                    _bytesProduced,
                    device);
                return _result;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                await StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.Dispose();
                _device.Dispose();
                _disposed = true;
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs args)
        {
            if (args.BytesRecorded <= 0 || Volatile.Read(ref _stopRequested) != 0)
            {
                return;
            }

            try
            {
                var processed = AudioSampleProcessor.ApplyInPlace(
                    args.Buffer,
                    args.BytesRecorded,
                    _capture.WaveFormat,
                    _processing);
                var chunks = _normalizer.Push(args.Buffer, args.BytesRecorded);
                lock (_gate)
                {
                    _processedSamples += processed;
                    Emit(chunks);
                }
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _callbackException ??= ex;
                }

                if (Interlocked.Exchange(ref _stopRequested, 1) == 0)
                {
                    SafeStop(_capture);
                }
            }
        }

        private void Emit(IReadOnlyList<StreamingAudioChunk> chunks)
        {
            foreach (var chunk in chunks)
            {
                _onChunk(chunk);
                _bytesProduced += chunk.Pcm16.Length;
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs args) =>
            _stopped.TrySetResult(args.Exception);
    }
}
