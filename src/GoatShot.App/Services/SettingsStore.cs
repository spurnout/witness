using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            var (decryptedJson, hadPlaintextWebhook) = UnprotectWebhookUrls(json);
            var settings = JsonSerializer.Deserialize<AppSettings>(decryptedJson, JsonOptions) ?? new AppSettings();
            var migration = SettingsMigrationService.Migrate(settings);
            if (migration.Changed || hadPlaintextWebhook)
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

    private static (string Json, bool HadPlaintextWebhook) UnprotectWebhookUrls(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
        var hadPlaintext = false;
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

            var protectedBytes = Convert.FromBase64String(value[ProtectedPrefix.Length..]);
            root[property] = Encoding.UTF8.GetString(ProtectedData.Unprotect(
                protectedBytes,
                WebhookEntropy,
                DataProtectionScope.CurrentUser));
        }

        return (root.ToJsonString(JsonOptions), hadPlaintext);
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
