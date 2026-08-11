using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class PersonSegmentationModelPackageServiceTests
{
    [TestMethod]
    public async Task ValidateManifestAsync_ParsesLocalManifestWithoutTrustingOrRunningModel()
    {
        await WithTempPathsAsync(async paths =>
        {
            var fixture = await WriteLocalModelManifestAsync(paths);
            var service = new PersonSegmentationModelPackageService(paths);

            var result = await service.ValidateManifestAsync(fixture.ManifestPath);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.IsFalse(result.Staged);
            Assert.IsFalse(result.DidDownloadModel);
            Assert.IsFalse(result.WouldRunModel);
            Assert.IsFalse(result.WouldTrustModel);
            Assert.IsFalse(result.WouldEnableModel);
            Assert.IsFalse(result.WouldRegisterRunner);
            Assert.IsFalse(result.WouldContactHostedSegmentationService);
            Assert.IsFalse(result.WouldCertifyModel);
            Assert.AreEqual("sample.person-segmentation", result.ModelId);
            Assert.AreEqual(paths.PersonSegmentationModelStagingRoot, result.StagingRoot);
        });
    }

    [TestMethod]
    public async Task ValidateManifestAsync_AcceptsLegacyGoatShotSchema()
    {
        await WithTempPathsAsync(async paths =>
        {
            var fixture = await WriteLocalModelManifestAsync(
                paths,
                schemaVersion: PersonSegmentationModelPackageService.LegacyManifestSchemaVersion);

            var result = await new PersonSegmentationModelPackageService(paths)
                .ValidateManifestAsync(fixture.ManifestPath);

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
        });
    }

    [TestMethod]
    public async Task StageModelAsync_CopiesLocalModelAndWritesStageManifest()
    {
        await WithTempPathsAsync(async paths =>
        {
            var fixture = await WriteLocalModelManifestAsync(paths);
            var service = new PersonSegmentationModelPackageService(paths);

            var result = await service.StageModelAsync(new PersonSegmentationModelPackageRequest
            {
                ManifestLocation = fixture.ManifestPath
            });

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.IsTrue(result.Staged);
            Assert.IsFalse(result.DidDownloadModel);
            Assert.IsTrue(File.Exists(result.StagedModelPath));
            Assert.IsTrue(File.Exists(result.StagedManifestPath));
            Assert.AreEqual(fixture.Sha256, result.ModelSha256);
            Assert.AreEqual(fixture.Bytes.Length, result.SizeBytes);
            Assert.IsFalse(result.WouldRunModel);
            Assert.IsFalse(result.WouldTrustModel);
            Assert.IsFalse(result.WouldEnableModel);
            Assert.IsFalse(result.WouldRegisterRunner);

            var stageManifest = JsonSerializer.Deserialize<PersonSegmentationModelStageManifest>(
                await File.ReadAllTextAsync(result.StagedManifestPath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.IsNotNull(stageManifest);
            Assert.AreEqual(PersonSegmentationModelPackageService.CurrentStageSchemaVersion, stageManifest!.SchemaVersion);
            Assert.IsFalse(stageManifest.WouldRunModel);
            Assert.IsFalse(stageManifest.WouldTrustModel);
            Assert.IsFalse(stageManifest.WouldEnableModel);
            Assert.IsFalse(stageManifest.WouldRegisterRunner);
            Assert.IsFalse(stageManifest.WouldContactHostedSegmentationService);
            Assert.IsFalse(stageManifest.WouldCertifyModel);
        });
    }

    [TestMethod]
    public async Task StageModelAsync_RejectsChecksumMismatchWithoutStaging()
    {
        await WithTempPathsAsync(async paths =>
        {
            var fixture = await WriteLocalModelManifestAsync(paths, sha256: new string('0', 64));
            var service = new PersonSegmentationModelPackageService(paths);

            var result = await service.StageModelAsync(new PersonSegmentationModelPackageRequest
            {
                ManifestLocation = fixture.ManifestPath
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.Staged);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("SHA-256", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(Directory.EnumerateFiles(paths.PersonSegmentationModelStagingRoot, "*", SearchOption.AllDirectories).Any());
        });
    }

    [TestMethod]
    public async Task StageModelAsync_RemoteModelRequiresAcceptDownload()
    {
        await WithTempPathsAsync(async paths =>
        {
            var bytes = Encoding.UTF8.GetBytes("fake onnx model bytes");
            var sha256 = Sha256(bytes);
            var manifestPath = await WriteManifestAsync(
                paths,
                modelUri: "https://models.example.test/person.onnx",
                sha256: sha256,
                sizeBytes: bytes.Length);
            using var httpClient = new HttpClient(new FakeHttpHandler(new Dictionary<string, byte[]>
            {
                ["https://models.example.test/person.onnx"] = bytes
            }));
            var service = new PersonSegmentationModelPackageService(paths, httpClient);

            var result = await service.StageModelAsync(new PersonSegmentationModelPackageRequest
            {
                ManifestLocation = manifestPath,
                AcceptDownload = false
            });

            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(result.Staged);
            Assert.IsTrue(result.WouldDownloadModel);
            Assert.IsFalse(result.DidDownloadModel);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("accept-download", StringComparison.OrdinalIgnoreCase)));
            Assert.IsFalse(Directory.EnumerateFiles(paths.PersonSegmentationModelStagingRoot, "*", SearchOption.AllDirectories).Any());
        });
    }

    [TestMethod]
    public async Task StageModelAsync_DownloadsRemoteModelFromFakeHttpWhenAccepted()
    {
        await WithTempPathsAsync(async paths =>
        {
            var bytes = Encoding.UTF8.GetBytes("fake onnx model bytes");
            var sha256 = Sha256(bytes);
            var manifestPath = await WriteManifestAsync(
                paths,
                modelUri: "https://models.example.test/person.onnx",
                sha256: sha256,
                sizeBytes: bytes.Length);
            using var httpClient = new HttpClient(new FakeHttpHandler(new Dictionary<string, byte[]>
            {
                ["https://models.example.test/person.onnx"] = bytes
            }));
            var service = new PersonSegmentationModelPackageService(paths, httpClient);

            var result = await service.StageModelAsync(new PersonSegmentationModelPackageRequest
            {
                ManifestLocation = manifestPath,
                AcceptDownload = true
            });

            Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues));
            Assert.IsTrue(result.Staged);
            Assert.IsTrue(result.WouldDownloadModel);
            Assert.IsTrue(result.DidDownloadModel);
            Assert.AreEqual(sha256, result.ModelSha256);
            Assert.IsTrue(File.Exists(result.StagedModelPath));
            Assert.IsTrue(File.Exists(result.StagedManifestPath));
            Assert.IsFalse(result.WouldRunModel);
            Assert.IsFalse(result.WouldTrustModel);
            Assert.IsFalse(result.WouldEnableModel);
            Assert.IsFalse(result.WouldRegisterRunner);
            Assert.IsFalse(result.WouldContactHostedSegmentationService);
            Assert.IsFalse(result.WouldCertifyModel);
        });
    }

    private static async Task<ModelFixture> WriteLocalModelManifestAsync(
        AppPaths paths,
        string? sha256 = null,
        string? schemaVersion = null)
    {
        var root = Path.Combine(paths.LocalRoot, "model-fixtures");
        Directory.CreateDirectory(root);
        var bytes = Encoding.UTF8.GetBytes("fake onnx model bytes");
        var modelPath = Path.Combine(root, "sample-person.onnx");
        await File.WriteAllBytesAsync(modelPath, bytes);
        var digest = sha256 ?? Sha256(bytes);
        var manifestPath = await WriteManifestAsync(
            paths,
            modelUri: Path.GetFileName(modelPath),
            sha256: digest,
            sizeBytes: bytes.Length,
            schemaVersion: schemaVersion);
        return new ModelFixture(bytes, digest, modelPath, manifestPath);
    }

    private static async Task<string> WriteManifestAsync(
        AppPaths paths,
        string modelUri,
        string sha256,
        long sizeBytes,
        string? schemaVersion = null)
    {
        var root = Path.Combine(paths.LocalRoot, "model-fixtures");
        Directory.CreateDirectory(root);
        var manifestPath = Path.Combine(root, "person-segmentation-model.json");
        await File.WriteAllTextAsync(
            manifestPath,
            $$"""
            {
              "schemaVersion": "{{schemaVersion ?? PersonSegmentationModelPackageService.CurrentManifestSchemaVersion}}",
              "modelId": "sample.person-segmentation",
              "name": "Sample Person Segmentation Model",
              "version": "0.1.0",
              "modelUri": "{{modelUri}}",
              "sha256": "{{sha256}}",
              "sizeBytes": {{sizeBytes}},
              "runnerHint": "onnxruntime",
              "license": "test-only",
              "source": "local test fixture",
              "notes": "Fixture model bytes only. Not a real segmentation model."
            }
            """);
        return manifestPath;
    }

    private static async Task WithTempPathsAsync(Func<AppPaths, Task> action)
    {
        var originalLocalRoot = Environment.GetEnvironmentVariable("GOATSHOT_LOCAL_ROOT");
        var originalLibraryRoot = Environment.GetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", Path.Combine(root, "local"));
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", Path.Combine(root, "library"));
            var settings = new AppSettings();
            var paths = AppPaths.Create(settings);

            await action(paths);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOATSHOT_LOCAL_ROOT", originalLocalRoot);
            Environment.SetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT", originalLibraryRoot);

            if (Directory.Exists(root))
            {
                DeleteDirectoryWithRetry(root);
            }
        }
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 7)
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 7)
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(100);
            }
        }
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record ModelFixture(
        byte[] Bytes,
        string Sha256,
        string ModelPath,
        string ManifestPath);

    private sealed class FakeHttpHandler(IReadOnlyDictionary<string, byte[]> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var key = request.RequestUri?.ToString() ?? string.Empty;
            if (!responses.TryGetValue(key, out var bytes))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
        }
    }
}
