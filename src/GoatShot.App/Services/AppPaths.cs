using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class AppPaths
{
    private AppPaths(string localRoot, string libraryRoot)
    {
        LocalRoot = localRoot;
        LibraryRoot = libraryRoot;
        ImagesRoot = Path.Combine(libraryRoot, "Images");
        VideosRoot = Path.Combine(libraryRoot, "Videos");
        ReceiptsRoot = Path.Combine(libraryRoot, "Receipts");
        DocumentsRoot = Path.Combine(libraryRoot, "Documents");
        ProjectsRoot = Path.Combine(libraryRoot, "Projects");
        PluginsRoot = Path.Combine(localRoot, "plugins");
        PluginStagingRoot = Path.Combine(localRoot, "plugin-staging");
        ModelStagingRoot = Path.Combine(localRoot, "model-staging");
        PersonSegmentationModelStagingRoot = Path.Combine(ModelStagingRoot, "person-segmentation");
        BrowserBridgeRoot = Path.Combine(localRoot, "browser-bridge");
        ThumbnailRoot = Path.Combine(localRoot, "thumbnails");
        TempRoot = Path.Combine(localRoot, "temp");
        ReplayBufferRoot = Path.Combine(localRoot, "replay-buffer");
        LogsRoot = Path.Combine(localRoot, "logs");
        AiOutputRoot = Path.Combine(localRoot, "ai-output");
        SecretsRoot = Path.Combine(localRoot, "secrets");
        AiActionHistoryPath = Path.Combine(localRoot, "ai-action-history.json");
        IndexPath = Path.Combine(localRoot, "workspace-index.json");
        MetadataDatabasePath = Path.Combine(localRoot, "workspace.sqlite");
        SettingsPath = Path.Combine(localRoot, "settings.json");
        ShareHistoryPath = Path.Combine(localRoot, "share-history.json");
        UploadQueuePath = Path.Combine(localRoot, "upload-queue.json");
    }

    public string LocalRoot { get; }
    public string LibraryRoot { get; }
    public string ImagesRoot { get; }
    public string VideosRoot { get; }
    public string ReceiptsRoot { get; }
    public string DocumentsRoot { get; }
    public string ProjectsRoot { get; }
    public string PluginsRoot { get; }
    public string PluginStagingRoot { get; }
    public string ModelStagingRoot { get; }
    public string PersonSegmentationModelStagingRoot { get; }
    public string BrowserBridgeRoot { get; }
    public string ThumbnailRoot { get; }
    public string TempRoot { get; }
    public string ReplayBufferRoot { get; }
    public string LogsRoot { get; }
    public string AiOutputRoot { get; }
    public string SecretsRoot { get; }
    public string AiActionHistoryPath { get; }
    public string IndexPath { get; }
    public string MetadataDatabasePath { get; }
    public string SettingsPath { get; }
    public string ShareHistoryPath { get; }
    public string UploadQueuePath { get; }

    public static AppPaths Create(AppSettings settings, string? localRootOverride = null)
    {
        var localResolution = BrandEnvironment.ResolveLocalRoot();
        var localRoot = string.IsNullOrWhiteSpace(localRootOverride)
            ? localResolution.Value
            : Path.GetFullPath(localRootOverride);
        if (string.IsNullOrWhiteSpace(localRootOverride) && localResolution.UsedLegacyFallback)
        {
            StartupTrace.Write($"Legacy environment alias in use: {localResolution.SourceVariable}; prefer {BrandIdentity.EnvironmentVariable(BrandEnvironment.LocalRootSuffix)}.");
        }

        var libraryResolution = BrandEnvironment.ResolveLibraryRoot();
        var defaultLibrary = libraryResolution.Value;
        if (libraryResolution.UsedLegacyFallback)
        {
            StartupTrace.Write($"Legacy environment alias in use: {libraryResolution.SourceVariable}; prefer {BrandIdentity.EnvironmentVariable(BrandEnvironment.LibraryRootSuffix)}.");
        }

        var libraryRoot = string.IsNullOrWhiteSpace(settings.LibraryRoot)
            ? defaultLibrary
            : Environment.ExpandEnvironmentVariables(settings.LibraryRoot);

        var paths = new AppPaths(localRoot, libraryRoot);
        paths.EnsureCreated();
        settings.LibraryRoot = libraryRoot;
        return paths;
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(LocalRoot);
        Directory.CreateDirectory(LibraryRoot);
        Directory.CreateDirectory(ImagesRoot);
        Directory.CreateDirectory(VideosRoot);
        Directory.CreateDirectory(ReceiptsRoot);
        Directory.CreateDirectory(DocumentsRoot);
        Directory.CreateDirectory(ProjectsRoot);
        Directory.CreateDirectory(PluginsRoot);
        Directory.CreateDirectory(PluginStagingRoot);
        Directory.CreateDirectory(ModelStagingRoot);
        Directory.CreateDirectory(PersonSegmentationModelStagingRoot);
        Directory.CreateDirectory(BrowserBridgeRoot);
        Directory.CreateDirectory(ThumbnailRoot);
        Directory.CreateDirectory(TempRoot);
        Directory.CreateDirectory(ReplayBufferRoot);
        Directory.CreateDirectory(LogsRoot);
        Directory.CreateDirectory(AiOutputRoot);
        Directory.CreateDirectory(SecretsRoot);
    }

    public static string DefaultLocalRoot()
    {
        return BrandEnvironment.ResolveLocalRoot().Value;
    }

    public static string DefaultLibraryRoot()
    {
        return BrandEnvironment.ResolveLibraryRoot().Value;
    }
}
