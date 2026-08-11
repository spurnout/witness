using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class PluginUpdateSurfaceServiceTests
{
    [TestMethod]
    public void Create_FormatsCountsRowsAndMutationBoundary()
    {
        var result = new RemotePluginUpdateSummaryResult
        {
            Succeeded = true,
            Message = "Remote plugin update summary: available=1, staged=1, installed=1, blocked=1, incompatible=1.",
            RegistryLocation = "samples\\local-plugins\\registry.json",
            PluginsRoot = "plugins",
            StagingRoot = "staging",
            AvailableCount = 1,
            StagedCount = 1,
            InstalledCount = 1,
            BlockedCount = 1,
            IncompatibleCount = 1,
            WouldTrust = false,
            WouldEnable = false,
            WouldExecute = false,
            NextActions =
            {
                "Stage only after review.",
                "Trust and enable separately."
            },
            Plugins =
            {
                Plugin("blocked", "sample.blocked", "Blocked", installed: "", registry: "2.0.0", message: "Policy blocked.", next: "Review policy.", blocked: true),
                Plugin("available", "sample.available", "Available", installed: "1.0.0", registry: "2.0.0", message: "Update available.", next: "Stage for review.", gates: ["Plugin is not trusted."]),
                Plugin("staged", "sample.staged", "Staged", installed: "1.0.0", registry: "2.0.0", staged: "2.0.0", message: "Staged.", next: "Review staged package."),
                Plugin("incompatible", "sample.incompatible", "Incompatible", installed: "", registry: "9.0.0", message: "Incompatible.", next: "Resolve compatibility.", incompatible: true),
                Plugin("installed", "sample.installed", "Installed", installed: "1.0.0", registry: "1.0.0", message: "Installed.", next: "No action.")
            }
        };

        var surface = new PluginUpdateSurfaceService().Create(result, "samples\\local-plugins\\registry.json");

        Assert.IsTrue(surface.Succeeded);
        Assert.AreEqual("Available 1 | Staged 1 | Installed 1 | Blocked 1 | Incompatible 1", surface.CountsText);
        StringAssert.Contains(surface.MutationBoundary, "install=false");
        StringAssert.Contains(surface.MutationBoundary, "execute=false");
        Assert.AreEqual(5, surface.Rows.Count);
        Assert.AreEqual("available", surface.Rows[0].Status);
        Assert.AreEqual("staged", surface.Rows[1].Status);
        Assert.AreEqual("installed", surface.Rows[2].Status);
        Assert.AreEqual("blocked", surface.Rows[3].Status);
        Assert.AreEqual("incompatible", surface.Rows[4].Status);
        StringAssert.Contains(surface.Rows[0].VersionText, "installed 1.0.0");
        StringAssert.Contains(surface.Rows[0].GateText, "Plugin is not trusted.");
        StringAssert.Contains(surface.Rows[3].GateText, "policy blocked");
        StringAssert.Contains(surface.CliCommand, "receipts plugins updates --registry samples\\local-plugins\\registry.json --json");
    }

    [TestMethod]
    public void Create_UsesRedactedRegistryLocationForDisplayButCopiesExplicitInputCommand()
    {
        var result = new RemotePluginUpdateSummaryResult
        {
            Succeeded = true,
            RegistryLocation = "https://registry.example.test/registry.json?access_token=[REDACTED]",
            Message = "Remote plugin update summary found no available updates."
        };

        var surface = new PluginUpdateSurfaceService().Create(
            result,
            "https://registry.example.test/registry.json?access_token=super-secret-token");

        Assert.AreEqual("https://registry.example.test/registry.json?access_token=[REDACTED]", surface.RegistryLocation);
        Assert.IsFalse(surface.Message.Contains("super-secret-token", StringComparison.Ordinal));
        StringAssert.Contains(surface.CliCommand, "super-secret-token");
        StringAssert.Contains(surface.MutationBoundary, "allowlist=false");
    }

    private static RemotePluginUpdateSummary Plugin(
        string status,
        string id,
        string name,
        string installed,
        string registry,
        string message,
        string next,
        string staged = "",
        bool blocked = false,
        bool incompatible = false,
        string[]? gates = null)
    {
        var plugin = new RemotePluginUpdateSummary
        {
            Status = status,
            PluginId = id,
            Name = name,
            InstalledVersion = installed,
            RegistryVersion = registry,
            StagedVersion = staged,
            Installed = !string.IsNullOrWhiteSpace(installed),
            Staged = !string.IsNullOrWhiteSpace(staged),
            PolicyBlocked = blocked,
            Incompatible = incompatible,
            Message = message,
            NextAction = next,
            WouldTrust = false,
            WouldEnable = false,
            WouldExecute = false
        };
        if (gates is not null)
        {
            plugin.OperatorGateReasons.AddRange(gates);
        }

        return plugin;
    }
}
