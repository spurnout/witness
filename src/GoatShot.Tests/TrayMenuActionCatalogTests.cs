using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class TrayMenuActionCatalogTests
{
    [TestMethod]
    public void All_PreservesExpectedTrayMenuShape()
    {
        Assert.AreEqual(20, TrayMenuActionCatalog.All.Count);
        Assert.AreEqual(18, TrayMenuActionCatalog.Actions.Count());
        Assert.AreEqual(2, TrayMenuActionCatalog.All.Count(item => item.IsSeparator));
        Assert.AreEqual("Capture region", TrayMenuActionCatalog.All.First().Label);
        Assert.AreEqual("Exit", TrayMenuActionCatalog.All.Last().Label);
    }

    [TestMethod]
    public void Actions_HaveLabelsGroupsAndKinds()
    {
        foreach (var action in TrayMenuActionCatalog.Actions)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(action.Label));
            Assert.IsFalse(string.IsNullOrWhiteSpace(action.Group));
            Assert.IsTrue(action.ActionKind.HasValue);
        }
    }

    [TestMethod]
    public void Actions_CoverEveryActionKindOnce()
    {
        var actionKinds = TrayMenuActionCatalog.Actions
            .Select(action => action.ActionKind!.Value)
            .ToArray();

        CollectionAssert.AreEquivalent(
            Enum.GetValues<TrayMenuActionKind>(),
            actionKinds);
        Assert.AreEqual(actionKinds.Length, actionKinds.Distinct().Count());
    }
}
