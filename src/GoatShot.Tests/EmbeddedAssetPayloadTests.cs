using System.IO.Compression;
using System.Text;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class EmbeddedAssetPayloadTests
{
    [TestMethod]
    public void BoundedReadStream_ExposesZipPayloadAtZeroBasedOffsets()
    {
        using var zipBytes = new MemoryStream();
        using (var writer = new ZipArchive(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = writer.CreateEntry("browser-extension/content-script.js", CompressionLevel.Optimal);
            using var output = entry.Open();
            output.Write(Encoding.UTF8.GetBytes("asset-ok"));
        }

        using var executable = new MemoryStream();
        executable.Write(new byte[8192]);
        var payloadOffset = executable.Position;
        zipBytes.Position = 0;
        zipBytes.CopyTo(executable);
        executable.Position = 0;

        using var payload = new BoundedReadStream(executable, payloadOffset, zipBytes.Length);
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry("browser-extension/content-script.js")!.Open());

        Assert.AreEqual("asset-ok", reader.ReadToEnd());
    }
}
