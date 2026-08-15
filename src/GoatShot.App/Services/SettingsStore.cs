using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

/// <summary>
/// What happened during the most recent <see cref="SettingsStore.Load"/>. Surfaced so the app can
/// tell the user when configuration was reset or a secret was dropped, instead of failing silently.
/// </summary>
public sealed record SettingsLoadDiagnostics(
    bool RecoveredFromUnreadableFile,
    string? PreservedCopyPath,
    IReadOnlyList<string> Warnings)
{
    public static SettingsLoadDiagnostics Clean { get; } = new(false, null, []);

    public bool HasIssues => RecoveredFromUnreadableFile || Warnings.Count > 0;
}

public sealed class SettingsStore
{
    private const int MaxFileAttempts = 6;
    private static readonly TimeSpan FileRetryDelay = TimeSpan.FromMilliseconds(40);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private static readonly byte[] WebhookEntropy = Encoding.UTF8.GetBytes("Receipts.Settings.WebhookUrls.v1");
    private static readonly string[] ProtectedWebhookProperties =
    [
        "customWebhookUrl",
        "slackWebhookUrl",
        "discordWebhookUrl",
        "teamsWebhookUrl"
    ];
    private const string ProtectedPrefix = "dpapi:v1:";

    private string _path = Path.Combine(AppPaths.DefaultLocalRoot(), "settings.json");

    /// <summary>Result of the most recent <see cref="Load"/>; clean until one runs.</summary>
    public SettingsLoadDiagnostics LastLoadDiagnostics { get; private set; } = SettingsLoadDiagnostics.Clean;

    public void UsePath(string path)
    {
        _path = path;
    }

    public AppSettings Load()
    {
        LastLoadDiagnostics = SettingsLoadDiagnostics.Clean;
        if (!File.Exists(_path))
        {
            var created = new AppSettings();
            SettingsMigrationService.Migrate(created);
            return created;
        }

        try
        {
            var json = ReadAllTextWithRetry(_path);
            var (decryptedJson, hadPlaintextWebhook, warnings) = UnprotectWebhookUrls(json);
            var settings = JsonSerializer.Deserialize<AppSettings>(decryptedJson, JsonOptions) ?? new AppSettings();
            var migration = SettingsMigrationService.Migrate(settings);
            LastLoadDiagnostics = new SettingsLoadDiagnostics(false, null, warnings);
            if (migration.Changed || hadPlaintextWebhook || warnings.Count > 0)
            {
                Save(settings);
            }

            return settings;
        }
        catch (Exception ex)
        {
            // The caller re-saves immediately after Load, so returning defaults here would overwrite
            // the user's real configuration for good. Move the unreadable file aside first.
            var preserved = TryPreserveUnreadableFile();
            var fallback = new AppSettings();
            SettingsMigrationService.Migrate(fallback);
            LastLoadDiagnostics = new SettingsLoadDiagnostics(
                true,
                preserved,
                [
                    preserved is null
                        ? $"Settings could not be read ({ex.GetType().Name}) and the existing file could not be preserved."
                        : $"Settings could not be read ({ex.GetType().Name}). The previous file was kept at {preserved}."
                ]);
            return fallback;
        }
    }

    /// <summary>
    /// Renames the unreadable settings file so a later <see cref="Save"/> cannot destroy it. Returns
    /// the new path, or null when even the rename failed.
    /// </summary>
    private string? TryPreserveUnreadableFile()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var directory = Path.GetDirectoryName(_path);
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var name = $"{Path.GetFileNameWithoutExtension(_path)}.unreadable-{stamp}{Path.GetExtension(_path)}";
            var target = string.IsNullOrWhiteSpace(directory) ? name : Path.Combine(directory, name);
            if (File.Exists(target))
            {
                target = string.IsNullOrWhiteSpace(directory)
                    ? $"{name}.{Guid.NewGuid():N}"
                    : Path.Combine(directory, $"{name}.{Guid.NewGuid():N}");
            }

            File.Move(_path, target);
            return target;
        }
        catch
        {
            return null;
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = ProtectWebhookUrls(JsonSerializer.Serialize(settings, JsonOptions));
        WriteAllTextAtomicallyWithRetry(_path, json);
    }

    private static string ProtectWebhookUrls(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
        foreach (var property in ProtectedWebhookProperties)
        {
            var value = root[property]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value) || value.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(value),
                WebhookEntropy,
                DataProtectionScope.CurrentUser);
            root[property] = ProtectedPrefix + Convert.ToBase64String(protectedBytes);
        }

        return root.ToJsonString(JsonOptions);
    }

    private static (string Json, bool HadPlaintextWebhook, IReadOnlyList<string> Warnings) UnprotectWebhookUrls(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
        var hadPlaintext = false;
        var warnings = new List<string>();
        foreach (var property in ProtectedWebhookProperties)
        {
            var value = root[property]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (!value.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            {
                hadPlaintext = true;
                continue;
            }

            // A blob written by a different Windows account or machine cannot be unprotected here.
            // Drop that one secret and keep the rest of the file rather than failing the whole load.
            try
            {
                var protectedBytes = Convert.FromBase64String(value[ProtectedPrefix.Length..]);
                root[property] = Encoding.UTF8.GetString(ProtectedData.Unprotect(
                    protectedBytes,
                    WebhookEntropy,
                    DataProtectionScope.CurrentUser));
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                root[property] = string.Empty;
                warnings.Add(
                    $"The saved {DescribeWebhookProperty(property)} could not be decrypted on this " +
                    "Windows account and was cleared. Re-enter it in Settings.");
            }
        }

        return (root.ToJsonString(JsonOptions), hadPlaintext, warnings);
    }

    private static string DescribeWebhookProperty(string property) => property switch
    {
        "slackWebhookUrl" => "Slack webhook URL",
        "discordWebhookUrl" => "Discord webhook URL",
        "teamsWebhookUrl" => "Microsoft Teams webhook URL",
        "customWebhookUrl" => "custom webhook URL",
        _ => property
    };

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
