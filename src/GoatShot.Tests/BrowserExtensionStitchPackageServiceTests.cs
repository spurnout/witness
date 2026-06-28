using System.Drawing;
using System.Drawing.Imaging;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BrowserExtensionStitchPackageServiceTests
{
    [TestMethod]
    public void Validate_AcceptsPackageDirectoryWithStitchedImageAndTiles()
    {
        WithPackage(packageRoot =>
        {
            WriteSamplePng(Path.Combine(packageRoot, "stitched.png"));
            Directory.CreateDirectory(Path.Combine(packageRoot, "tiles"));
            WriteSamplePng(Path.Combine(packageRoot, "tiles", "tile-0000.png"));
            File.WriteAllText(Path.Combine(packageRoot, "goatshot-stitch-package.json"), """
                {
                  "schemaVersion": "goatshot.browser-stitch-package.v1",
                  "correlationId": "sample-contract-fixture",
                  "source": "extension-storage-export",
                  "stitchedImagePath": "stitched.png",
                  "tiles": [
                    { "index": 0, "path": "tiles/tile-0000.png", "captureState": "captured" }
                  ],
                  "warnings": []
                }
                """);

            var result = new BrowserExtensionStitchPackageService().Validate(packageRoot, "sample-contract-fixture");

            Assert.IsTrue(result.IsValid, string.Join(Environment.NewLine, result.Issues));
            Assert.AreEqual(Path.Combine(packageRoot, "stitched.png"), result.StitchedImagePath);
            Assert.IsTrue(result.StitchedImageBytes > 0);
            Assert.IsTrue(result.TotalTileBytes > 0);
        });
    }

    [TestMethod]
    public void Validate_RejectsPathTraversal()
    {
        WithPackage(packageRoot =>
        {
            File.WriteAllText(Path.Combine(packageRoot, "goatshot-stitch-package.json"), """
                {
                  "schemaVersion": "goatshot.browser-stitch-package.v1",
                  "correlationId": "sample-contract-fixture",
                  "stitchedImagePath": "..\\outside.png",
                  "tiles": []
                }
                """);

            var result = new BrowserExtensionStitchPackageService().Validate(packageRoot, "sample-contract-fixture");

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("inside the stitch package", StringComparison.OrdinalIgnoreCase)));
        });
    }

    [TestMethod]
    public void Validate_RejectsCorrelationMismatch()
    {
        WithPackage(packageRoot =>
        {
            WriteSamplePng(Path.Combine(packageRoot, "stitched.png"));
            File.WriteAllText(Path.Combine(packageRoot, "goatshot-stitch-package.json"), """
                {
                  "schemaVersion": "goatshot.browser-stitch-package.v1",
                  "correlationId": "different-correlation",
                  "stitchedImagePath": "stitched.png",
                  "tiles": []
                }
                """);

            var result = new BrowserExtensionStitchPackageService().Validate(packageRoot, "sample-contract-fixture");

            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Issues.Any(issue => issue.Contains("correlationId", StringComparison.OrdinalIgnoreCase)));
        });
    }

    private static void WithPackage(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "GoatShot.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            action(root);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void WriteSamplePng(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new Bitmap(80, 50, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            using var brush = new SolidBrush(Color.FromArgb(48, 230, 195));
            graphics.FillRectangle(brush, 10, 10, 60, 30);
        }

        bitmap.Save(path, ImageFormat.Png);
    }
}
