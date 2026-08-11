namespace GoatShot.App.Models;

public enum ReceiptVerificationStatus
{
    IntactKnownDevice,
    IntactUnknownDevice,
    Modified,
    Incomplete,
    Unverifiable
}

public sealed class ReceiptVerificationResult
{
    public ReceiptVerificationStatus Status { get; init; }
    public string ReceiptId { get; init; } = string.Empty;
    public string SignerFingerprintSha256 { get; init; } = string.Empty;
    public List<string> Issues { get; init; } = [];

    public bool IsIntact => Status is ReceiptVerificationStatus.IntactKnownDevice or
        ReceiptVerificationStatus.IntactUnknownDevice;
}

public sealed class ReceiptDeviceKeyInfo
{
    public string FingerprintSha256 { get; init; } = string.Empty;
    public string PublicKeySpkiBase64 { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? RetiredAtUtc { get; init; }
    public bool IsActive { get; init; }
}
