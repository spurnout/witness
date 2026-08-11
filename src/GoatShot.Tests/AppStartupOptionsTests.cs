using GoatShot.App.Services;

namespace GoatShot.Tests;

[TestClass]
public sealed class AppStartupOptionsTests
{
    [TestMethod]
    public void Parse_BackgroundSelectsBackgroundMode()
    {
        var options = AppStartupOptions.Parse(["--background"]);

        Assert.AreEqual(AppStartupMode.Background, options.Mode);
        Assert.AreEqual(string.Empty, options.RuntimeVerb);
    }

    [TestMethod]
    public void Parse_RuntimeVerbPreservesArguments()
    {
        var options = AppStartupOptions.Parse([
            "--plugin-background-update",
            "--registry",
            "plugins.json",
            "--force"
        ]);

        Assert.AreEqual(AppStartupMode.RuntimeVerb, options.Mode);
        Assert.AreEqual("--plugin-background-update", options.RuntimeVerb);
        CollectionAssert.Contains(options.RuntimeArguments.ToList(), "plugins.json");
    }

    [TestMethod]
    public void Parse_OpensSettingsWithoutSection()
    {
        var options = AppStartupOptions.Parse(["--open-settings"]);

        Assert.IsTrue(options.OpenSettings);
        Assert.AreEqual(string.Empty, options.SettingsSection);
    }

    [TestMethod]
    public void Parse_OpensSettingsAtInlineSection()
    {
        var options = AppStartupOptions.Parse(["--open-settings", "Automation"]);

        Assert.IsTrue(options.OpenSettings);
        Assert.AreEqual("Automation", options.SettingsSection);
    }

    [TestMethod]
    public void Parse_OpensSettingsAtExplicitSection()
    {
        var options = AppStartupOptions.Parse(["--settings-section", "Recording"]);

        Assert.IsTrue(options.OpenSettings);
        Assert.AreEqual("Recording", options.SettingsSection);
    }

    [TestMethod]
    public void Parse_IgnoresMissingSectionValue()
    {
        var options = AppStartupOptions.Parse(["--settings-section", "--other"]);

        Assert.IsFalse(options.OpenSettings);
        Assert.AreEqual(string.Empty, options.SettingsSection);
    }

    [TestMethod]
    public void Parse_OpensSettingsFromEnvironmentFallback()
    {
        var options = AppStartupOptions.Parse([], "true", "Automation");

        Assert.IsTrue(options.OpenSettings);
        Assert.AreEqual("Automation", options.SettingsSection);
    }

    [TestMethod]
    public void Parse_ArgumentsOverrideEnvironmentSection()
    {
        var options = AppStartupOptions.Parse(["--open-settings", "Sharing"], "true", "Automation");

        Assert.IsTrue(options.OpenSettings);
        Assert.AreEqual("Sharing", options.SettingsSection);
    }

    [TestMethod]
    public void Parse_RendersMainWindowToOutputPath()
    {
        var options = AppStartupOptions.Parse([
            "--render-main",
            "--output",
            "main-window.png"
        ]);

        Assert.IsTrue(options.RenderMain);
        Assert.AreEqual("main-window.png", options.RenderMainOutputPath);
    }

    [TestMethod]
    public void Parse_RendersMainWindowFromExplicitOutput()
    {
        var options = AppStartupOptions.Parse([
            "--render-main-output",
            "main-window.png"
        ]);

        Assert.IsTrue(options.RenderMain);
        Assert.AreEqual("main-window.png", options.RenderMainOutputPath);
    }

    [TestMethod]
    public void Parse_RendersMainWindowFromEnvironmentFallback()
    {
        var options = AppStartupOptions.Parse(
            [],
            renderMainEnvironment: "true",
            renderMainOutputEnvironment: "main-window.png");

        Assert.IsTrue(options.RenderMain);
        Assert.AreEqual("main-window.png", options.RenderMainOutputPath);
    }

    [TestMethod]
    public void Parse_RendersSettingsSectionToOutputPath()
    {
        var options = AppStartupOptions.Parse([
            "--render-settings-section",
            "Automation",
            "--render-settings-output",
            "artifacts/settings.png"
        ]);

        Assert.IsTrue(options.RenderSettings);
        Assert.AreEqual("Automation", options.RenderSettingsSection);
        Assert.AreEqual("artifacts/settings.png", options.RenderSettingsOutputPath);
    }

