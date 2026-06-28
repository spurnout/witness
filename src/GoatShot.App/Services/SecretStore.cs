using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class SecretStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppPaths _paths;

    public SecretStore(AppPaths paths)
    {
        _paths = paths;
    }

    public bool HasGeminiApiKey => File.Exists(GeminiApiKeyPath);
    public bool HasS3Credentials => File.Exists(S3AccessKeyIdPath) && File.Exists(S3SecretAccessKeyPath);
    public bool HasImgurClientId => File.Exists(ImgurClientIdPath);
    public bool HasCloudinaryCredentials => File.Exists(CloudinaryApiKeyPath) && File.Exists(CloudinaryApiSecretPath);
    public bool HasDropboxAccessToken => File.Exists(DropboxAccessTokenPath);
    public bool HasGoogleDriveAccessToken => File.Exists(GoogleDriveAccessTokenPath);
    public bool HasGooglePhotosAccessToken => File.Exists(GooglePhotosAccessTokenPath);
    public bool HasOneDriveAccessToken => File.Exists(OneDriveAccessTokenPath);
    public bool HasYouTubeAccessToken => File.Exists(YouTubeAccessTokenPath);
    public bool HasOneNoteAccessToken => File.Exists(OneNoteAccessTokenPath);
    public bool HasLinearCredential => File.Exists(LinearCredentialPath);
    public bool HasGitHubToken => File.Exists(GitHubTokenPath);
    public bool HasJiraApiToken => File.Exists(JiraApiTokenPath);
    public bool HasAzureDevOpsPat => File.Exists(AzureDevOpsPatPath);
    public bool HasWebDavPassword => File.Exists(WebDavPasswordPath);
    public bool HasFtpPassword => File.Exists(FtpPasswordPath);

    private string GeminiApiKeyPath => Path.Combine(_paths.SecretsRoot, "gemini-api-key.dpapi");
    private string S3AccessKeyIdPath => Path.Combine(_paths.SecretsRoot, "s3-access-key-id.dpapi");
    private string S3SecretAccessKeyPath => Path.Combine(_paths.SecretsRoot, "s3-secret-access-key.dpapi");
    private string ImgurClientIdPath => Path.Combine(_paths.SecretsRoot, "imgur-client-id.dpapi");
    private string CloudinaryApiKeyPath => Path.Combine(_paths.SecretsRoot, "cloudinary-api-key.dpapi");
    private string CloudinaryApiSecretPath => Path.Combine(_paths.SecretsRoot, "cloudinary-api-secret.dpapi");
    private string DropboxAccessTokenPath => Path.Combine(_paths.SecretsRoot, "dropbox-access-token.dpapi");
    private string GoogleDriveAccessTokenPath => Path.Combine(_paths.SecretsRoot, "google-drive-access-token.dpapi");
    private string GooglePhotosAccessTokenPath => Path.Combine(_paths.SecretsRoot, "google-photos-access-token.dpapi");
    private string OneDriveAccessTokenPath => Path.Combine(_paths.SecretsRoot, "onedrive-access-token.dpapi");
    private string YouTubeAccessTokenPath => Path.Combine(_paths.SecretsRoot, "youtube-access-token.dpapi");
    private string OneNoteAccessTokenPath => Path.Combine(_paths.SecretsRoot, "onenote-access-token.dpapi");
    private string LinearCredentialPath => Path.Combine(_paths.SecretsRoot, "linear-credential.dpapi");
    private string GitHubTokenPath => Path.Combine(_paths.SecretsRoot, "github-token.dpapi");
    private string JiraApiTokenPath => Path.Combine(_paths.SecretsRoot, "jira-api-token.dpapi");
    private string AzureDevOpsPatPath => Path.Combine(_paths.SecretsRoot, "azure-devops-pat.dpapi");
    private string WebDavPasswordPath => Path.Combine(_paths.SecretsRoot, "webdav-password.dpapi");
    private string FtpPasswordPath => Path.Combine(_paths.SecretsRoot, "ftp-password.dpapi");
    private string OAuthTokenPath(string providerName) => Path.Combine(_paths.SecretsRoot, $"oauth-{NormalizeSecretName(providerName)}.dpapi");

    public void SaveGeminiApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        SaveSecret(GeminiApiKeyPath, apiKey);
    }

    public string? ReadGeminiApiKey()
    {
        return ReadSecret(GeminiApiKeyPath);
    }

    public void ClearGeminiApiKey()
    {
        if (File.Exists(GeminiApiKeyPath))
        {
            File.Delete(GeminiApiKeyPath);
        }
    }

    public void SaveS3Credentials(string accessKeyId, string secretAccessKey)
    {
        if (string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(secretAccessKey))
        {
            return;
        }

        SaveSecret(S3AccessKeyIdPath, accessKeyId);
        SaveSecret(S3SecretAccessKeyPath, secretAccessKey);
    }

    public S3Credentials? ReadS3Credentials()
    {
        var accessKeyId = ReadSecret(S3AccessKeyIdPath);
        var secretAccessKey = ReadSecret(S3SecretAccessKeyPath);
        return string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(secretAccessKey)
            ? null
            : new S3Credentials(accessKeyId, secretAccessKey);
    }

    public void ClearS3Credentials()
    {
        DeleteSecret(S3AccessKeyIdPath);
        DeleteSecret(S3SecretAccessKeyPath);
    }

    public void SaveImgurClientId(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        SaveSecret(ImgurClientIdPath, clientId);
    }

    public string? ReadImgurClientId()
    {
        return ReadSecret(ImgurClientIdPath);
    }

    public void ClearImgurClientId()
    {
        DeleteSecret(ImgurClientIdPath);
    }

    public void SaveCloudinaryCredentials(string apiKey, string apiSecret)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
        {
            return;
        }

        SaveSecret(CloudinaryApiKeyPath, apiKey);
        SaveSecret(CloudinaryApiSecretPath, apiSecret);
    }

    public CloudinaryCredentials? ReadCloudinaryCredentials()
    {
        var apiKey = ReadSecret(CloudinaryApiKeyPath);
        var apiSecret = ReadSecret(CloudinaryApiSecretPath);
        return string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret)
            ? null
            : new CloudinaryCredentials(apiKey, apiSecret);
    }

    public void ClearCloudinaryCredentials()
    {
        DeleteSecret(CloudinaryApiKeyPath);
        DeleteSecret(CloudinaryApiSecretPath);
    }

    public void SaveDropboxAccessToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        SaveSecret(DropboxAccessTokenPath, accessToken);
    }

    public string? ReadDropboxAccessToken()
    {
        return ReadSecret(DropboxAccessTokenPath);
    }

    public void ClearDropboxAccessToken()
    {
        DeleteSecret(DropboxAccessTokenPath);
    }

    public void SaveGoogleDriveAccessToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        SaveSecret(GoogleDriveAccessTokenPath, accessToken);
    }

    public string? ReadGoogleDriveAccessToken()
    {
        return ReadSecret(GoogleDriveAccessTokenPath);
    }

    public void ClearGoogleDriveAccessToken()
    {
        DeleteSecret(GoogleDriveAccessTokenPath);
    }

    public void SaveGooglePhotosAccessToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        SaveSecret(GooglePhotosAccessTokenPath, accessToken);
    }

    public string? ReadGooglePhotosAccessToken()
    {
        return ReadSecret(GooglePhotosAccessTokenPath);
    }

    public void ClearGooglePhotosAccessToken()
    {
        DeleteSecret(GooglePhotosAccessTokenPath);
    }

    public void SaveOneDriveAccessToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        SaveSecret(OneDriveAccessTokenPath, accessToken);
    }

    public string? ReadOneDriveAccessToken()
    {
        return ReadSecret(OneDriveAccessTokenPath);
    }

    public void ClearOneDriveAccessToken()
    {
        DeleteSecret(OneDriveAccessTokenPath);
    }

    public void SaveYouTubeAccessToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        SaveSecret(YouTubeAccessTokenPath, accessToken);
    }

    public string? ReadYouTubeAccessToken()
    {
        return ReadSecret(YouTubeAccessTokenPath);
    }

    public void ClearYouTubeAccessToken()
    {
        DeleteSecret(YouTubeAccessTokenPath);
    }

    public void SaveOneNoteAccessToken(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        SaveSecret(OneNoteAccessTokenPath, accessToken);
    }

    public string? ReadOneNoteAccessToken()
    {
        return ReadSecret(OneNoteAccessTokenPath);
    }

    public void ClearOneNoteAccessToken()
    {
        DeleteSecret(OneNoteAccessTokenPath);
    }

    public void SaveLinearCredential(string credential)
    {
        if (string.IsNullOrWhiteSpace(credential))
        {
            return;
        }

        SaveSecret(LinearCredentialPath, credential);
    }

    public string? ReadLinearCredential()
    {
        return ReadSecret(LinearCredentialPath);
    }

    public void ClearLinearCredential()
    {
        DeleteSecret(LinearCredentialPath);
    }

    public void SaveGitHubToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        SaveSecret(GitHubTokenPath, token);
    }

    public string? ReadGitHubToken()
    {
        return ReadSecret(GitHubTokenPath);
    }

    public void ClearGitHubToken()
    {
        DeleteSecret(GitHubTokenPath);
    }

    public void SaveJiraApiToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        SaveSecret(JiraApiTokenPath, token);
    }

    public string? ReadJiraApiToken()
    {
        return ReadSecret(JiraApiTokenPath);
    }

    public void ClearJiraApiToken()
    {
        DeleteSecret(JiraApiTokenPath);
    }

    public void SaveAzureDevOpsPat(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        SaveSecret(AzureDevOpsPatPath, token);
    }

    public string? ReadAzureDevOpsPat()
    {
        return ReadSecret(AzureDevOpsPatPath);
    }

    public void ClearAzureDevOpsPat()
    {
        DeleteSecret(AzureDevOpsPatPath);
    }

    public void SaveWebDavPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        SaveSecret(WebDavPasswordPath, password);
    }

    public string? ReadWebDavPassword()
    {
        return ReadSecret(WebDavPasswordPath);
    }

    public void ClearWebDavPassword()
    {
        DeleteSecret(WebDavPasswordPath);
    }

    public void SaveFtpPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        SaveSecret(FtpPasswordPath, password);
    }

    public string? ReadFtpPassword()
    {
        return ReadSecret(FtpPasswordPath);
    }

    public void ClearFtpPassword()
    {
        DeleteSecret(FtpPasswordPath);
    }

    public bool HasOAuthToken(string providerName)
    {
        return File.Exists(OAuthTokenPath(providerName));
    }

    public void SaveOAuthToken(string providerName, OAuthTokenResult result)
    {
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.AccessToken))
        {
            return;
        }

        var existing = ReadOAuthToken(providerName);
        var token = new OAuthStoredToken
        {
            ProviderName = providerName,
            AccessToken = result.AccessToken,
            RefreshToken = string.IsNullOrWhiteSpace(result.RefreshToken)
                ? existing?.RefreshToken ?? string.Empty
                : result.RefreshToken,
            ExpiresAt = result.ExpiresAt,
            UpdatedAt = DateTimeOffset.Now
        };

        SaveSecret(OAuthTokenPath(providerName), JsonSerializer.Serialize(token, JsonOptions));
        SaveProviderAccessToken(providerName, token.AccessToken);
    }

    public OAuthStoredToken? ReadOAuthToken(string providerName)
    {
        var json = ReadSecret(OAuthTokenPath(providerName));
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OAuthStoredToken>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void ClearOAuthToken(string providerName)
    {
        DeleteSecret(OAuthTokenPath(providerName));
        ClearProviderAccessToken(providerName);
    }

    private void SaveProviderAccessToken(string providerName, string accessToken)
    {
        switch (providerName.Trim().ToLowerInvariant())
        {
            case "dropbox":
                SaveDropboxAccessToken(accessToken);
                break;
            case "google drive":
            case "googledrive":
            case "gdrive":
                SaveGoogleDriveAccessToken(accessToken);
                break;
            case "google photos":
            case "googlephotos":
            case "gphotos":
            case "photos":
                SaveGooglePhotosAccessToken(accessToken);
                break;
            case "onedrive":
            case "one drive":
            case "microsoft onedrive":
                SaveOneDriveAccessToken(accessToken);
                break;
            case "youtube":
            case "you tube":
                SaveYouTubeAccessToken(accessToken);
                break;
            case "onenote":
            case "one note":
            case "microsoft onenote":
                SaveOneNoteAccessToken(accessToken);
                break;
        }
    }

    private void ClearProviderAccessToken(string providerName)
    {
        switch (providerName.Trim().ToLowerInvariant())
        {
            case "dropbox":
                ClearDropboxAccessToken();
                break;
            case "google drive":
            case "googledrive":
            case "gdrive":
                ClearGoogleDriveAccessToken();
                break;
            case "google photos":
            case "googlephotos":
            case "gphotos":
            case "photos":
                ClearGooglePhotosAccessToken();
                break;
            case "onedrive":
            case "one drive":
            case "microsoft onedrive":
                ClearOneDriveAccessToken();
                break;
            case "youtube":
            case "you tube":
                ClearYouTubeAccessToken();
                break;
            case "onenote":
            case "one note":
            case "microsoft onenote":
                ClearOneNoteAccessToken();
                break;
        }
    }

    private static string NormalizeSecretName(string value)
    {
        var safe = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());

        return string.IsNullOrWhiteSpace(safe)
            ? "provider"
            : safe.Trim('-');
    }

    private void SaveSecret(string path, string value)
    {
        Directory.CreateDirectory(_paths.SecretsRoot);
        var clearBytes = Encoding.UTF8.GetBytes(value.Trim());
        var encrypted = ProtectedData.Protect(clearBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, encrypted);
    }

    private static string? ReadSecret(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var encrypted = File.ReadAllBytes(path);
        var clearBytes = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(clearBytes);
    }

    private static void DeleteSecret(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
