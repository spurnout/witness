using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace GoatShot.App.Services;

public sealed class WindowsAudioCaptureService : IAudioCaptureService
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
}