    [TestMethod]
    public void Parse_RendersSettingsWithShortOutputArgument()
    {
        var options = AppStartupOptions.Parse([
            "--render-settings",
            "Recording",
            "--output",
            "recording-settings.png"
        ]);

        Assert.IsTrue(options.RenderSettings);
        Assert.AreEqual("Recording", options.RenderSettingsSection);
        Assert.AreEqual("recording-settings.png", options.RenderSettingsOutputPath);
    }

    [TestMethod]
    public void Parse_RendersSettingsFromEnvironmentFallback()
    {
        var options = AppStartupOptions.Parse(
            [],
            renderSettingsSectionEnvironment: "Automation",
            renderSettingsOutputEnvironment: "settings.png");

        Assert.IsTrue(options.RenderSettings);
        Assert.AreEqual("Automation", options.RenderSettingsSection);
        Assert.AreEqual("settings.png", options.RenderSettingsOutputPath);
    }

    [TestMethod]
    public void Parse_AuditsSettingsSectionToOutputPath()
    {
        var options = AppStartupOptions.Parse([
            "--audit-settings-section",
            "Automation",
            "--audit-settings-output",
            "artifacts/settings-audit.md"
        ]);

        Assert.IsTrue(options.AuditSettings);
        Assert.AreEqual("Automation", options.AuditSettingsSection);
        Assert.AreEqual("artifacts/settings-audit.md", options.AuditSettingsOutputPath);
    }

    [TestMethod]
    public void Parse_AuditsSettingsWithShortOutputArgument()
    {
        var options = AppStartupOptions.Parse([
            "--audit-settings",
            "Automation",
            "--output",
            "automation-accessibility.md"
        ]);

        Assert.IsTrue(options.AuditSettings);
        Assert.AreEqual("Automation", options.AuditSettingsSection);
        Assert.AreEqual("automation-accessibility.md", options.AuditSettingsOutputPath);
    }

    [TestMethod]
    public void Parse_AuditsSettingsFromEnvironmentFallback()
    {
        var options = AppStartupOptions.Parse(
            [],
            auditSettingsSectionEnvironment: "Automation",
            auditSettingsOutputEnvironment: "settings-audit.md");

        Assert.IsTrue(options.AuditSettings);
        Assert.AreEqual("Automation", options.AuditSettingsSection);
        Assert.AreEqual("settings-audit.md", options.AuditSettingsOutputPath);
    }

    [TestMethod]
    public void Parse_RendersEditorImageToOutputPath()
    {
        var options = AppStartupOptions.Parse([
            "--render-editor",
            "sample.png",
            "--output",
            "editor.png"
        ]);

        Assert.IsTrue(options.RenderEditor);
        Assert.AreEqual("sample.png", options.RenderEditorImagePath);
        Assert.AreEqual("editor.png", options.RenderEditorOutputPath);
    }

    [TestMethod]
    public void Parse_RendersEditorFromExplicitArguments()
    {
        var options = AppStartupOptions.Parse([
            "--editor-image",
            "sample.png",
            "--render-editor-output",
            "editor.png"
        ]);

        Assert.IsTrue(options.RenderEditor);
        Assert.AreEqual("sample.png", options.RenderEditorImagePath);
        Assert.AreEqual("editor.png", options.RenderEditorOutputPath);
    }

    [TestMethod]
    public void Parse_RendersEditorFromEnvironmentFallback()
    {
        var options = AppStartupOptions.Parse(
            [],
            renderEditorImageEnvironment: "sample.png",
            renderEditorOutputEnvironment: "editor.png");

        Assert.IsTrue(options.RenderEditor);
        Assert.AreEqual("sample.png", options.RenderEditorImagePath);
        Assert.AreEqual("editor.png", options.RenderEditorOutputPath);
    }

    [TestMethod]
    public void Parse_RendersTrayMenuToOutputPath()
    {
        var options = AppStartupOptions.Parse([
            "--render-tray-menu",
            "--output",
            "tray-menu.png"
        ]);

        Assert.IsTrue(options.RenderTrayMenu);
        Assert.AreEqual("tray-menu.png", options.RenderTrayMenuOutputPath);
    }

    [TestMethod]
    public void Parse_RendersTrayMenuFromExplicitOutput()
    {
        var options = AppStartupOptions.Parse([
            "--render-tray-menu-output",
            "tray-menu.png"
        ]);

        Assert.IsTrue(options.RenderTrayMenu);
        Assert.AreEqual("tray-menu.png", options.RenderTrayMenuOutputPath);
    }

