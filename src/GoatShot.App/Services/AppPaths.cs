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
        DocumentsRoot = Path.Combine(libraryRoot, "Documents");
        ProjectsRoot = Path.Combine(libraryRoot, "Projects");
        PluginsRoot = Path.Combine(localRoot, "plugins");
        PluginStagingRoot = Path.Combine(localRoot, "plugin-staging");
        ModelStagingRoot = Path.Combine(localRoot, "model-staging");
        PersonSegmentationModelStagingRoot = Path.Combine(ModelStagingRoot, "person-segmentation");
        BrowserBridgeRoot = Path.Combine(localRoot, "browser-bridge");
        ThumbnailRoot = Path.Combine(localRoot, "thumbnails");
        TempRoot = Path.Combine(localRoot, "temp");
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
    public string DocumentsRoot { get; }
    public string ProjectsRoot { get; }
    public string PluginsRoot { get; }
    public string PluginStagingRoot { get; }
    public string ModelStagingRoot { get; }
    public string PersonSegmentationModelStagingRoot { get; }
    public string BrowserBridgeRoot { get; }
    public string ThumbnailRoot { get; }
    public string TempRoot { get; }
    public string LogsRoot { get; }
    public string AiOutputRoot { get; }
    public string SecretsRoot { get; }
    public string AiActionHistoryPath { get; }
    public string IndexPath { get; }
    public string MetadataDatabasePath { get; }
    public string SettingsPath { get; }
    public string ShareHistoryPath { get; }
    public string UploadQueuePath { get; }

    public static AppPaths Create(AppSettings settings)
    {
        var localRoot = DefaultLocalRoot();

        var defaultLibrary = DefaultLibraryRoot();

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
        Directory.CreateDirectory(DocumentsRoot);
        Directory.CreateDirectory(ProjectsRoot);
        Directory.CreateDirectory(PluginsRoot);
        Directory.CreateDirectory(PluginStagingRoot);
        Directory.CreateDirectory(ModelStagingRoot);
        Directory.CreateDirectory(PersonSegmentationModelStagingRoot);
        Directory.CreateDirectory(BrowserBridgeRoot);
        Directory.CreateDirectory(ThumbnailRoot);
        Directory.CreateDirectory(TempRoot);
        Directory.CreateDirectory(LogsRoot);
        Directory.CreateDirectory(AiOutputRoot);
        Directory.CreateDirectory(SecretsRoot);
    }

    public static string DefaultLocalRoot()
    {
        var overridePath = Environment.GetEnvironmentVariable("GOATSHOT_LOCAL_ROOT");
        return string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GoatShot")
            : Environment.ExpandEnvironmentVariables(overridePath);
    }

    public static string DefaultLibraryRoot()
    {
        var overridePath = Environment.GetEnvironmentVariable("GOATSHOT_LIBRARY_ROOT");
        return string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "GoatShot")
            : Environment.ExpandEnvironmentVariables(overridePath);
    }
}
