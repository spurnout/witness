using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class BrandIdentityTests
{
    [TestMethod]
    public void Constants_KeepPublicReceiptsAndInternalCompatibilityNamesExplicit()
    {
        Assert.AreEqual("Receipts", BrandIdentity.ProductName);
        Assert.AreEqual("GoatShot", BrandIdentity.LegacyProductName);
        Assert.AreEqual("Receipts.exe", BrandIdentity.DesktopExecutableName);
        Assert.AreEqual("Receipts.Cli.exe", BrandIdentity.CommandLineExecutableName);
        Assert.AreEqual("com.receipts.bridge", BrandIdentity.NativeMessagingHostName);
        Assert.AreEqual("com.goatshot.bridge", BrandIdentity.LegacyNativeMessagingHostName);
        Assert.AreEqual("RECEIPTS_FFMPEG_PATH", BrandIdentity.EnvironmentVariable("ffmpeg_path"));
        Assert.AreEqual("GOATSHOT_FFMPEG_PATH", BrandIdentity.LegacyEnvironmentVariable("ffmpeg_path"));
    }

    [TestMethod]
    public void Resolve_PrefersReceiptsVariableAndReportsItsSource()
    {
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["RECEIPTS_LOCAL_ROOT"] = " C:\\ReceiptsData ",
            ["GOATSHOT_LOCAL_ROOT"] = "C:\\GoatShotData"
        };

        var result = BrandEnvironment.Resolve("LOCAL_ROOT", ReadVariable);

        Assert.AreEqual("C:\\ReceiptsData", result.Value);
        Assert.AreEqual("RECEIPTS_LOCAL_ROOT", result.SourceVariable);
        Assert.IsFalse(result.UsedLegacyFallback);
        return;

        string? ReadVariable(string name) => variables.GetValueOrDefault(name);
    }

    [TestMethod]
    public void Resolve_FallsBackToLegacyVariableAndReportsCompatibilityUse()
    {
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["RECEIPTS_LOCAL_ROOT"] = "  ",
            ["GOATSHOT_LOCAL_ROOT"] = "C:\\GoatShotData"
        };

        var result = BrandEnvironment.Resolve("LOCAL_ROOT", name => variables.GetValueOrDefault(name));

        Assert.AreEqual("C:\\GoatShotData", result.Value);
        Assert.AreEqual("GOATSHOT_LOCAL_ROOT", result.SourceVariable);
        Assert.IsTrue(result.UsedLegacyFallback);
    }

    [TestMethod]
    public void ResolveRoots_UsesReceiptsDefaultsWithoutConfiguredOverrides()
    {
        string? ReadVariable(string _) => null;
        string GetFolderPath(Environment.SpecialFolder folder) => folder switch
        {
            Environment.SpecialFolder.LocalApplicationData => "C:\\Profile\\AppData\\Local",
            Environment.SpecialFolder.MyPictures => "C:\\Profile\\Pictures",
            _ => throw new AssertFailedException($"Unexpected folder: {folder}")
        };

        var local = BrandEnvironment.ResolveLocalRoot(ReadVariable, GetFolderPath);
        var library = BrandEnvironment.ResolveLibraryRoot(ReadVariable, GetFolderPath);
        var legacy = BrandEnvironment.ResolveLegacyLocalRoot(ReadVariable, GetFolderPath);

        Assert.AreEqual(Path.Combine("C:\\Profile\\AppData\\Local", "Receipts"), local.Value);
        Assert.AreEqual(Path.Combine("C:\\Profile\\Pictures", "Receipts"), library.Value);
        Assert.AreEqual(Path.Combine("C:\\Profile\\AppData\\Local", "GoatShot"), legacy);
        Assert.IsFalse(local.IsConfigured);
        Assert.IsFalse(library.IsConfigured);
    }

    [TestMethod]
    public void RenderCliHelpTemplate_RebrandsExamplesWithoutInventingInternalPathsOrSchemas()
    {
        const string template = """
            goatshot workflows export --output profile.goatshot-workflow.json
            goatshot browser-extension package --output goatshot-browser-extension.zip
            goatshot manual-validation --cli-path GoatShot.Cli.exe --app-path GoatShot.exe
            goatshot manual-validation --portable GoatShot-0.1.0-win-x64-portable.zip
            Internal solution: GoatShot.slnx
            Internal project: src\GoatShot.App\GoatShot.App.csproj
            Legacy schema: goatshot.browser-capture.v1
            Legacy environment alias: GOATSHOT_LOCAL_ROOT
            """;

        var rendered = BrandIdentity.RenderCliHelpTemplate(template);

        StringAssert.Contains(rendered, "receipts workflows export --output profile.receipts-workflow.json");
        StringAssert.Contains(rendered, "receipts-browser-extension.zip");
        StringAssert.Contains(rendered, "--cli-path Receipts.Cli.exe --app-path Receipts.exe");
        StringAssert.Contains(rendered, "Receipts-0.3.0-win-x64-portable.zip");
        StringAssert.Contains(rendered, "GoatShot.slnx");
        StringAssert.Contains(rendered, "src\\GoatShot.App\\GoatShot.App.csproj");
        StringAssert.Contains(rendered, "goatshot.browser-capture.v1");
        StringAssert.Contains(rendered, "GOATSHOT_LOCAL_ROOT");
        Assert.IsFalse(rendered.Contains("Receipts.slnx", StringComparison.Ordinal));
        Assert.IsFalse(rendered.Contains("src\\Receipts.App", StringComparison.Ordinal));
    }
}
