namespace GoatShot.App.Models;

public sealed class WorkflowProfileWatchFolders
{
    public bool EnableWatchFolders { get; set; }
    public bool IncludeSubdirectories { get; set; }
    public bool AutoImport { get; set; } = true;
    public bool CopyImageAfterImport { get; set; }
    public bool StripMetadataAfterImport { get; set; }
    public bool ShareAfterImport { get; set; }
    public bool EnableVirtualPrinterImport { get; set; }
    public string VirtualPrinterImportFolder { get; set; } = string.Empty;
    public bool VirtualPrinterImportIncludeSubdirectories { get; set; }
    public List<string> WatchFolders { get; set; } = new();
}
