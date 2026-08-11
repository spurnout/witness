using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

/// <summary>
/// Publishes a pinned replay snapshot into a complete signed package before the
/// final directory is made visible to the library.
/// </summary>
public sealed class ReplayReceiptPackagePublisher : IReplaySnapshotPublisher
{
    private readonly FileReplayBufferStorage _storage;
    private readonly ReceiptIntegrityService _integrity;
    private readonly ReceiptDeviceKeyService _deviceKeys;
    private readonly string _deviceKeyPath;
    private readonly AppSettings _settings;

    public ReplayReceiptPackagePublisher(
        FileReplayBufferStorage storage,
        ReceiptIntegrityService integrity,
        ReceiptDeviceKeyService deviceKeys,
        string deviceKeyPath,
        AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(integrity);
        ArgumentNullException.ThrowIfNull(deviceKeys);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKeyPath);
        ArgumentNullException.ThrowIfNull(settings);

        _storage = storage;
        _integrity = integrity;
        _deviceKeys = deviceKeys;
        _deviceKeyPath = Path.GetFullPath(deviceKeyPath);
        _settings = settings;
    }

    public async Task<ReplaySnapshotPublishResult> PublishAsync(
        ReplaySnapshotPublication publication,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(publication);
        var destination = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(publication.DestinationDirectory));
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException($"Replay receipt destination already exists: {destination}");
        }

        var parent = Directory.GetParent(destination)
            ?? throw new IOException($"Replay receipt destination has no parent directory: {destination}");
        Directory.CreateDirectory(parent.FullName);
        var staging = Path.Combine(
            parent.FullName,
            $".{Path.GetFileName(destination)}.signing-{Guid.NewGuid():N}");

        try
        {
            var stagedPublication = publication with { DestinationDirectory = staging };
            var staged = await _storage.PublishAsync(stagedPublication, cancellationToken)
                .ConfigureAwait(false);

            using var signingKey = _deviceKeys.CaptureActiveSigningKey(_deviceKeyPath);
            var key = signingKey.KeyInfo;
            var publicKeyPath = Path.Combine(staging, "public-key.pem");
            await File.WriteAllTextAsync(
                publicKeyPath,
                ToPublicKeyPem(key.PublicKeySpkiBase64),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);

            var thumbnailPath = Path.Combine(staging, "thumbnail.png");
            await CreateThumbnailAsync(staged.Segments[0].FullPath, thumbnailPath, cancellationToken)
                .ConfigureAwait(false);

            var manifest = BuildManifest(publication, staged);
            manifest.Artifacts.Add(new ReceiptArtifactManifest
            {
                ArtifactId = "device-public-key",
                Role = "public-verification-key",
                RelativePath = "public-key.pem",
                MediaType = "application/x-pem-file"
            });
            manifest.Artifacts.Add(new ReceiptArtifactManifest
            {
                ArtifactId = "thumbnail",
                Role = "thumbnail",
                RelativePath = "thumbnail.png",
                MediaType = "image/png"
            });
            var sealedManifest = await _integrity.SealAndWriteWithKeyAsync(
                manifest,
                staging,
                signingKey,
                cancellationToken).ConfigureAwait(false);
            await ValidatePublicKeyArtifactMatchesSignatureAsync(
                publicKeyPath,
                sealedManifest,
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(staging, destination);
            return new ReplaySnapshotPublishResult(
                publication.ReceiptId,
                destination,
                staged.Segments.Select(segment => new ReplayPublishedSegment(
                    segment.SegmentId,
                    segment.TrackId,
                    segment.RelativePath,
                    Path.Combine(destination, segment.RelativePath),
                    segment.ByteLength)).ToArray());
        }
        catch
        {
            TryDeleteOwnedStagingDirectory(staging, parent.FullName);
            throw;
        }
    }

    private ReceiptManifest BuildManifest(
        ReplaySnapshotPublication publication,
        ReplaySnapshotPublishResult staged)
    {
        var normalizedRecording = RecordingSettingsNormalizer.Normalize(
            _settings.Recording,
            _settings.Replay.FramesPerSecond);
        var ordered = publication.Segments
            .OrderBy(segment => segment.MonotonicStart)
            .ThenBy(segment => segment.TrackId, StringComparer.Ordinal)
            .ThenBy(segment => segment.SequenceNumber)
            .ToArray();
        var systemAudioSegmentCount = ordered.Count(segment => segment.IncludesSystemAudio);
        var microphoneSegmentCount = ordered.Count(segment => segment.IncludesMicrophone);
        var webcamSegmentCount = ordered.Count(segment => segment.IncludesWebcam);
        var allSegmentsHaveSystemAudio = ordered.Length > 0 &&
            systemAudioSegmentCount == ordered.Length;
        var allSegmentsHaveMicrophone = ordered.Length > 0 &&
            microphoneSegmentCount == ordered.Length;
        var allSegmentsHaveWebcam = ordered.Length > 0 &&
            webcamSegmentCount == ordered.Length;
        var relativeBySegmentId = staged.Segments.ToDictionary(
            segment => segment.SegmentId,
            segment => segment.RelativePath,
            StringComparer.Ordinal);
        var assembly = Assembly.GetEntryAssembly() ?? typeof(ReplayReceiptPackagePublisher).Assembly;
        var version = assembly.GetName().Version?.ToString(3) ?? BrandIdentity.ReleaseVersion;
        var build = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? version;

        var manifest = new ReceiptManifest
        {
            ReceiptId = publication.ReceiptId,
            CreatedAtUtc = ordered.Min(segment => segment.StartedAtUtc),
            FinalizedAtUtc = DateTimeOffset.UtcNow,
            Application = new ReceiptApplicationManifest
            {
                ProductName = BrandIdentity.ProductName,
                Version = version,
                Build = build
            },
            CaptureSettings = new ReceiptCaptureSettingsManifest
            {
                RecordingMode = "replay",
                TargetStrategy = _settings.Replay.CaptureSource.Kind.ToString(),
                VideoCodec = normalizedRecording.PreferHevcEncoding ? "hevc" : "h264",
                FramesPerSecond = normalizedRecording.FramesPerSecond,
                VideoBitrateBitsPerSecond = Math.Max(0, normalizedRecording.BitrateKbps) * 1_000,
                IncludeCursor = _settings.IncludeCursor,
                // A top-level true value means every finalized MP4 in this receipt has
                // that stream/overlay. Per-segment fields preserve partial device coverage.
                IncludeSystemAudio = allSegmentsHaveSystemAudio,
                IncludeMicrophone = allSegmentsHaveMicrophone,
                IncludeWebcam = allSegmentsHaveWebcam,
                AdditionalSettings = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["bufferDurationTicks"] = _settings.Replay.BufferDuration.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["segmentDurationTicks"] = _settings.Replay.SegmentDuration.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["storageCapBytes"] = _settings.Replay.MaxBufferBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["localWallClockOffset"] = DateTimeOffset.Now.Offset.ToString(),
                    ["monotonicClock"] = "Stopwatch.GetTimestamp",
                    ["sceneIndexingEnabled"] = _settings.Replay.EnableSceneIndexing.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["localOcrComparisonEnabled"] = _settings.Replay.EnableLocalOcrIndexing.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["analysisSensitivity"] = _settings.Replay.AnalysisSensitivity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["requestedSystemAudio"] = normalizedRecording.IncludeSystemAudio.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["requestedMicrophone"] = normalizedRecording.IncludeMicrophone.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["requestedWebcam"] = normalizedRecording.EnableWebcamOverlay.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["requestedRecordingBorder"] = normalizedRecording.ShowRecordingBorder.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["requestedRecordingTimer"] = normalizedRecording.ShowRecordingTimer.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["requestedKeystrokeOverlay"] = normalizedRecording.ShowKeystrokeOverlay.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["finalizedSegmentTrackCount"] = ordered.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["systemAudioSegmentTrackCount"] = systemAudioSegmentCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["microphoneSegmentTrackCount"] = microphoneSegmentCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["webcamSegmentTrackCount"] = webcamSegmentCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["captureLimitations"] = BuildCaptureLimitations(
                        normalizedRecording,
                        ordered.Length,
                        systemAudioSegmentCount,
                        microphoneSegmentCount,
                        webcamSegmentCount)
                }
            }
        };

        foreach (var trackGroup in ordered.GroupBy(segment => segment.TrackId, StringComparer.Ordinal))
        {
            var first = trackGroup.First();
            var transitions = new List<ReceiptSourceTransitionManifest>();
            ReplayTrackDescriptor? previous = null;
            foreach (var segment in trackGroup.OrderBy(segment => segment.MonotonicStart))
            {
                if (previous is not null && SameSource(previous, segment.Track))
                {
                    continue;
                }

                transitions.Add(new ReceiptSourceTransitionManifest
                {
                    SourceKind = segment.Track.Source.Kind.ToString(),
                    SourceId = segment.Track.Source.SourceId,
                    CapturedAtUtc = segment.StartedAtUtc,
                    EffectiveStartMonotonicTicks = segment.MonotonicStart.Ticks,
                    Bounds = ToManifestBounds(segment.Track.Source.Bounds, segment.Track.PixelWidth, segment.Track.PixelHeight),
                    DpiX = Math.Max(1, (int)Math.Round(96d * segment.Track.DpiScaleX)),
                    DpiY = Math.Max(1, (int)Math.Round(96d * segment.Track.DpiScaleY))
                });
                previous = segment.Track;
            }

            manifest.Tracks.Add(new ReceiptTrackManifest
            {
                TrackId = first.TrackId,
                SourceKind = first.Track.Source.Kind.ToString(),
                SourceId = first.Track.Source.SourceId,
                DisplayName = first.Track.DisplayName,
                Bounds = ToManifestBounds(first.Track.Source.Bounds, first.Track.PixelWidth, first.Track.PixelHeight),
                DpiX = Math.Max(1, (int)Math.Round(96d * first.Track.DpiScaleX)),
                DpiY = Math.Max(1, (int)Math.Round(96d * first.Track.DpiScaleY)),
                SourceTransitions = transitions
            });
        }

        foreach (var segment in ordered)
        {
            manifest.Segments.Add(new ReceiptSegmentManifest
            {
                SegmentId = segment.SegmentId,
                TrackId = segment.TrackId,
                SequenceNumber = segment.SequenceNumber,
                RelativePath = relativeBySegmentId[segment.SegmentId],
                CapturedAtUtc = segment.StartedAtUtc,
                StartMonotonicTicks = segment.MonotonicStart.Ticks,
                DurationTicks = segment.Duration.Ticks,
                IncludesSystemAudio = segment.IncludesSystemAudio,
                IncludesMicrophone = segment.IncludesMicrophone,
                IncludesWebcam = segment.IncludesWebcam,
                EncodedFrameCount = segment.EncodedFrameCount,
                WebcamFrameCount = segment.WebcamFrameCount,
                PrivacyRedacted = segment.PrivacyRedacted
            });
        }

        return manifest;
    }

    internal static string BuildCaptureLimitations(
        NormalizedRecordingSettings settings,
        int segmentCount,
        int systemAudioSegmentCount,
        int microphoneSegmentCount,
        int webcamSegmentCount)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var limitations = new List<string>();
        AddCoverageLimitation(
            limitations,
            "system audio",
            settings.IncludeSystemAudio,
            systemAudioSegmentCount,
            segmentCount);
        AddCoverageLimitation(
            limitations,
            "microphone audio",
            settings.IncludeMicrophone,
            microphoneSegmentCount,
            segmentCount);
        AddCoverageLimitation(
            limitations,
            "webcam overlay",
            settings.EnableWebcamOverlay,
            webcamSegmentCount,
            segmentCount);

        return limitations.Count == 0
            ? "No requested Replay media input was omitted from the finalized segment tracks."
            : string.Join(" ", limitations);
    }

    private static void AddCoverageLimitation(
        ICollection<string> limitations,
        string label,
        bool requested,
        int coveredSegments,
        int segmentCount)
    {
        if (!requested || (segmentCount > 0 && coveredSegments >= segmentCount))
        {
            return;
        }

        limitations.Add(
            $"Requested {label} is present in {Math.Max(0, coveredSegments)} of " +
            $"{Math.Max(0, segmentCount)} finalized segment tracks.");
    }

    private static ReceiptCaptureBoundsManifest ToManifestBounds(
        ReplayCaptureBounds? bounds,
        int fallbackWidth,
        int fallbackHeight) => new()
    {
        X = bounds?.X ?? 0,
        Y = bounds?.Y ?? 0,
        Width = Math.Max(1, bounds?.Width ?? fallbackWidth),
        Height = Math.Max(1, bounds?.Height ?? fallbackHeight)
    };

    private static bool SameSource(ReplayTrackDescriptor left, ReplayTrackDescriptor right) =>
        left.Source.Kind == right.Source.Kind &&
        left.Source.SourceId.Equals(right.Source.SourceId, StringComparison.Ordinal) &&
        left.Source.Bounds == right.Source.Bounds &&
        left.PixelWidth == right.PixelWidth &&
        left.PixelHeight == right.PixelHeight &&
        Math.Abs(left.DpiScaleX - right.DpiScaleX) < 0.001d &&
        Math.Abs(left.DpiScaleY - right.DpiScaleY) < 0.001d;

    private static string ToPublicKeyPem(string publicKeySpkiBase64)
    {
        var lines = Enumerable.Range(0, (publicKeySpkiBase64.Length + 63) / 64)
            .Select(index => publicKeySpkiBase64.Substring(
                index * 64,
                Math.Min(64, publicKeySpkiBase64.Length - (index * 64))));
        return $"-----BEGIN PUBLIC KEY-----\n{string.Join('\n', lines)}\n-----END PUBLIC KEY-----\n";
    }

    internal static async Task ValidatePublicKeyArtifactMatchesSignatureAsync(
        string publicKeyPath,
        ReceiptManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPath);
        ArgumentNullException.ThrowIfNull(manifest);
        var signature = manifest.Signature
            ?? throw new InvalidDataException("The signed receipt manifest has no signature metadata.");
        var pem = await File.ReadAllTextAsync(publicKeyPath, cancellationToken)
            .ConfigureAwait(false);

        byte[] publicKeySpki;
        try
        {
            using var publicKey = ECDsa.Create();
            publicKey.ImportFromPem(pem);
            if (!ReceiptDeviceKeyService.IsP256(publicKey))
            {
                throw new InvalidDataException("The receipt public-key artifact is not an ECDSA P-256 key.");
            }

            publicKeySpki = publicKey.ExportSubjectPublicKeyInfo();
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            throw new InvalidDataException("The receipt public-key artifact is invalid.", ex);
        }

        var artifactPublicKey = Convert.ToBase64String(publicKeySpki);
        var artifactFingerprint = ReceiptDeviceKeyService.ComputeFingerprint(publicKeySpki);
        if (!artifactPublicKey.Equals(signature.PublicKeySpkiBase64, StringComparison.Ordinal) ||
            !artifactFingerprint.Equals(signature.KeyFingerprintSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The receipt public-key artifact does not match the key that signed the manifest.");
        }
    }

    private static async Task CreateThumbnailAsync(
        string videoPath,
        string thumbnailPath,
        CancellationToken cancellationToken)
    {
        var ffmpeg = RecordingService.FindFfmpeg();
        if (!string.IsNullOrWhiteSpace(ffmpeg))
        {
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                start.ArgumentList.Add("-v");
                start.ArgumentList.Add("error");
                start.ArgumentList.Add("-y");
                start.ArgumentList.Add("-i");
                start.ArgumentList.Add(videoPath);
                start.ArgumentList.Add("-frames:v");
                start.ArgumentList.Add("1");
                start.ArgumentList.Add(thumbnailPath);
                using var process = Process.Start(start);
                if (process is not null)
                {
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                    if (process.ExitCode == 0 && File.Exists(thumbnailPath))
                    {
                        return;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A deterministic local placeholder keeps the package complete.
            }
        }

        using var bitmap = new Bitmap(640, 360);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(8, 16, 22));
        using var accent = new SolidBrush(Color.FromArgb(48, 230, 195));
        using var text = new SolidBrush(Color.White);
        using var font = new Font("Segoe UI", 30, FontStyle.Bold);
        graphics.FillRectangle(accent, 44, 52, 14, 256);
        graphics.DrawString("Replay receipt", font, text, 86, 136);
        bitmap.Save(thumbnailPath, ImageFormat.Png);
    }

    private static void TryDeleteOwnedStagingDirectory(string staging, string parent)
    {
        try
        {
            var fullStaging = Path.GetFullPath(staging);
            var fullParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
            if (fullStaging.StartsWith(fullParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(fullStaging).StartsWith(".", StringComparison.Ordinal) &&
                Directory.Exists(fullStaging))
            {
                Directory.Delete(fullStaging, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Startup cleanup can remove a staging directory left after a crash.
        }
    }
}
