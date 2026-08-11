namespace GoatShot.App.Models;

public static class ReceiptManifestSchemas
{
    public const string V1 = "receipts.receipt.v1";
}

public sealed class ReceiptManifest
{
    public string Schema { get; set; } = ReceiptManifestSchemas.V1;
    public string ReceiptId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset FinalizedAtUtc { get; set; }
    public ReceiptApplicationManifest Application { get; set; } = new();
    public ReceiptCaptureSettingsManifest CaptureSettings { get; set; } = new();
    public List<ReceiptTrackManifest> Tracks { get; set; } = [];
    public List<ReceiptSegmentManifest> Segments { get; set; } = [];
    public List<ReceiptArtifactManifest> Artifacts { get; set; } = [];
    public ReceiptSignatureManifest? Signature { get; set; }
}

public sealed class ReceiptApplicationManifest
{
    public string ProductName { get; set; } = "Receipts";
    public string Version { get; set; } = string.Empty;
    public string Build { get; set; } = string.Empty;
}

public sealed class ReceiptCaptureSettingsManifest
{
    public string RecordingMode { get; set; } = "replay";
    public string TargetStrategy { get; set; } = string.Empty;
    public string VideoCodec { get; set; } = "h264";
    public int FramesPerSecond { get; set; }
    public int VideoBitrateBitsPerSecond { get; set; }
    public bool IncludeCursor { get; set; }
    public bool IncludeSystemAudio { get; set; }
    public bool IncludeMicrophone { get; set; }
    public bool IncludeWebcam { get; set; }
    public Dictionary<string, string> AdditionalSettings { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ReceiptTrackManifest
{
    public string TrackId { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ReceiptCaptureBoundsManifest Bounds { get; set; } = new();
    public int DpiX { get; set; } = 96;
    public int DpiY { get; set; } = 96;
    public List<ReceiptSourceTransitionManifest> SourceTransitions { get; set; } = [];
}

public sealed class ReceiptSourceTransitionManifest
{
    public string SourceKind { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public DateTimeOffset CapturedAtUtc { get; set; }
    public long EffectiveStartMonotonicTicks { get; set; }
    public ReceiptCaptureBoundsManifest Bounds { get; set; } = new();
    public int DpiX { get; set; } = 96;
    public int DpiY { get; set; } = 96;
}

public sealed class ReceiptCaptureBoundsManifest
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class ReceiptSegmentManifest
{
    public string SegmentId { get; set; } = string.Empty;
    public string TrackId { get; set; } = string.Empty;
    public long SequenceNumber { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public string MediaType { get; set; } = "video/mp4";
    public DateTimeOffset CapturedAtUtc { get; set; }
    public long StartMonotonicTicks { get; set; }
    public long DurationTicks { get; set; }
    public bool IncludesSystemAudio { get; set; }
    public bool IncludesMicrophone { get; set; }
    public bool IncludesWebcam { get; set; }
    public int EncodedFrameCount { get; set; }
    public int WebcamFrameCount { get; set; }
    public bool PrivacyRedacted { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string PreviousChainSha256 { get; set; } = string.Empty;
    public string ChainSha256 { get; set; } = string.Empty;
}

public sealed class ReceiptArtifactManifest
{
    public string ArtifactId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string MediaType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string? SourceArtifactId { get; set; }
}

public sealed class ReceiptSignatureManifest
{
    public string Algorithm { get; set; } = ReceiptSignatureAlgorithms.EcdsaP256Sha256;
    public string Canonicalization { get; set; } = ReceiptSignatureAlgorithms.CanonicalJsonV1;
    public string KeyFingerprintSha256 { get; set; } = string.Empty;
    public string PublicKeySpkiBase64 { get; set; } = string.Empty;
    public string SignatureBase64 { get; set; } = string.Empty;
}

public static class ReceiptSignatureAlgorithms
{
    public const string EcdsaP256Sha256 = "ecdsa-p256-sha256";
    public const string CanonicalJsonV1 = "receipts.canonical-json.v1";
}