    [TestMethod]
    public void Parse_RendersTrayMenuFromEnvironmentFallback()
    {
        var options = AppStartupOptions.Parse(
            [],
            renderTrayMenuEnvironment: "true",
            renderTrayMenuOutputEnvironment: "tray-menu.png");

        Assert.IsTrue(options.RenderTrayMenu);
        Assert.AreEqual("tray-menu.png", options.RenderTrayMenuOutputPath);
    }

    [TestMethod]
    public void Parse_RendersCaptureOverlayToOutputPath()
    {
        var options = AppStartupOptions.Parse([
            "--render-capture-overlay",
            "--output",
            "overlay.png"
        ]);

        Assert.IsTrue(options.RenderCaptureOverlay);
        Assert.AreEqual("overlay.png", options.RenderCaptureOverlayOutputPath);
    }

    [TestMethod]
    public void Parse_RendersCaptureOverlayFromExplicitOutput()
    {
        var options = AppStartupOptions.Parse([
            "--render-capture-overlay-output",
            "overlay.png"
        ]);

        Assert.IsTrue(options.RenderCaptureOverlay);
        Assert.AreEqual("overlay.png", options.RenderCaptureOverlayOutputPath);
    }

    [TestMethod]
    public void Parse_RendersCaptureOverlayFromEnvironmentFallback()
    {
        var options = AppStartupOptions.Parse(
            [],
            renderCaptureOverlayEnvironment: "true",
            renderCaptureOverlayOutputEnvironment: "overlay.png");

        Assert.IsTrue(options.RenderCaptureOverlay);
        Assert.AreEqual("overlay.png", options.RenderCaptureOverlayOutputPath);
    }

    [TestMethod]
    public void Parse_ShowsProofScene()
    {
        var options = AppStartupOptions.Parse(["--proof-scene"]);

        Assert.IsTrue(options.ShowProofScene);
        Assert.IsFalse(options.RenderProofScene);
    }

    [TestMethod]
    public void Parse_RendersProofSceneToOutputPath()
    {
        var options = AppStartupOptions.Parse([
            "--render-proof-scene",
            "--output",
            "proof-scene.png"
        ]);

        Assert.IsTrue(options.RenderProofScene);
        Assert.AreEqual("proof-scene.png", options.RenderProofSceneOutputPath);
    }

    [TestMethod]
    public void Parse_RendersProofSceneFromExplicitOutput()
    {
        var options = AppStartupOptions.Parse([
            "--render-proof-scene-output",
            "proof-scene.png"
        ]);

        Assert.IsTrue(options.RenderProofScene);
        Assert.AreEqual("proof-scene.png", options.RenderProofSceneOutputPath);
    }

    [TestMethod]
    public void Parse_RendersProofSceneFromEnvironmentFallback()
    {
        var options = AppStartupOptions.Parse(
            [],
            renderProofSceneEnvironment: "true",
            renderProofSceneOutputEnvironment: "proof-scene.png");

        Assert.IsTrue(options.RenderProofScene);
        Assert.AreEqual("proof-scene.png", options.RenderProofSceneOutputPath);
    }

    [TestMethod]
    public void Parse_RecordsProofSceneToOutputPath()
    {
        var options = AppStartupOptions.Parse([
            "--record-proof-scene",
            "--output",
            "proof-scene.mp4"
        ]);

        Assert.IsTrue(options.RecordProofScene);
        Assert.AreEqual("proof-scene.mp4", options.RecordProofSceneOutputPath);
        Assert.AreEqual(10d, options.RecordProofSceneDurationSeconds);
    }

    [TestMethod]
    public void Parse_RecordsProofSceneFromExplicitOutputAndDuration()
    {
        var options = AppStartupOptions.Parse([
            "--record-proof-scene-output",
            "proof-scene.mp4",
            "--record-proof-scene-duration",
            "12.5"
        ]);

        Assert.IsTrue(options.RecordProofScene);
        Assert.AreEqual("proof-scene.mp4", options.RecordProofSceneOutputPath);
        Assert.AreEqual(12.5d, options.RecordProofSceneDurationSeconds);
    }

