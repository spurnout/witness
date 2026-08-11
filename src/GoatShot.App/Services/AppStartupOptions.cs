namespace GoatShot.App.Services;

public sealed record AppStartupOptions(
    bool OpenSettings,
    string SettingsSection,
    bool RenderMain,
    string RenderMainOutputPath,
    bool RenderSettings,
    string RenderSettingsSection,
    string RenderSettingsOutputPath,
    bool AuditSettings,
    string AuditSettingsSection,
    string AuditSettingsOutputPath,
    bool RenderEditor,
    string RenderEditorImagePath,
    string RenderEditorOutputPath,
    bool RenderTrayMenu,
    string RenderTrayMenuOutputPath,
    bool RenderCaptureOverlay,
    string RenderCaptureOverlayOutputPath,
    bool RenderUploadTask,
    string RenderUploadTaskSurface,
    string RenderUploadTaskOutputPath,
    bool RenderShareHistory,
    string RenderShareHistoryOutputPath,
    bool RenderAiHistory,
    string RenderAiHistoryOutputPath,
    bool RenderCaptureTask,
    string RenderCaptureTaskOutputPath,
    bool ShowProofScene,
    bool RenderProofScene,
    string RenderProofSceneOutputPath,
    bool RecordProofScene,
    string RecordProofSceneOutputPath,
    double RecordProofSceneDurationSeconds,
    bool AuditWpf,
    string AuditWpfSurface,
    string AuditWpfOutputPath)
{
    public AppStartupMode Mode { get; init; } = AppStartupMode.Interactive;
    public string RuntimeVerb { get; init; } = string.Empty;
    public IReadOnlyList<string> RuntimeArguments { get; init; } = Array.Empty<string>();

    public static AppStartupOptions Parse(
        IEnumerable<string> args,
        string? openSettingsEnvironment = null,
        string? settingsSectionEnvironment = null,
        string? renderMainEnvironment = null,
        string? renderMainOutputEnvironment = null,
        string? renderSettingsSectionEnvironment = null,
        string? renderSettingsOutputEnvironment = null,
        string? auditSettingsSectionEnvironment = null,
        string? auditSettingsOutputEnvironment = null,
        string? renderEditorImageEnvironment = null,
        string? renderEditorOutputEnvironment = null,
        string? renderTrayMenuEnvironment = null,
        string? renderTrayMenuOutputEnvironment = null,
        string? renderCaptureOverlayEnvironment = null,
        string? renderCaptureOverlayOutputEnvironment = null,
        string? renderUploadTaskSurfaceEnvironment = null,
        string? renderUploadTaskOutputEnvironment = null,
        string? renderShareHistoryEnvironment = null,
        string? renderShareHistoryOutputEnvironment = null,
        string? renderAiHistoryEnvironment = null,
        string? renderAiHistoryOutputEnvironment = null,
        string? renderCaptureTaskEnvironment = null,
        string? renderCaptureTaskOutputEnvironment = null,
        string? showProofSceneEnvironment = null,
        string? renderProofSceneEnvironment = null,
        string? renderProofSceneOutputEnvironment = null,
        string? recordProofSceneEnvironment = null,
        string? recordProofSceneOutputEnvironment = null,
        string? recordProofSceneDurationEnvironment = null,
        string? auditWpfSurfaceEnvironment = null,
        string? auditWpfOutputEnvironment = null)
    {
        var openSettings = IsTruthy(openSettingsEnvironment);
        var settingsSection = settingsSectionEnvironment?.Trim() ?? string.Empty;
        var renderMain = IsTruthy(renderMainEnvironment) ||
            !string.IsNullOrWhiteSpace(renderMainOutputEnvironment);
        var renderMainOutputPath = renderMainOutputEnvironment?.Trim() ?? string.Empty;
        var renderSettings = !string.IsNullOrWhiteSpace(renderSettingsSectionEnvironment) ||
            !string.IsNullOrWhiteSpace(renderSettingsOutputEnvironment);
        var renderSettingsSection = renderSettingsSectionEnvironment?.Trim() ?? string.Empty;
        var renderSettingsOutputPath = renderSettingsOutputEnvironment?.Trim() ?? string.Empty;
        var auditSettings = !string.IsNullOrWhiteSpace(auditSettingsSectionEnvironment) ||
            !string.IsNullOrWhiteSpace(auditSettingsOutputEnvironment);
        var auditSettingsSection = auditSettingsSectionEnvironment?.Trim() ?? string.Empty;
        var auditSettingsOutputPath = auditSettingsOutputEnvironment?.Trim() ?? string.Empty;
        var renderEditor = !string.IsNullOrWhiteSpace(renderEditorImageEnvironment) ||
            !string.IsNullOrWhiteSpace(renderEditorOutputEnvironment);
        var renderEditorImagePath = renderEditorImageEnvironment?.Trim() ?? string.Empty;
        var renderEditorOutputPath = renderEditorOutputEnvironment?.Trim() ?? string.Empty;
        var renderTrayMenu = IsTruthy(renderTrayMenuEnvironment) ||
            !string.IsNullOrWhiteSpace(renderTrayMenuOutputEnvironment);
        var renderTrayMenuOutputPath = renderTrayMenuOutputEnvironment?.Trim() ?? string.Empty;
        var renderCaptureOverlay = IsTruthy(renderCaptureOverlayEnvironment) ||
            !string.IsNullOrWhiteSpace(renderCaptureOverlayOutputEnvironment);
        var renderCaptureOverlayOutputPath = renderCaptureOverlayOutputEnvironment?.Trim() ?? string.Empty;
        var renderUploadTask = !string.IsNullOrWhiteSpace(renderUploadTaskSurfaceEnvironment) ||
            !string.IsNullOrWhiteSpace(renderUploadTaskOutputEnvironment);
        var renderUploadTaskSurface = renderUploadTaskSurfaceEnvironment?.Trim() ?? string.Empty;
        var renderUploadTaskOutputPath = renderUploadTaskOutputEnvironment?.Trim() ?? string.Empty;
        var renderShareHistory = IsTruthy(renderShareHistoryEnvironment) ||
            !string.IsNullOrWhiteSpace(renderShareHistoryOutputEnvironment);
        var renderShareHistoryOutputPath = renderShareHistoryOutputEnvironment?.Trim() ?? string.Empty;
        var renderAiHistory = IsTruthy(renderAiHistoryEnvironment) ||
            !string.IsNullOrWhiteSpace(renderAiHistoryOutputEnvironment);
        var renderAiHistoryOutputPath = renderAiHistoryOutputEnvironment?.Trim() ?? string.Empty;
        var renderCaptureTask = IsTruthy(renderCaptureTaskEnvironment) ||
            !string.IsNullOrWhiteSpace(renderCaptureTaskOutputEnvironment);
        var renderCaptureTaskOutputPath = renderCaptureTaskOutputEnvironment?.Trim() ?? string.Empty;
        var showProofScene = IsTruthy(showProofSceneEnvironment);
        var renderProofScene = IsTruthy(renderProofSceneEnvironment) ||
            !string.IsNullOrWhiteSpace(renderProofSceneOutputEnvironment);
        var renderProofSceneOutputPath = renderProofSceneOutputEnvironment?.Trim() ?? string.Empty;
        var recordProofScene = IsTruthy(recordProofSceneEnvironment) ||
            !string.IsNullOrWhiteSpace(recordProofSceneOutputEnvironment);
        var recordProofSceneOutputPath = recordProofSceneOutputEnvironment?.Trim() ?? string.Empty;
        var recordProofSceneDurationSeconds = ParseDurationSeconds(recordProofSceneDurationEnvironment, 10d);
        var auditWpf = !string.IsNullOrWhiteSpace(auditWpfSurfaceEnvironment) ||
            !string.IsNullOrWhiteSpace(auditWpfOutputEnvironment);
        var auditWpfSurface = auditWpfSurfaceEnvironment?.Trim() ?? string.Empty;
        var auditWpfOutputPath = auditWpfOutputEnvironment?.Trim() ?? string.Empty;
        var list = args.ToList();

        for (var index = 0; index < list.Count; index++)
        {
            var arg = list[index];
            if (arg.Equals("--open-settings", StringComparison.OrdinalIgnoreCase))
            {
                openSettings = true;
                if (index + 1 < list.Count && !list[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    settingsSection = list[++index].Trim();
                }

                continue;
            }

            if (arg.Equals("--settings-section", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                openSettings = true;
                settingsSection = list[++index].Trim();
                continue;
            }

            if (arg.Equals("--render-main", StringComparison.OrdinalIgnoreCase))
            {
                renderMain = true;
                if (index + 1 < list.Count && !list[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    renderMainOutputPath = list[++index].Trim();
                }

                continue;
            }

            if (arg.Equals("--render-main-output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderMain = true;
                renderMainOutputPath = list[++index].Trim();
                continue;
            }

            if (renderMain &&
                arg.Equals("--output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderMainOutputPath = list[++index].Trim();
                continue;
            }

            if (arg.Equals("--render-settings", StringComparison.OrdinalIgnoreCase))
            {
                renderSettings = true;
                if (index + 1 < list.Count && !list[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    renderSettingsSection = list[++index].Trim();
                }

                continue;
            }

            if (arg.Equals("--render-settings-section", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderSettings = true;
                renderSettingsSection = list[++index].Trim();
                continue;
            }

            if (arg.Equals("--render-settings-output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderSettings = true;
                renderSettingsOutputPath = list[++index].Trim();
                continue;
            }

            if (renderSettings &&
                arg.Equals("--output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderSettingsOutputPath = list[++index].Trim();
                continue;
            }

            if (arg.Equals("--audit-settings", StringComparison.OrdinalIgnoreCase))
            {
                auditSettings = true;
                if (index + 1 < list.Count && !list[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    auditSettingsSection = list[++index].Trim();
                }

                continue;
            }

            if (arg.Equals("--audit-settings-section", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                auditSettings = true;
                auditSettingsSection = list[++index].Trim();
                continue;
            }

            if (arg.Equals("--audit-settings-output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                auditSettings = true;
                auditSettingsOutputPath = list[++index].Trim();
                continue;
            }

            if (auditSettings &&
                arg.Equals("--output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                auditSettingsOutputPath = list[++index].Trim();
                continue;
            }

            if (arg.Equals("--render-editor", StringComparison.OrdinalIgnoreCase))
            {
                renderEditor = true;
                if (index + 1 < list.Count && !list[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    renderEditorImagePath = list[++index].Trim();
                }

                continue;
            }

            if (arg.Equals("--editor-image", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderEditor = true;
                renderEditorImagePath = list[++index].Trim();
                continue;
            }

            if (arg.Equals("--render-editor-output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderEditor = true;
                renderEditorOutputPath = list[++index].Trim();
                continue;
            }

            if (renderEditor &&
                arg.Equals("--output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderEditorOutputPath = list[++index].Trim();
                continue;
            }

            if (arg.Equals("--render-tray-menu", StringComparison.OrdinalIgnoreCase))
            {
                renderTrayMenu = true;
                continue;
            }

            if (arg.Equals("--render-tray-menu-output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderTrayMenu = true;
                renderTrayMenuOutputPath = list[++index].Trim();
                continue;
            }

            if (renderTrayMenu &&
                arg.Equals("--output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderTrayMenuOutputPath = list[++index].Trim();
                continue;
            }

            if (arg.Equals("--render-capture-overlay", StringComparison.OrdinalIgnoreCase))
            {
                renderCaptureOverlay = true;
                continue;
            }

            if (arg.Equals("--render-capture-overlay-output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderCaptureOverlay = true;
                renderCaptureOverlayOutputPath = list[++index].Trim();
                continue;
            }

            if (renderCaptureOverlay &&
                arg.Equals("--output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderCaptureOverlayOutputPath = list[++index].Trim();
                continue;
            }

            if (arg.Equals("--render-upload-task", StringComparison.OrdinalIgnoreCase))
            {
                renderUploadTask = true;
                if (index + 1 < list.Count && !list[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    renderUploadTaskSurface = list[++index].Trim();
                }

                continue;
            }

            if (arg.Equals("--render-upload-task-surface", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderUploadTask = true;
                renderUploadTaskSurface = list[++index].Trim();
                continue;
            }

            if (arg.Equals("--render-upload-task-output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderUploadTask = true;
                renderUploadTaskOutputPath = list[++index].Trim();
                continue;
            }

            if (renderUploadTask &&
                arg.Equals("--output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderUploadTaskOutputPath = list[++index].Trim();
                continue;
            }

            if (arg.Equals("--render-share-history", StringComparison.OrdinalIgnoreCase))
            {
                renderShareHistory = true;
                continue;
            }

            if (arg.Equals("--render-share-history-output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderShareHistory = true;
                renderShareHistoryOutputPath = list[++index].Trim();
                continue;
            }

            if (renderShareHistory &&
                arg.Equals("--output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderShareHistoryOutputPath = list[++index].Trim();
                continue;
            }

            if (arg.Equals("--render-ai-history", StringComparison.OrdinalIgnoreCase))
            {
                renderAiHistory = true;
                continue;
            }

            if (arg.Equals("--render-ai-history-output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderAiHistory = true;
                renderAiHistoryOutputPath = list[++index].Trim();
                continue;
            }

            if (renderAiHistory &&
                arg.Equals("--output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderAiHistoryOutputPath = list[++index].Trim();
                continue;
            }

            if (arg.Equals("--render-capture-task", StringComparison.OrdinalIgnoreCase))
            {
                renderCaptureTask = true;
                continue;
            }

            if (arg.Equals("--render-capture-task-output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderCaptureTask = true;
                renderCaptureTaskOutputPath = list[++index].Trim();
                continue;
            }

            if (renderCaptureTask &&
                arg.Equals("--output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderCaptureTaskOutputPath = list[++index].Trim();
                continue;
            }

            if (arg.Equals("--proof-scene", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--show-proof-scene", StringComparison.OrdinalIgnoreCase))
            {
                showProofScene = true;
                continue;
            }

            if (arg.Equals("--render-proof-scene", StringComparison.OrdinalIgnoreCase))
            {
                renderProofScene = true;
                if (index + 1 < list.Count && !list[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    renderProofSceneOutputPath = list[++index].Trim();
                }

                continue;
            }

            if (arg.Equals("--render-proof-scene-output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderProofScene = true;
                renderProofSceneOutputPath = list[++index].Trim();
                continue;
            }

            if (renderProofScene &&
                arg.Equals("--output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                renderProofSceneOutputPath = list[++index].Trim();
                continue;
            }

            if (arg.Equals("--record-proof-scene", StringComparison.OrdinalIgnoreCase))
            {
                recordProofScene = true;
                if (index + 1 < list.Count && !list[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    recordProofSceneOutputPath = list[++index].Trim();
                }

                continue;
            }

            if ((arg.Equals("--record-proof-scene-output", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--proof-scene-recording-output", StringComparison.OrdinalIgnoreCase)) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                recordProofScene = true;
                recordProofSceneOutputPath = list[++index].Trim();
                continue;
            }

            if ((arg.Equals("--record-proof-scene-duration", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--proof-scene-recording-duration", StringComparison.OrdinalIgnoreCase)) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                recordProofScene = true;
                recordProofSceneDurationSeconds = ParseDurationSeconds(list[++index], recordProofSceneDurationSeconds);
                continue;
            }

            if (recordProofScene &&
                arg.Equals("--duration", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                recordProofSceneDurationSeconds = ParseDurationSeconds(list[++index], recordProofSceneDurationSeconds);
                continue;
            }

            if (recordProofScene &&
                arg.Equals("--output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                recordProofSceneOutputPath = list[++index].Trim();
                continue;
            }

            if (arg.Equals("--audit-wpf", StringComparison.OrdinalIgnoreCase))
            {
                auditWpf = true;
                if (index + 1 < list.Count && !list[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    auditWpfSurface = list[++index].Trim();
                }

                continue;
            }

            if (arg.Equals("--audit-wpf-surface", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                auditWpf = true;
                auditWpfSurface = list[++index].Trim();
                continue;
            }

            if (arg.Equals("--audit-wpf-output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                auditWpf = true;
                auditWpfOutputPath = list[++index].Trim();
                continue;
            }

            if (auditWpf &&
                arg.Equals("--output", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < list.Count &&
                !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                auditWpfOutputPath = list[++index].Trim();
            }
        }

        var runtimeVerb = GetRuntimeVerb(list);
        var mode = list.Any(arg => arg.Equals("--background", StringComparison.OrdinalIgnoreCase))
            ? AppStartupMode.Background
            : string.IsNullOrWhiteSpace(runtimeVerb)
                ? AppStartupMode.Interactive
                : AppStartupMode.RuntimeVerb;

        return new AppStartupOptions(
            openSettings,
            settingsSection,
            renderMain,
            renderMainOutputPath,
            renderSettings,
            renderSettingsSection,
            renderSettingsOutputPath,
            auditSettings,
            auditSettingsSection,
            auditSettingsOutputPath,
            renderEditor,
            renderEditorImagePath,
            renderEditorOutputPath,
            renderTrayMenu,
            renderTrayMenuOutputPath,
            renderCaptureOverlay,
            renderCaptureOverlayOutputPath,
            renderUploadTask,
            renderUploadTaskSurface,
            renderUploadTaskOutputPath,
            renderShareHistory,
            renderShareHistoryOutputPath,
            renderAiHistory,
            renderAiHistoryOutputPath,
            renderCaptureTask,
            renderCaptureTaskOutputPath,
            showProofScene,
            renderProofScene,
            renderProofSceneOutputPath,
            recordProofScene,
            recordProofSceneOutputPath,
            recordProofSceneDurationSeconds,
            auditWpf,
            auditWpfSurface,
            auditWpfOutputPath)
        {
            Mode = mode,
            RuntimeVerb = runtimeVerb,
            RuntimeArguments = list
        };
    }

    private static string GetRuntimeVerb(IReadOnlyList<string> args)
    {
        var knownVerbs = new[]
        {
            "--install",
            "--repair",
            "--uninstall",
            "--complete-uninstall",
            "--browser-native-host",
            "--plugin-background-update",
            "--runtime-diagnostics"
        };

        return args.FirstOrDefault(arg => knownVerbs.Contains(arg, StringComparer.OrdinalIgnoreCase)) ?? string.Empty;
    }

    private static bool IsTruthy(string? value)
    {
        return value is not null &&
            (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             value.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private static double ParseDurationSeconds(string? value, double fallback)
    {
        return double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) &&
            parsed > 0
            ? parsed
            : fallback;
    }
}

public enum AppStartupMode
{
    Interactive,
    Background,
    RuntimeVerb
}
