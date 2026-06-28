using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class WatchFolderService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly AppPaths _paths;
    private readonly AutomationService _automationService;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly HashSet<string> _recentPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public WatchFolderService(AppSettings settings, AppPaths paths, AutomationService automationService)
    {
        _settings = settings;
        _paths = paths;
        _automationService = automationService;
    }

    public event EventHandler<string>? StatusChanged;

    public IReadOnlyList<string> ActiveFolders => _watchers.Select(watcher => watcher.Path).ToList();

    public void Restart()
    {
        Stop();
        if (!_settings.EnableWatchFolders && !_settings.EnableVirtualPrinterImport)
        {
            PublishStatus("Watch folders disabled.");
            return;
        }

        if (_settings.EnableVirtualPrinterImport)
        {
            Directory.CreateDirectory(VirtualPrinterImportService.GetDropFolder(_settings, _paths));
        }

        foreach (var folder in VirtualPrinterImportService.BuildWatchedFolders(_settings, _paths).Where(Directory.Exists))
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = ShouldIncludeSubdirectories(folder),
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite
            };
            watcher.Created += OnFileEvent;
            watcher.Renamed += OnFileEvent;
            _watchers.Add(watcher);
        }

        PublishStatus(_watchers.Count == 0
            ? "Watch folders enabled, but no configured folders exist."
            : $"Watching {_watchers.Count} folder(s).");
    }

    private bool ShouldIncludeSubdirectories(string folder)
    {
        var virtualPrinterFolder = VirtualPrinterImportService.GetDropFolder(_settings, _paths);
        if (folder.Equals(virtualPrinterFolder, StringComparison.OrdinalIgnoreCase))
        {
            return _settings.VirtualPrinterImportIncludeSubdirectories;
        }

        return _settings.WatchFolderIncludeSubdirectories;
    }

    public void Stop()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnFileEvent;
            watcher.Renamed -= OnFileEvent;
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (Directory.Exists(e.FullPath))
        {
            return;
        }

        lock (_gate)
        {
            if (!_recentPaths.Add(e.FullPath))
            {
                return;
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _automationService.ProcessWatchedFileAsync(e.FullPath);
            }
            catch (Exception ex)
            {
                PublishStatus($"Watch folder error for {e.Name}: {ex.Message}");
            }
            finally
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                lock (_gate)
                {
                    _recentPaths.Remove(e.FullPath);
                }
            }
        });
    }

    private void PublishStatus(string message)
    {
        StatusChanged?.Invoke(this, message);
    }
}