    [TestMethod]
    public void Parse_RecordsProofSceneFromEnvironmentFallback()
    {
        var options = AppStartupOptions.Parse(
            [],
            recordProofSceneEnvironment: "true",
            recordProofSceneOutputEnvironment: "proof-scene.mp4",
            recordProofSceneDurationEnvironment: "9");

        Assert.IsTrue(options.RecordProofScene);
        Assert.AreEqual("proof-scene.mp4", options.RecordProofSceneOutputPath);
        Assert.AreEqual(9d, options.RecordProofSceneDurationSeconds);
    }

    [TestMethod]
    public void Parse_AuditsWpfSurfaceToOutputPath()
    {
        var options = AppStartupOptions.Parse([
            "--audit-wpf",
            "recording",
            "--output",
            "recording-accessibility.md"
        ]);

        Assert.IsTrue(options.AuditWpf);
        Assert.AreEqual("recording", options.AuditWpfSurface);
        Assert.AreEqual("recording-accessibility.md", options.AuditWpfOutputPath);
    }

    [TestMethod]
    public void Parse_RendersUploadTaskWindowToOutputPath()
    {
        var options = AppStartupOptions.Parse([
            "--render-upload-task",
            "result",
            "--output",
            "upload-result.png"
        ]);

        Assert.IsTrue(options.RenderUploadTask);
        Assert.AreEqual("result", options.RenderUploadTaskSurface);
        Assert.AreEqual("upload-result.png", options.RenderUploadTaskOutputPath);
    }

    [TestMethod]
    public void Parse_RendersUploadTaskWindowFromExplicitArguments()
    {
        var options = AppStartupOptions.Parse([
            "--render-upload-task-surface",
            "confirm",
            "--render-upload-task-output",
            "upload-confirm.png"
        ]);

        Assert.IsTrue(options.RenderUploadTask);
        Assert.AreEqual("confirm", options.RenderUploadTaskSurface);
        Assert.AreEqual("upload-confirm.png", options.RenderUploadTaskOutputPath);
    }

    [TestMethod]
    public void Parse_RendersUploadTaskWindowFromEnvironmentFallback()
    {
        var options = AppStartupOptions.Parse(
            [],
            renderUploadTaskSurfaceEnvironment: "result",
            renderUploadTaskOutputEnvironment: "upload-result.png");

        Assert.IsTrue(options.RenderUploadTask);
        Assert.AreEqual("result", options.RenderUploadTaskSurface);
        Assert.AreEqual("upload-result.png", options.RenderUploadTaskOutputPath);
    }

    [TestMethod]
    public void Parse_RendersShareHistoryToOutputPath()
    {
        var options = AppStartupOptions.Parse([
            "--render-share-history",
            "--output",
            "share-history.png"
        ]);

        Assert.IsTrue(options.RenderShareHistory);
        Assert.AreEqual("share-history.png", options.RenderShareHistoryOutputPath);
    }

    [TestMethod]
    public void Parse_RendersShareHistoryFromExplicitOutput()
    {
        var options = AppStartupOptions.Parse([
            "--render-share-history-output",
            "share-history.png"
        ]);

        Assert.IsTrue(options.RenderShareHistory);
        Assert.AreEqual("share-history.png", options.RenderShareHistoryOutputPath);
    }

    [TestMethod]
    public void Parse_RendersShareHistoryFromEnvironmentFallback()
    {
        var options = AppStartupOptions.Parse(
            [],
            renderShareHistoryEnvironment: "true",
            renderShareHistoryOutputEnvironment: "share-history.png");

        Assert.IsTrue(options.RenderShareHistory);
        Assert.AreEqual("share-history.png", options.RenderShareHistoryOutputPath);
    }

    [TestMethod]
    public void Parse_AuditsWpfSurfaceFromExplicitArguments()
    {
        var options = AppStartupOptions.Parse([
            "--audit-wpf-surface",
            "editor",
            "--audit-wpf-output",
            "editor-accessibility.md"
        ]);

        Assert.IsTrue(options.AuditWpf);
        Assert.AreEqual("editor", options.AuditWpfSurface);
        Assert.AreEqual("editor-accessibility.md", options.AuditWpfOutputPath);
    }

    [TestMethod]
    public void Parse_AuditsWpfSurfaceFromEnvironmentFallback()
    {
        var options = AppStartupOptions.Parse(
            [],
            auditWpfSurfaceEnvironment: "main",
            auditWpfOutputEnvironment: "main-accessibility.md");

        Assert.IsTrue(options.AuditWpf);
        Assert.AreEqual("main", options.AuditWpfSurface);
        Assert.AreEqual("main-accessibility.md", options.AuditWpfOutputPath);
    }
}
