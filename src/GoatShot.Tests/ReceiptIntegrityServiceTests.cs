using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class ReceiptIntegrityServiceTests
{
    [TestMethod]
    public async Task SealAndVerifyAsync_ReturnsKnownDeviceAndWritesCanonicalManifest()
    {
        await WithFixtureAsync(async fixture =>
        {
            var sealedManifest = await fixture.Service.SealAndWriteAsync(
                fixture.Manifest,
                fixture.ReceiptRoot,
                fixture.KeyPath);

            var result = await fixture.Service.VerifyPackageAsync(fixture.ReceiptRoot, fixture.KeyPath);
            var manifestBytes = await File.ReadAllBytesAsync(
                Path.Combine(fixture.ReceiptRoot, ReceiptIntegrityService.ManifestFileName));

            Assert.AreEqual(ReceiptVerificationStatus.IntactKnownDevice, result.Status);
            Assert.IsTrue(result.IsIntact);
            Assert.AreEqual(fixture.Manifest.ReceiptId, result.ReceiptId);
            Assert.IsNotNull(sealedManifest.Signature);
            Assert.AreEqual(ReceiptSignatureAlgorithms.EcdsaP256Sha256, sealedManifest.Signature.Algorithm);
            Assert.AreEqual(ReceiptSignatureAlgorithms.CanonicalJsonV1, sealedManifest.Signature.Canonicalization);
            Assert.AreEqual(sealedManifest.Signature.KeyFingerprintSha256, result.SignerFingerprintSha256);
            CollectionAssert.AreEqual(ReceiptCanonicalJson.Serialize(sealedManifest), manifestBytes);
            Assert.AreEqual(ReceiptIntegrityService.ChainSeedSha256, sealedManifest.Segments[0].PreviousChainSha256);
            Assert.AreEqual(sealedManifest.Segments[0].ChainSha256, sealedManifest.Segments[1].PreviousChainSha256);
            Assert.IsTrue(sealedManifest.Segments.All(segment => segment.Sha256.Length == 64));
            Assert.IsTrue(sealedManifest.Artifacts.All(artifact => artifact.Sha256.Length == 64));
        });
    }

    [TestMethod]
    public async Task VerifyAsync_ReturnsUnknownDeviceWhenLocalKeyHistoryDoesNotContainSigner()
    {
        await WithFixtureAsync(async fixture =>
        {
            var sealedManifest = await fixture.Service.SealAsync(
                fixture.Manifest,
                fixture.ReceiptRoot,
                fixture.KeyPath);
            var unrelatedKeyPath = Path.Combine(fixture.TestRoot, "unrelated", "device-key.dpapi");
            _ = new ReceiptDeviceKeyService().GetOrCreate(unrelatedKeyPath);

            var noLocalHistory = await fixture.Service.VerifyAsync(sealedManifest, fixture.ReceiptRoot);
            var unrelatedHistory = await fixture.Service.VerifyAsync(
                sealedManifest,
                fixture.ReceiptRoot,
                unrelatedKeyPath);

            Assert.AreEqual(ReceiptVerificationStatus.IntactUnknownDevice, noLocalHistory.Status);
            Assert.AreEqual(ReceiptVerificationStatus.IntactUnknownDevice, unrelatedHistory.Status);
        });
    }

    [TestMethod]
    public async Task VerifyAsync_ReturnsModifiedWhenSegmentBytesChange()
    {
        await WithFixtureAsync(async fixture =>
        {
            var sealedManifest = await fixture.Service.SealAsync(
                fixture.Manifest,
                fixture.ReceiptRoot,
                fixture.KeyPath);
            await File.WriteAllBytesAsync(
                Path.Combine(fixture.ReceiptRoot, "segments", "track-a", "0000.mp4"),
                Encoding.UTF8.GetBytes("changed segment payload"));

            var result = await fixture.Service.VerifyAsync(sealedManifest, fixture.ReceiptRoot, fixture.KeyPath);

            Assert.AreEqual(ReceiptVerificationStatus.Modified, result.Status);
            Assert.IsFalse(result.IsIntact);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("0000.mp4", StringComparison.Ordinal)));
        });
    }

    [TestMethod]
    public async Task VerifyAsync_ReturnsIncompleteWhenSignedSegmentIsMissing()
    {
        await WithFixtureAsync(async fixture =>
        {
            var sealedManifest = await fixture.Service.SealAsync(
                fixture.Manifest,
                fixture.ReceiptRoot,
                fixture.KeyPath);
            File.Delete(Path.Combine(fixture.ReceiptRoot, "segments", "track-a", "0001.mp4"));

            var result = await fixture.Service.VerifyAsync(sealedManifest, fixture.ReceiptRoot, fixture.KeyPath);

            Assert.AreEqual(ReceiptVerificationStatus.Incomplete, result.Status);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("missing", StringComparison.OrdinalIgnoreCase)));
        });
    }

    [TestMethod]
    public async Task VerifyAsync_ReturnsModifiedWhenSignedSegmentsAreReordered()
    {
        await WithFixtureAsync(async fixture =>
        {
            var sealedManifest = await fixture.Service.SealAsync(
                fixture.Manifest,
                fixture.ReceiptRoot,
                fixture.KeyPath);
            sealedManifest.Segments.Reverse();

            var result = await fixture.Service.VerifyAsync(sealedManifest, fixture.ReceiptRoot, fixture.KeyPath);

            Assert.AreEqual(ReceiptVerificationStatus.Modified, result.Status);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("signature", StringComparison.OrdinalIgnoreCase)));
        });
    }

    [TestMethod]
    public async Task VerifyAsync_ReturnsModifiedWhenSegmentEntryIsInserted()
    {
        await WithFixtureAsync(async fixture =>
        {
            var sealedManifest = await fixture.Service.SealAsync(
                fixture.Manifest,
                fixture.ReceiptRoot,
                fixture.KeyPath);
            sealedManifest.Segments.Add(new ReceiptSegmentManifest
            {
                SegmentId = "inserted",
                TrackId = "track-a",
                SequenceNumber = 2,
                RelativePath = "segments/track-a/0001.mp4",
                CapturedAtUtc = sealedManifest.FinalizedAtUtc,
                StartMonotonicTicks = 30_000,
                DurationTicks = 10_000
            });

            var result = await fixture.Service.VerifyAsync(sealedManifest, fixture.ReceiptRoot, fixture.KeyPath);

            Assert.AreEqual(ReceiptVerificationStatus.Modified, result.Status);
        });
    }

    [TestMethod]
    public async Task VerifyAsync_ReturnsModifiedWhenUnlistedSegmentFileIsInserted()
    {
        await WithFixtureAsync(async fixture =>
        {
            var sealedManifest = await fixture.Service.SealAsync(
                fixture.Manifest,
                fixture.ReceiptRoot,
                fixture.KeyPath);
            var insertedPath = Path.Combine(
                fixture.ReceiptRoot,
                "segments",
                "track-a",
                "inserted.mp4");
            await File.WriteAllBytesAsync(insertedPath, Encoding.UTF8.GetBytes("inserted payload"));

            var result = await fixture.Service.VerifyAsync(
                sealedManifest,
                fixture.ReceiptRoot,
                fixture.KeyPath);

            Assert.AreEqual(ReceiptVerificationStatus.Modified, result.Status);
            Assert.IsTrue(result.Issues.Any(issue =>
                issue.Contains("not listed", StringComparison.OrdinalIgnoreCase)));
        });
    }

    [TestMethod]
    public async Task VerifyPackageAsync_ReturnsModifiedWhenUnlistedRootFileIsInserted()
    {
        await WithFixtureAsync(async fixture =>
        {
            await fixture.Service.SealAndWriteAsync(
                fixture.Manifest,
                fixture.ReceiptRoot,
                fixture.KeyPath);
            await File.WriteAllTextAsync(
                Path.Combine(fixture.ReceiptRoot, "after.png"),
                "fabricated unsigned claim");

            var result = await fixture.Service.VerifyPackageAsync(
                fixture.ReceiptRoot,
                fixture.KeyPath);

            Assert.AreEqual(ReceiptVerificationStatus.Modified, result.Status);
            Assert.IsTrue(result.Issues.Any(issue =>
                issue.Contains("not listed", StringComparison.OrdinalIgnoreCase)));
        });
    }

    [TestMethod]
    public async Task VerifyPackageAsync_ReturnsModifiedWhenUnknownManifestClaimIsInserted()
    {
        await WithFixtureAsync(async fixture =>
        {
            await fixture.Service.SealAndWriteAsync(
                fixture.Manifest,
                fixture.ReceiptRoot,
                fixture.KeyPath);
            var manifestPath = Path.Combine(
                fixture.ReceiptRoot,
                ReceiptIntegrityService.ManifestFileName);
            var canonical = await File.ReadAllTextAsync(manifestPath);
            var closingBrace = canonical.LastIndexOf('}');
            Assert.IsTrue(closingBrace >= 0);
            var mutated = canonical.Insert(closingBrace, ",\n  \"legalCertification\": true\n");
            await File.WriteAllTextAsync(manifestPath, mutated);

            var result = await fixture.Service.VerifyPackageAsync(
                fixture.ReceiptRoot,
                fixture.KeyPath);

            Assert.AreEqual(ReceiptVerificationStatus.Modified, result.Status);
            Assert.IsTrue(result.Issues.Any(issue =>
                issue.Contains("canonical JSON", StringComparison.OrdinalIgnoreCase)));
        });
    }

    [TestMethod]
    public async Task Rotate_PreservesRecognitionOfOldReceiptsAndSignsWithNewFingerprint()
    {
        await WithFixtureAsync(async fixture =>
        {
            var original = await fixture.Service.SealAsync(
                fixture.Manifest,
                fixture.ReceiptRoot,
                fixture.KeyPath);
            var oldFingerprint = original.Signature!.KeyFingerprintSha256;

            var rotatedKey = fixture.DeviceKeys.Rotate(fixture.KeyPath);
            var secondManifest = Clone(original);
            secondManifest.ReceiptId = "receipt-after-rotation";
            secondManifest.Signature = null;
            var afterRotation = await fixture.Service.SealAsync(
                secondManifest,
                fixture.ReceiptRoot,
                fixture.KeyPath);

            var originalResult = await fixture.Service.VerifyAsync(original, fixture.ReceiptRoot, fixture.KeyPath);
            var rotatedResult = await fixture.Service.VerifyAsync(afterRotation, fixture.ReceiptRoot, fixture.KeyPath);
            var knownFingerprints = fixture.DeviceKeys.GetKnownFingerprints(fixture.KeyPath);

            Assert.AreNotEqual(oldFingerprint, rotatedKey.FingerprintSha256);
            Assert.AreEqual(rotatedKey.FingerprintSha256, afterRotation.Signature!.KeyFingerprintSha256);
            Assert.AreEqual(ReceiptVerificationStatus.IntactKnownDevice, originalResult.Status);
            Assert.AreEqual(ReceiptVerificationStatus.IntactKnownDevice, rotatedResult.Status);
            Assert.IsTrue(knownFingerprints.Contains(oldFingerprint));
            Assert.IsTrue(knownFingerprints.Contains(rotatedKey.FingerprintSha256));
        });
    }

    [TestMethod]
    public async Task CapturedSigningKey_KeepsPublicArtifactAndSignatureConsistentAcrossRotation()
    {
        await WithFixtureAsync(async fixture =>
        {
            using var signingKey = fixture.DeviceKeys.CaptureActiveSigningKey(fixture.KeyPath);
            var capturedFingerprint = signingKey.KeyInfo.FingerprintSha256;
            var publicKeyPath = Path.Combine(fixture.ReceiptRoot, "public-key.pem");
            await File.WriteAllTextAsync(publicKeyPath, ToPublicKeyPem(signingKey.KeyInfo));
            fixture.Manifest.Artifacts.Add(new ReceiptArtifactManifest
            {
                ArtifactId = "device-public-key",
                Role = "public-verification-key",
                RelativePath = "public-key.pem",
                MediaType = "application/x-pem-file"
            });

            var rotated = fixture.DeviceKeys.Rotate(fixture.KeyPath);
            var sealedManifest = await fixture.Service.SealAndWriteWithKeyAsync(
                fixture.Manifest,
                fixture.ReceiptRoot,
                signingKey);

            Assert.AreNotEqual(capturedFingerprint, rotated.FingerprintSha256);
            Assert.AreEqual(capturedFingerprint, sealedManifest.Signature!.KeyFingerprintSha256);
            Assert.AreEqual(signingKey.KeyInfo.PublicKeySpkiBase64, sealedManifest.Signature.PublicKeySpkiBase64);
            await ReplayReceiptPackagePublisher.ValidatePublicKeyArtifactMatchesSignatureAsync(
                publicKeyPath,
                sealedManifest);
            var verification = await fixture.Service.VerifyPackageAsync(
                fixture.ReceiptRoot,
                fixture.KeyPath);
            Assert.AreEqual(ReceiptVerificationStatus.IntactKnownDevice, verification.Status);

            await File.WriteAllTextAsync(publicKeyPath, ToPublicKeyPem(rotated));
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                ReplayReceiptPackagePublisher.ValidatePublicKeyArtifactMatchesSignatureAsync(
                    publicKeyPath,
                    sealedManifest));
        });
    }

    [TestMethod]
    public async Task Rotate_WaitsForExclusiveInterprocessKeyFileLock()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "Receipts.Tests", Guid.NewGuid().ToString("N"));
        var keyPath = Path.Combine(testRoot, "secrets", ReceiptDeviceKeyService.DefaultKeyFileName);
        var service = new ReceiptDeviceKeyService();
        FileStream? externalLock = null;
        try
        {
            var original = service.GetOrCreate(keyPath);
            externalLock = new FileStream(
                ReceiptDeviceKeyService.GetInterprocessLockPath(keyPath),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var rotateTask = Task.Run(() =>
            {
                started.SetResult();
                return service.Rotate(keyPath);
            });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);

            Assert.IsFalse(rotateTask.IsCompleted, "Rotation must wait while another process holds the key-file lock.");
            externalLock.Dispose();
            externalLock = null;

            var rotated = await rotateTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreNotEqual(original.FingerprintSha256, rotated.FingerprintSha256);
        }
        finally
        {
            externalLock?.Dispose();
            DeleteDirectoryWithRetry(testRoot);
        }
    }

    [TestMethod]
    public async Task ReplayPublisher_WritesPublicKeyArtifactMatchingManifestSigner()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "Receipts.Tests", Guid.NewGuid().ToString("N"));
        var bufferRoot = Path.Combine(testRoot, "buffer");
        var destination = Path.Combine(testRoot, "library", "receipt-publisher-fixture");
        var keyPath = Path.Combine(testRoot, "secrets", ReceiptDeviceKeyService.DefaultKeyFileName);
        Directory.CreateDirectory(bufferRoot);
        var segmentPath = Path.Combine(bufferRoot, "segment-000000.mp4");
        var segmentBytes = Encoding.UTF8.GetBytes("finalized replay segment fixture");
        await File.WriteAllBytesAsync(segmentPath, segmentBytes);
        try
        {
            var source = new ReplayCaptureSourceDescriptor(
                ReplayCaptureSourceKind.SelectedMonitor,
                "display-1",
                "Primary display",
                new ReplayCaptureBounds(0, 0, 1280, 720));
            var track = new ReplayTrackDescriptor(
                "track-1",
                "Primary display",
                source,
                1280,
                720);
            var segment = new ReplaySegmentMetadata(
                "segment-1",
                0,
                track,
                segmentPath,
                DateTimeOffset.UtcNow,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                segmentBytes.LongLength);
            var deviceKeys = new ReceiptDeviceKeyService();
            var integrity = new ReceiptIntegrityService(deviceKeys);
            var publisher = new ReplayReceiptPackagePublisher(
                new FileReplayBufferStorage(bufferRoot),
                integrity,
                deviceKeys,
                keyPath,
                new AppSettings());

            var published = await publisher.PublishAsync(
                new ReplaySnapshotPublication(
                    "receipt-publisher-fixture",
                    destination,
                    segment.StartedAtUtc,
                    [segment]),
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15));

            var manifest = ReceiptCanonicalJson.Deserialize(await File.ReadAllBytesAsync(
                Path.Combine(published.PackagePath, ReceiptIntegrityService.ManifestFileName)));
            var publicKeyPath = Path.Combine(published.PackagePath, "public-key.pem");
            await ReplayReceiptPackagePublisher.ValidatePublicKeyArtifactMatchesSignatureAsync(
                publicKeyPath,
                manifest);
            var verification = await integrity.VerifyPackageAsync(published.PackagePath, keyPath);

            Assert.AreEqual(ReceiptVerificationStatus.IntactKnownDevice, verification.Status);
            Assert.IsTrue(manifest.Artifacts.Any(artifact =>
                artifact.Role.Equals("public-verification-key", StringComparison.Ordinal) &&
                artifact.RelativePath.Equals("public-key.pem", StringComparison.Ordinal)));
        }
        finally
        {
            DeleteDirectoryWithRetry(testRoot);
        }
    }

    [TestMethod]
    public async Task SealAsync_StoresDevicePrivateKeyOnlyInDpapiProtectedFile()
    {
        await WithFixtureAsync(async fixture =>
        {
            var sealedManifest = await fixture.Service.SealAsync(
                fixture.Manifest,
                fixture.ReceiptRoot,
                fixture.KeyPath);
            var protectedBytes = await File.ReadAllBytesAsync(fixture.KeyPath);
            var protectedText = Encoding.UTF8.GetString(protectedBytes);

            Assert.IsNotNull(sealedManifest.Signature);
            Assert.IsTrue(protectedBytes.Length > 0);
            Assert.IsFalse(protectedText.Contains("activePrivateKeyPkcs8Base64", StringComparison.Ordinal));
            Assert.IsFalse(protectedText.Contains("receipts.device-key-ring.v1", StringComparison.Ordinal));
            Assert.Throws<JsonException>(() => JsonDocument.Parse(protectedBytes));
        });
    }

    [TestMethod]
    public void Serialize_ProducesSameCanonicalJsonForDifferentDictionaryInsertionOrder()
    {
        var first = BuildManifest();
        first.CaptureSettings.AdditionalSettings["zeta"] = "last";
        first.CaptureSettings.AdditionalSettings["alpha"] = "first";
        var second = BuildManifest();
        second.CaptureSettings.AdditionalSettings["alpha"] = "first";
        second.CaptureSettings.AdditionalSettings["zeta"] = "last";

        var firstJson = ReceiptCanonicalJson.Serialize(first);
        var secondJson = ReceiptCanonicalJson.Serialize(second);

        CollectionAssert.AreEqual(firstJson, secondJson);
        var jsonText = Encoding.UTF8.GetString(firstJson);
        Assert.IsTrue(jsonText.IndexOf("alpha", StringComparison.Ordinal) <
                      jsonText.IndexOf("zeta", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ReceiptVerificationStatus_ExposesOnlyApprovedOfflineStates()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "IntactKnownDevice",
                "IntactUnknownDevice",
                "Modified",
                "Incomplete",
                "Unverifiable"
            },
            Enum.GetNames<ReceiptVerificationStatus>());
    }

    private static async Task WithFixtureAsync(Func<ReceiptFixture, Task> action)
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "Receipts.Tests", Guid.NewGuid().ToString("N"));
        var receiptRoot = Path.Combine(testRoot, "receipt");
        Directory.CreateDirectory(Path.Combine(receiptRoot, "segments", "track-a"));
        Directory.CreateDirectory(Path.Combine(receiptRoot, "artifacts"));
        await File.WriteAllBytesAsync(
            Path.Combine(receiptRoot, "segments", "track-a", "0000.mp4"),
            Encoding.UTF8.GetBytes("segment zero payload"));
        await File.WriteAllBytesAsync(
            Path.Combine(receiptRoot, "segments", "track-a", "0001.mp4"),
            Encoding.UTF8.GetBytes("segment one payload"));
        await File.WriteAllBytesAsync(
            Path.Combine(receiptRoot, "artifacts", "thumbnail.png"),
            Encoding.UTF8.GetBytes("thumbnail payload"));

        var deviceKeys = new ReceiptDeviceKeyService();
        var fixture = new ReceiptFixture(
            testRoot,
            receiptRoot,
            Path.Combine(testRoot, "secrets", "receipt-device-key.dpapi"),
            BuildManifest(),
            deviceKeys,
            new ReceiptIntegrityService(deviceKeys));
        try
        {
            await action(fixture);
        }
        finally
        {
            DeleteDirectoryWithRetry(testRoot);
        }
    }

    private static ReceiptManifest BuildManifest()
    {
        var createdAtUtc = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
        return new ReceiptManifest
        {
            ReceiptId = "receipt-fixture",
            CreatedAtUtc = createdAtUtc,
            FinalizedAtUtc = createdAtUtc.AddSeconds(4),
            Application = new ReceiptApplicationManifest
            {
                ProductName = "Receipts",
                Version = "0.3.0",
                Build = "test"
            },
            CaptureSettings = new ReceiptCaptureSettingsManifest
            {
                RecordingMode = "replay",
                TargetStrategy = "chosen-monitor",
                FramesPerSecond = 30,
                VideoBitrateBitsPerSecond = 8_000_000,
                IncludeCursor = true
            },
            Tracks =
            [
                new ReceiptTrackManifest
                {
                    TrackId = "track-a",
                    SourceKind = "monitor",
                    SourceId = "display-1",
                    DisplayName = "Primary display",
                    Bounds = new ReceiptCaptureBoundsManifest
                    {
                        Width = 1920,
                        Height = 1080
                    },
                    SourceTransitions =
                    [
                        new ReceiptSourceTransitionManifest
                        {
                            SourceKind = "monitor",
                            SourceId = "display-1",
                            CapturedAtUtc = createdAtUtc.AddSeconds(2),
                            EffectiveStartMonotonicTicks = 20_000,
                            Bounds = new ReceiptCaptureBoundsManifest { Width = 1920, Height = 1080 }
                        },
                        new ReceiptSourceTransitionManifest
                        {
                            SourceKind = "monitor",
                            SourceId = "display-1",
                            CapturedAtUtc = createdAtUtc,
                            EffectiveStartMonotonicTicks = 0,
                            Bounds = new ReceiptCaptureBoundsManifest { Width = 1920, Height = 1080 }
                        }
                    ]
                }
            ],
            Segments =
            [
                new ReceiptSegmentManifest
                {
                    SegmentId = "segment-1",
                    TrackId = "track-a",
                    SequenceNumber = 1,
                    RelativePath = "segments\\track-a\\0001.mp4",
                    CapturedAtUtc = createdAtUtc.AddSeconds(2),
                    StartMonotonicTicks = 20_000,
                    DurationTicks = 20_000
                },
                new ReceiptSegmentManifest
                {
                    SegmentId = "segment-0",
                    TrackId = "track-a",
                    SequenceNumber = 0,
                    RelativePath = "segments\\track-a\\0000.mp4",
                    CapturedAtUtc = createdAtUtc,
                    StartMonotonicTicks = 0,
                    DurationTicks = 20_000
                }
            ],
            Artifacts =
            [
                new ReceiptArtifactManifest
                {
                    ArtifactId = "thumbnail",
                    Role = "thumbnail",
                    RelativePath = "artifacts\\thumbnail.png",
                    MediaType = "image/png"
                }
            ]
        };
    }

    private static ReceiptManifest Clone(ReceiptManifest manifest) =>
        ReceiptCanonicalJson.Deserialize(ReceiptCanonicalJson.Serialize(manifest));

    private static string ToPublicKeyPem(ReceiptDeviceKeyInfo keyInfo)
    {
        var publicKey = Convert.FromBase64String(keyInfo.PublicKeySpkiBase64);
        return PemEncoding.WriteString("PUBLIC KEY", publicKey);
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 4)
            {
                Thread.Sleep(50);
            }
        }
    }

    private sealed record ReceiptFixture(
        string TestRoot,
        string ReceiptRoot,
        string KeyPath,
        ReceiptManifest Manifest,
        ReceiptDeviceKeyService DeviceKeys,
        ReceiptIntegrityService Service);
}
