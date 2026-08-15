using GoatShot.App.Models;
using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class PostCaptureActionCatalogTests
{
    [TestMethod]
    public void Parse_ReadsEveryStoredValueRegardlessOfCasing()
    {
        Assert.AreEqual(PostCaptureAction.CopyQuietly, PostCaptureActionCatalog.Parse("CopyQuietly"));
        Assert.AreEqual(PostCaptureAction.ShowActionsWindow, PostCaptureActionCatalog.Parse("showactionswindow"));
        Assert.AreEqual(PostCaptureAction.OpenEditor, PostCaptureActionCatalog.Parse("  OpenEditor  "));
    }

    [TestMethod]
    public void Parse_FallsBackToQuietCopyForAnythingUnrecognized()
    {
        // settings.json is hand-editable, so garbage must degrade to the default rather than throw.
        Assert.AreEqual(PostCaptureAction.CopyQuietly, PostCaptureActionCatalog.Parse(null));
        Assert.AreEqual(PostCaptureAction.CopyQuietly, PostCaptureActionCatalog.Parse(string.Empty));
        Assert.AreEqual(PostCaptureAction.CopyQuietly, PostCaptureActionCatalog.Parse("   "));
        Assert.AreEqual(PostCaptureAction.CopyQuietly, PostCaptureActionCatalog.Parse("OpenTheThing"));
        Assert.AreEqual(PostCaptureAction.CopyQuietly, PostCaptureActionCatalog.Parse("7"));
    }

    [TestMethod]
    public void Normalize_RewritesLooseInputToTheCanonicalStoredValue()
    {
        Assert.AreEqual("OpenEditor", PostCaptureActionCatalog.Normalize("openeditor"));
        Assert.AreEqual("CopyQuietly", PostCaptureActionCatalog.Normalize("nonsense"));
    }

    [TestMethod]
    public void Options_CoverEveryActionWithDistinctLabels()
    {
        var actions = PostCaptureActionCatalog.Options.Select(option => option.Action).ToList();
        CollectionAssert.AreEquivalent(Enum.GetValues<PostCaptureAction>(), actions);

        foreach (var option in PostCaptureActionCatalog.Options)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(option.Label));
            Assert.IsFalse(string.IsNullOrWhiteSpace(option.Description));
            Assert.AreEqual(option.Action.ToString(), option.StorageValue);
            Assert.AreSame(option, PostCaptureActionCatalog.Describe(option.Action));
        }
    }
}
