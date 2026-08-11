using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class ReceiptDeviceKeyService
{
    public const string DefaultKeyFileName = "receipt-device-key.dpapi";

    private const string KeyRingSchema = "receipts.device-key-ring.v1";
    private const int KeyFileLockRetryMilliseconds = 25;
    private static readonly TimeSpan KeyFileLockTimeout = TimeSpan.FromSeconds(30);
    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("Receipts.DeviceSigningKey.v1");
    private static readonly object KeyFileSync = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public ReceiptDeviceKeyInfo GetOrCreate(string keyPath)
    {
        return WithKeyFileLock(keyPath, validatedPath =>
        {
            var keyRing = LoadOrCreateKeyRing(validatedPath);
            return GetActiveKeyInfo(keyRing);
        });
    }

    public ReceiptDeviceKeyInfo Rotate(string keyPath)
    {
        return WithKeyFileLock(keyPath, validatedPath =>
        {
            var keyRing = LoadOrCreateKeyRing(validatedPath);
            var rotatedAtUtc = DateTimeOffset.UtcNow;
            var activeRecord = keyRing.Keys.Single(key =>
                key.FingerprintSha256.Equals(keyRing.ActiveFingerprintSha256, StringComparison.Ordinal));
            activeRecord.RetiredAtUtc = rotatedAtUtc;

            var generated = GenerateKey(rotatedAtUtc);
            keyRing.ActiveFingerprintSha256 = generated.PublicKey.FingerprintSha256;
            keyRing.ActivePrivateKeyPkcs8Base64 = generated.PrivateKeyPkcs8Base64;
            keyRing.Keys.Add(generated.PublicKey);
            SaveKeyRing(validatedPath, keyRing);

            return ToInfo(generated.PublicKey, keyRing.ActiveFingerprintSha256);
        });
    }

    public IReadOnlySet<string> GetKnownFingerprints(string keyPath)
    {
        return WithKeyFileLock<IReadOnlySet<string>>(keyPath, validatedPath =>
        {
            if (!File.Exists(validatedPath))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var keyRing = LoadKeyRing(validatedPath);
            return keyRing.Keys
                .Select(key => key.FingerprintSha256)
                .ToHashSet(StringComparer.Ordinal);
        });
    }

    public bool IsKnownFingerprint(string keyPath, string fingerprintSha256) =>
        !string.IsNullOrWhiteSpace(fingerprintSha256) &&
        GetKnownFingerprints(keyPath).Contains(fingerprintSha256.Trim().ToLowerInvariant());

    public ReceiptSignatureManifest Sign(string keyPath, ReadOnlySpan<byte> payload)
    {
        var payloadBytes = payload.ToArray();
        try
        {
            return WithKeyFileLock(keyPath, validatedPath =>
            {
                var keyRing = LoadOrCreateKeyRing(validatedPath);
                var activeKey = keyRing.Keys.Single(key =>
                    key.FingerprintSha256.Equals(keyRing.ActiveFingerprintSha256, StringComparison.Ordinal));
                return CreateSignatureManifest(
                    activeKey,
                    SignPayload(keyRing.ActivePrivateKeyPkcs8Base64, payloadBytes));
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payloadBytes);
        }
    }

    internal ReceiptSignatureManifest SignManifest(string keyPath, ReceiptManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return WithKeyFileLock(keyPath, validatedPath =>
        {
            var keyRing = LoadOrCreateKeyRing(validatedPath);
            var activeKey = keyRing.Keys.Single(key =>
                key.FingerprintSha256.Equals(keyRing.ActiveFingerprintSha256, StringComparison.Ordinal));
            manifest.Signature = CreateSignatureManifest(activeKey, signature: null);
            var payload = ReceiptCanonicalJson.SerializeForSignature(manifest);
            var signatureBytes = SignPayload(keyRing.ActivePrivateKeyPkcs8Base64, payload);
            manifest.Signature.SignatureBase64 = Convert.ToBase64String(signatureBytes);
            return manifest.Signature;
        });
    }

    internal CapturedSigningKey CaptureActiveSigningKey(string keyPath)
    {
        return WithKeyFileLock(keyPath, validatedPath =>
        {
            var keyRing = LoadOrCreateKeyRing(validatedPath);
            var activeKey = keyRing.Keys.Single(key =>
                key.FingerprintSha256.Equals(keyRing.ActiveFingerprintSha256, StringComparison.Ordinal));
            return new CapturedSigningKey(
                ToInfo(activeKey, keyRing.ActiveFingerprintSha256),
                keyRing.ActivePrivateKeyPkcs8Base64);
        });
    }

    private static ReceiptSignatureManifest CreateSignatureManifest(
        ReceiptDevicePublicKeyRecord activeKey,
        byte[]? signature) => new()
    {
        Algorithm = ReceiptSignatureAlgorithms.EcdsaP256Sha256,
        Canonicalization = ReceiptSignatureAlgorithms.CanonicalJsonV1,
        KeyFingerprintSha256 = activeKey.FingerprintSha256,
        PublicKeySpkiBase64 = activeKey.PublicKeySpkiBase64,
        SignatureBase64 = signature is null ? string.Empty : Convert.ToBase64String(signature)
    };

    private static byte[] SignPayload(string privateKeyPkcs8Base64, ReadOnlySpan<byte> payload)
    {
        var privateKeyBytes = Convert.FromBase64String(privateKeyPkcs8Base64);
        try
        {
            return SignPayload(privateKeyBytes, payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKeyBytes);
        }
    }

    private static byte[] SignPayload(ReadOnlySpan<byte> privateKeyPkcs8, ReadOnlySpan<byte> payload)
    {
        using var signer = ECDsa.Create();
        signer.ImportPkcs8PrivateKey(privateKeyPkcs8, out var bytesRead);
        if (bytesRead != privateKeyPkcs8.Length || !IsP256(signer))
        {
            throw new CryptographicException("The Receipts device signing key is not a valid ECDSA P-256 key.");
        }

        return signer.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
    }

    private static string ValidateKeyPath(string keyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        return Path.GetFullPath(keyPath);
    }

    internal static string GetInterprocessLockPath(string keyPath) =>
        ValidateKeyPath(keyPath) + ".lock";

    private static T WithKeyFileLock<T>(string keyPath, Func<string, T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (KeyFileSync)
        {
            var validatedPath = ValidateKeyPath(keyPath);
            using var keyFileLock = AcquireInterprocessKeyFileLock(validatedPath);
            return operation(validatedPath);
        }
    }

    private static FileStream AcquireInterprocessKeyFileLock(string keyPath)
    {
        var directory = Path.GetDirectoryName(keyPath)
            ?? throw new InvalidOperationException("The Receipts device key path has no parent directory.");
        Directory.CreateDirectory(directory);
        var lockPath = GetInterprocessLockPath(keyPath);
        var elapsed = Stopwatch.StartNew();
        IOException? lastFailure = null;
        while (elapsed.Elapsed < KeyFileLockTimeout)
        {
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
            }
            catch (IOException ex)
            {
                lastFailure = ex;
                Thread.Sleep(KeyFileLockRetryMilliseconds);
            }
        }

        throw new IOException(
            $"Timed out waiting for exclusive access to the Receipts device signing key: {keyPath}",
            lastFailure);
    }

    private static ReceiptDeviceKeyRing LoadOrCreateKeyRing(string keyPath)
    {
        if (File.Exists(keyPath))
        {
            return LoadKeyRing(keyPath);
        }

        var generated = GenerateKey(DateTimeOffset.UtcNow);
        var keyRing = new ReceiptDeviceKeyRing
        {
            ActiveFingerprintSha256 = generated.PublicKey.FingerprintSha256,
            ActivePrivateKeyPkcs8Base64 = generated.PrivateKeyPkcs8Base64,
            Keys = [generated.PublicKey]
        };
        SaveKeyRing(keyPath, keyRing);
        return keyRing;
    }

    private static ReceiptDeviceKeyRing LoadKeyRing(string keyPath)
    {
        var encryptedBytes = File.ReadAllBytes(keyPath);
        var clearBytes = ProtectedData.Unprotect(encryptedBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
        try
        {
            var keyRing = JsonSerializer.Deserialize<ReceiptDeviceKeyRing>(clearBytes, JsonOptions)
                ?? throw new InvalidDataException("The Receipts device key ring was empty.");
            ValidateKeyRing(keyRing);
            return keyRing;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    private static void SaveKeyRing(string keyPath, ReceiptDeviceKeyRing keyRing)
    {
        ValidateKeyRing(keyRing);
        var directory = Path.GetDirectoryName(keyPath)
            ?? throw new InvalidOperationException("The Receipts device key path has no parent directory.");
        Directory.CreateDirectory(directory);

        var clearBytes = JsonSerializer.SerializeToUtf8Bytes(keyRing, JsonOptions);
        byte[]? protectedBytes = null;
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(keyPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            protectedBytes = ProtectedData.Protect(clearBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(temporaryPath, protectedBytes);
            File.Move(temporaryPath, keyPath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
            if (protectedBytes is not null)
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }

            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateKeyRing(ReceiptDeviceKeyRing keyRing)
    {
        if (!string.Equals(keyRing.Schema, KeyRingSchema, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Receipts device key ring schema is unsupported.");
        }

        if (keyRing.Keys is null || keyRing.Keys.Count == 0 || keyRing.Keys.Any(key => key is null) ||
            keyRing.Keys.Select(key => key.FingerprintSha256).Distinct(StringComparer.Ordinal).Count() != keyRing.Keys.Count)
        {
            throw new InvalidDataException("The Receipts device key ring does not contain unique public keys.");
        }

        var active = keyRing.Keys.SingleOrDefault(key =>
            key.FingerprintSha256.Equals(keyRing.ActiveFingerprintSha256, StringComparison.Ordinal));
        if (active is null || active.RetiredAtUtc is not null)
        {
            throw new InvalidDataException("The Receipts device key ring has no valid active key.");
        }

        byte[] privateKeyBytes;
        try
        {
            privateKeyBytes = Convert.FromBase64String(keyRing.ActivePrivateKeyPkcs8Base64);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("The Receipts device key ring contains an invalid private key.", ex);
        }

        try
        {
            using var signer = ECDsa.Create();
            signer.ImportPkcs8PrivateKey(privateKeyBytes, out var bytesRead);
            if (bytesRead != privateKeyBytes.Length || !IsP256(signer))
            {
                throw new InvalidDataException("The Receipts device key ring contains a non-P-256 key.");
            }

            var publicKey = signer.ExportSubjectPublicKeyInfo();
            var fingerprint = ComputeFingerprint(publicKey);
            if (!fingerprint.Equals(active.FingerprintSha256, StringComparison.Ordinal) ||
                !Convert.ToBase64String(publicKey).Equals(active.PublicKeySpkiBase64, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The Receipts device private key does not match its active public key.");
            }
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException("The Receipts device key ring contains an invalid private key.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKeyBytes);
        }

        foreach (var key in keyRing.Keys)
        {
            ValidatePublicKeyRecord(key);
        }
    }

    private static void ValidatePublicKeyRecord(ReceiptDevicePublicKeyRecord key)
    {
        try
        {
            var publicKey = Convert.FromBase64String(key.PublicKeySpkiBase64);
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(publicKey, out var bytesRead);
            if (bytesRead != publicKey.Length || !IsP256(verifier) ||
                !ComputeFingerprint(publicKey).Equals(key.FingerprintSha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The Receipts device key ring contains an invalid public key.");
            }
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            throw new InvalidDataException("The Receipts device key ring contains an invalid public key.", ex);
        }
    }

    private static GeneratedDeviceKey GenerateKey(DateTimeOffset createdAtUtc)
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = signer.ExportPkcs8PrivateKey();
        var publicKey = signer.ExportSubjectPublicKeyInfo();
        try
        {
            return new GeneratedDeviceKey(
                Convert.ToBase64String(privateKey),
                new ReceiptDevicePublicKeyRecord
                {
                    FingerprintSha256 = ComputeFingerprint(publicKey),
                    PublicKeySpkiBase64 = Convert.ToBase64String(publicKey),
                    CreatedAtUtc = createdAtUtc
                });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    private static ReceiptDeviceKeyInfo GetActiveKeyInfo(ReceiptDeviceKeyRing keyRing)
    {
        var active = keyRing.Keys.Single(key =>
            key.FingerprintSha256.Equals(keyRing.ActiveFingerprintSha256, StringComparison.Ordinal));
        return ToInfo(active, keyRing.ActiveFingerprintSha256);
    }

    private static ReceiptDeviceKeyInfo ToInfo(ReceiptDevicePublicKeyRecord key, string activeFingerprint) => new()
    {
        FingerprintSha256 = key.FingerprintSha256,
        PublicKeySpkiBase64 = key.PublicKeySpkiBase64,
        CreatedAtUtc = key.CreatedAtUtc,
        RetiredAtUtc = key.RetiredAtUtc,
        IsActive = key.FingerprintSha256.Equals(activeFingerprint, StringComparison.Ordinal)
    };

    internal static string ComputeFingerprint(ReadOnlySpan<byte> publicKeySpki) =>
        Convert.ToHexString(SHA256.HashData(publicKeySpki)).ToLowerInvariant();

    internal static bool IsP256(ECDsa key)
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        return parameters.Curve.Oid.Value == ECCurve.NamedCurves.nistP256.Oid.Value;
    }

    internal sealed class CapturedSigningKey : IDisposable
    {
        private byte[]? _privateKeyPkcs8;

        internal CapturedSigningKey(ReceiptDeviceKeyInfo keyInfo, string privateKeyPkcs8Base64)
        {
            KeyInfo = keyInfo;
            _privateKeyPkcs8 = Convert.FromBase64String(privateKeyPkcs8Base64);
        }

        internal ReceiptDeviceKeyInfo KeyInfo { get; }

        internal ReceiptSignatureManifest SignManifest(ReceiptManifest manifest)
        {
            ArgumentNullException.ThrowIfNull(manifest);
            var privateKey = _privateKeyPkcs8
                ?? throw new ObjectDisposedException(nameof(CapturedSigningKey));
            manifest.Signature = new ReceiptSignatureManifest
            {
                Algorithm = ReceiptSignatureAlgorithms.EcdsaP256Sha256,
                Canonicalization = ReceiptSignatureAlgorithms.CanonicalJsonV1,
                KeyFingerprintSha256 = KeyInfo.FingerprintSha256,
                PublicKeySpkiBase64 = KeyInfo.PublicKeySpkiBase64
            };
            var payload = ReceiptCanonicalJson.SerializeForSignature(manifest);
            var signatureBytes = SignPayload(privateKey, payload);
            manifest.Signature.SignatureBase64 = Convert.ToBase64String(signatureBytes);
            return manifest.Signature;
        }

        public void Dispose()
        {
            var privateKey = Interlocked.Exchange(ref _privateKeyPkcs8, null);
            if (privateKey is not null)
            {
                CryptographicOperations.ZeroMemory(privateKey);
            }
        }
    }

    private sealed class ReceiptDeviceKeyRing
    {
        public string Schema { get; set; } = KeyRingSchema;
        public string ActiveFingerprintSha256 { get; set; } = string.Empty;
        public string ActivePrivateKeyPkcs8Base64 { get; set; } = string.Empty;
        public List<ReceiptDevicePublicKeyRecord> Keys { get; set; } = [];
    }

    private sealed class ReceiptDevicePublicKeyRecord
    {
        public string FingerprintSha256 { get; set; } = string.Empty;
        public string PublicKeySpkiBase64 { get; set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset? RetiredAtUtc { get; set; }
    }

    private sealed record GeneratedDeviceKey(
        string PrivateKeyPkcs8Base64,
        ReceiptDevicePublicKeyRecord PublicKey);
}
