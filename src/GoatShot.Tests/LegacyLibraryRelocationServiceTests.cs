using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class LegacyLibraryRelocationServiceTests
{
    private static string TempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "receipts-relocation-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [TestMethod]
    public void Describe_IgnoresALibraryThatIsAlreadyOnTheCurrentBrand()
    {
        Assert.IsNull(LegacyLibraryRelocationService.Describe(@"C:\Users\dev\Pictures\Receipts"));
        Assert.IsNull(LegacyLibraryRelocationService.Describe(@"C:\Users\dev\Pictures\Screenshots"));
        Assert.IsNull(LegacyLibraryRelocationService.Describe(string.Empty));
        Assert.IsNull(LegacyLibraryRelocationService.Describe(null));
    }

    [TestMethod]
    public void Describe_OffersASiblingFolderOnTheCurrentBrand()
    {
        var plan = LegacyLibraryRelocationService.Describe(@"C:\Users\dev\Pictures\GoatShot");

        Assert.IsNotNull(plan);
        Assert.AreEqual(@"C:\Users\dev\Pictures\GoatShot", plan.Source);
        Assert.AreEqual(@"C:\Users\dev\Pictures\Receipts", plan.Target);
    }

    [TestMethod]
    public void Describe_IsCaseInsensitiveAboutTheLegacyFolderName()
    {
        Assert.IsNotNull(LegacyLibraryRelocationService.Describe(@"C:\Users\dev\Pictures\goatshot"));
    }

    [TestMethod]
    public void Relocate_MovesEveryFileAndReportsTheNewRoot()
    {
        var root = TempRoot();
        var source = Path.Combine(root, "GoatShot");
        Directory.CreateDirectory(Path.Combine(source, "Images"));
        File.WriteAllText(Path.Combine(source, "Images", "capture.png"), "bytes");

        var plan = LegacyLibraryRelocationService.Describe(source);
        Assert.IsNotNull(plan);

        var result = LegacyLibraryRelocationService.Relocate(plan);

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.IsFalse(Directory.Exists(source));
        Assert.AreEqual("bytes", File.ReadAllText(Path.Combine(plan.Target, "Images", "capture.png")));
        Directory.Delete(root, recursive: true);
    }

    [TestMethod]
    public void Relocate_RefusesWhenTheTargetAlreadyHasContentAndLeavesTheSourceIntact()
    {
        var root = TempRoot();
        var source = Path.Combine(root, "GoatShot");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "capture.png"), "original");
        var target = Path.Combine(root, "Receipts");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "existing.png"), "do not clobber");

        var plan = LegacyLibraryRelocationService.Describe(source);
        var result = LegacyLibraryRelocationService.Relocate(plan!);

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Message, "already");
        Assert.AreEqual("original", File.ReadAllText(Path.Combine(source, "capture.png")));
        Assert.AreEqual("do not clobber", File.ReadAllText(Path.Combine(target, "existing.png")));
        Directory.Delete(root, recursive: true);
    }

    [TestMethod]
    public void Relocate_AcceptsAnExistingButEmptyTarget()
    {
        var root = TempRoot();
        var source = Path.Combine(root, "GoatShot");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "capture.png"), "bytes");
        Directory.CreateDirectory(Path.Combine(root, "Receipts"));

        var result = LegacyLibraryRelocationService.Relocate(LegacyLibraryRelocationService.Describe(source)!);

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.AreEqual("bytes", File.ReadAllText(Path.Combine(root, "Receipts", "capture.png")));
        Directory.Delete(root, recursive: true);
    }

    [TestMethod]
    public void Relocate_FailsCleanlyWhenTheSourceIsMissing()
    {
        var root = TempRoot();
        var plan = LegacyLibraryRelocationService.Describe(Path.Combine(root, "GoatShot"));

        var result = LegacyLibraryRelocationService.Relocate(plan!);

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Message, "no longer");
        Directory.Delete(root, recursive: true);
    }
}
