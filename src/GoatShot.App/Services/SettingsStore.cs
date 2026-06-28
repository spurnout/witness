using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class SettingsStore
{
    private const int MaxFileAttempts = 6;
    private static readonly TimeSpan FileRetryDelay = TimeSpan.FromMilliseconds(40);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private string _path = Path.Combine(AppPaths.DefaultLocalRoot(), "settings.json");

    public void UsePath(string path)
    {
        _path = path;
    }

    public AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            var created = new AppSettings();
            SettingsMigrationService.Migrate(created);
            return created;
        }

        try
        {
            var json = ReadAllTextWithRetry(_path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            var migration = SettingsMigrationService.Migrate(settings);
            if (migration.Changed)
            {
                Save(settings);
            }

            return settings;
        }
        catch
        {
            var fallback = new AppSettings();
            SettingsMigrationService.Migrate(fallback);
            return fallback;
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        WriteAllTextAtomicallyWithRetry(_path, json);
    }

    private static string ReadAllTextWithRetry(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < MaxFileAttempts)
            {
                Thread.Sleep(FileRetryDelay);
            }
            catch (UnauthorizedAccessException) when (attempt < MaxFileAttempts)
            {
                Thread.Sleep(FileRetryDelay);
            }
        }
    }

    private static void WriteAllTextAtomicallyWithRetry(string path, string text)
    {
        var directory = Path.GetDirectoryName(path);
        var tempPath = Path.Combine(
            string.IsNullOrWhiteSpace(directory) ? AppContext.BaseDirectory : directory,
            $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.WriteAllText(tempPath, text);
                    File.Move(tempPath, path, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < MaxFileAttempts)
                {
                    Thread.Sleep(FileRetryDelay);
                }
                catch (UnauthorizedAccessException) when (attempt < MaxFileAttempts)
                {
                    Thread.Sleep(FileRetryDelay);
                }
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // A stale temp file is less harmful than masking the original settings write result.
            }
        }
    }
}
