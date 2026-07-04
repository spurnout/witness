using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class ShareService
{
    private const long OneDriveUploadSessionThresholdBytes = 4L * 1024L * 1024L;
    private const int OneDriveUploadSessionChunkBytes = 10 * 320 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions CompactJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly AppPaths _paths;
    private readonly AppSettings _settings;
    private readonly SecretStore _secretStore;
    private readonly HttpClient _httpClient;
    private readonly object _historyGate = new();
    private readonly IReadOnlyDictionary<ShareDestination, IShareProvider> _providerAdapters;

    public ShareService(
        AppPaths paths,
        AppSettings settings,
        SecretStore secretStore,
        IEnumerable<IShareProvider>? providerAdapters = null)
        : this(paths, settings, secretStore, new HttpClient(), providerAdapters)
    {
    }

    internal ShareService(
        AppPaths paths,
        AppSettings settings,
        SecretStore secretStore,
        HttpClient httpClient,
        IEnumerable<IShareProvider>? providerAdapters = null)
    {
        _paths = paths;
        _settings = settings;
        _secretStore = secretStore;
        _httpClient = httpClient;
        var defaultAdapters = ShareProviderCatalog.CreateExecutable(paths, settings, secretStore);
        var suppliedAdapters = providerAdapters?.ToList();
        var adapters = suppliedAdapters is null
            ? defaultAdapters
            : suppliedAdapters.Concat(defaultAdapters.Where(defaultAdapter =>
                defaultAdapter.Destination is null ||
                suppliedAdapters.All(adapter => adapter.Destination != defaultAdapter.Destination)));
        _providerAdapters = adapters
            .Where(provider => provider.Destination.HasValue)
            .GroupBy(provider => provider.Destination!.Value)
            .ToDictionary(group => group.Key, group => group.First());
    }

    public async Task<ShareResult> ShareAsync(CaptureItem item, ShareDestination destination, CancellationToken cancellationToken = default)
    {
        ShareResult result;
        try
        {
            result = await ExecuteShareAsync(item, destination, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = new ShareResult
            {
                Succeeded = false,
                Message = $"Share failed: {ex.Message}"
            };
        }

        await AppendHistoryAsync(item, destination, result, cancellationToken);
        return result;
    }

    public IReadOnlyList<ShareHistoryEntry> LoadHistory(int limit = 50)
    {
        return SearchHistory(limit: limit);
    }

    public IReadOnlyList<ShareHistoryEntry> SearchHistory(
        string? query = null,
        ShareDestination? destination = null,
        bool? succeeded = null,
        bool? externalDestination = null,
        int limit = 50)
    {
        lock (_historyGate)
        {
            if (!File.Exists(_paths.ShareHistoryPath))
            {
                return Array.Empty<ShareHistoryEntry>();
            }

            try
            {
                var json = File.ReadAllText(_paths.ShareHistoryPath);
                var entries = JsonSerializer.Deserialize<List<ShareHistoryEntry>>(json, JsonOptions) ?? new List<ShareHistoryEntry>();
                var filtered = entries.AsEnumerable();
                if (destination is not null)
                {
                    filtered = filtered.Where(entry => entry.Destination == destination.Value);
                }

                if (succeeded is not null)
                {
                    filtered = filtered.Where(entry => entry.Succeeded == succeeded.Value);
                }

                if (externalDestination is not null)
                {
                    filtered = filtered.Where(entry => entry.ExternalDestination == externalDestination.Value);
                }

                var terms = SplitHistorySearchTerms(query);
                if (terms.Count > 0)
                {
                    filtered = filtered.Where(entry => HistoryEntryMatches(entry, terms));
                }

                return filtered
                    .OrderByDescending(entry => entry.CreatedAt)
                    .Take(Math.Clamp(limit, 1, 500))
                    .ToList();
            }
            catch
            {
                return Array.Empty<ShareHistoryEntry>();
            }
        }
    }

    private async Task<ShareResult> ExecuteShareAsync(CaptureItem item, ShareDestination destination, CancellationToken cancellationToken)
    {
        if (!ManagedPolicyService.IsShareDestinationAllowed(_settings, destination, out var policyReason))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = policyReason
            };
        }

        if (_providerAdapters.TryGetValue(destination, out var provider))
        {
            return await ExecuteProviderAdapterAsync(provider, item, cancellationToken);
        }

        return destination switch
        {
            ShareDestination.Dropbox => await UploadToDropboxAsync(item, cancellationToken),
            ShareDestination.GoogleDrive => await UploadToGoogleDriveAsync(item, cancellationToken),
            ShareDestination.OneDrive => await UploadToOneDriveAsync(item, cancellationToken),
            _ => new ShareResult { Succeeded = false, Message = $"Unsupported share destination: {destination}" }
        };
    }

    private static async Task<ShareResult> ExecuteProviderAdapterAsync(
        IShareProvider provider,
        CaptureItem item,
        CancellationToken cancellationToken)
    {
        var uploadResult = await provider.UploadAsync(BuildShareUploadRequest(item), cancellationToken);
        return new ShareResult
        {
            Succeeded = uploadResult.Succeeded,
            Message = uploadResult.Message,
            Url = uploadResult.ShareUrl
        };
    }

    private static ShareUploadRequest BuildShareUploadRequest(CaptureItem item)
    {
        return new ShareUploadRequest(
            item.FilePath,
            item.Kind.ToString(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = item.Id,
                ["file"] = item.FilePath,
                ["fileName"] = item.FileName,
                ["captureType"] = item.Kind.ToString(),
                ["createdAt"] = item.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                ["bytes"] = item.Bytes.ToString(CultureInfo.InvariantCulture),
                ["width"] = item.Width.ToString(CultureInfo.InvariantCulture),
                ["height"] = item.Height.ToString(CultureInfo.InvariantCulture),
                ["isPrivate"] = item.IsPrivate ? "true" : "false",
                ["sourceApp"] = item.SourceApp ?? string.Empty,
                ["sourceWindowTitle"] = item.SourceWindowTitle ?? string.Empty,
                ["bounds"] = item.Bounds?.Display ?? string.Empty,
                ["notes"] = item.Notes ?? string.Empty,
                ["ocrText"] = item.OcrText ?? string.Empty
            });
    }

    private async Task AppendHistoryAsync(
        CaptureItem item,
        ShareDestination destination,
        ShareResult result,
        CancellationToken cancellationToken)
    {
        var entry = new ShareHistoryEntry
        {
            CaptureItemId = item.Id,
            FileName = item.FileName,
            FilePath = item.FilePath,
            Bytes = item.Bytes,
            Destination = destination,
            ExternalDestination = IsExternalDestination(destination),
            Succeeded = result.Succeeded,
            Message = RedactHistoryText(result.Message),
            Url = string.IsNullOrWhiteSpace(result.Url) ? null : RedactHistoryText(result.Url)
        };

        await Task.Run(() =>
        {
            lock (_historyGate)
            {
                List<ShareHistoryEntry> entries;
                try
                {
                    entries = File.Exists(_paths.ShareHistoryPath)
                        ? JsonSerializer.Deserialize<List<ShareHistoryEntry>>(File.ReadAllText(_paths.ShareHistoryPath), JsonOptions) ?? new List<ShareHistoryEntry>()
                        : new List<ShareHistoryEntry>();
                }
                catch
                {
                    entries = new List<ShareHistoryEntry>();
                }

                entries.Insert(0, entry);
                entries = entries
                    .OrderByDescending(existing => existing.CreatedAt)
                    .Take(500)
                    .ToList();

                Directory.CreateDirectory(Path.GetDirectoryName(_paths.ShareHistoryPath)!);
                File.WriteAllText(_paths.ShareHistoryPath, JsonSerializer.Serialize(entries, JsonOptions));
            }
        }, cancellationToken);
    }

    private static IReadOnlyList<string> SplitHistorySearchTerms(string? query)
    {
        return string.IsNullOrWhiteSpace(query)
            ? Array.Empty<string>()
            : query
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(term => term.Length > 0)
                .ToList();
    }

    private static bool HistoryEntryMatches(ShareHistoryEntry entry, IReadOnlyList<string> terms)
    {
        var haystack = string.Join(
            '\n',
            entry.Id,
            entry.CaptureItemId,
            entry.FileName,
            entry.FilePath,
            entry.Destination,
            entry.ExternalDestination ? "external" : "local",
            entry.Succeeded ? "succeeded success ok" : "failed failure error",
            entry.Message,
            entry.Url ?? string.Empty);

        return terms.All(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public ShareDestination ResolveDefaultDestination()
    {
        return ParseDestination(_settings.DefaultShareDestination);
    }

    public static ShareDestination ParseDestination(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "clipboard image" => ShareDestination.ClipboardImage,
            "clipboard file" => ShareDestination.ClipboardFile,
            "clipboard path" => ShareDestination.ClipboardPath,
            "markdown image link" => ShareDestination.MarkdownImageLink,
            "local folder" => ShareDestination.LocalFolder,
            "email attachment" => ShareDestination.EmailAttachment,
            "s3" => ShareDestination.S3Compatible,
            "s3-compatible" => ShareDestination.S3Compatible,
            "s3 compatible" => ShareDestination.S3Compatible,
            "amazon s3" => ShareDestination.S3Compatible,
            "imgur" => ShareDestination.Imgur,
            "imgur upload" => ShareDestination.Imgur,
            "sftp" => ShareDestination.Sftp,
            "sftp upload" => ShareDestination.Sftp,
            "cloudinary" => ShareDestination.Cloudinary,
            "cloudinary upload" => ShareDestination.Cloudinary,
            "dropbox" => ShareDestination.Dropbox,
            "dropbox upload" => ShareDestination.Dropbox,
            "google drive" => ShareDestination.GoogleDrive,
            "google-drive" => ShareDestination.GoogleDrive,
            "googledrive" => ShareDestination.GoogleDrive,
            "gdrive" => ShareDestination.GoogleDrive,
            "drive" => ShareDestination.GoogleDrive,
            "google photos" => ShareDestination.GooglePhotos,
            "google-photos" => ShareDestination.GooglePhotos,
            "googlephotos" => ShareDestination.GooglePhotos,
            "gphotos" => ShareDestination.GooglePhotos,
            "photos" => ShareDestination.GooglePhotos,
            "onedrive" => ShareDestination.OneDrive,
            "one drive" => ShareDestination.OneDrive,
            "microsoft onedrive" => ShareDestination.OneDrive,
            "onedrive upload" => ShareDestination.OneDrive,
            "youtube" => ShareDestination.YouTube,
            "you tube" => ShareDestination.YouTube,
            "youtube upload" => ShareDestination.YouTube,
            "youtube video" => ShareDestination.YouTube,
            "onenote" => ShareDestination.OneNote,
            "one note" => ShareDestination.OneNote,
            "microsoft onenote" => ShareDestination.OneNote,
            "onenote export" => ShareDestination.OneNote,
            "linear" => ShareDestination.Linear,
            "linear upload" => ShareDestination.Linear,
            "linear issue" => ShareDestination.Linear,
            "linear attachment" => ShareDestination.Linear,
            "github" => ShareDestination.GitHubIssues,
            "github issue" => ShareDestination.GitHubIssues,
            "github issues" => ShareDestination.GitHubIssues,
            "jira" => ShareDestination.Jira,
            "jira issue" => ShareDestination.Jira,
            "azure devops" => ShareDestination.AzureDevOps,
            "azure-devops" => ShareDestination.AzureDevOps,
            "ado" => ShareDestination.AzureDevOps,
            "devops" => ShareDestination.AzureDevOps,
            "slack" => ShareDestination.SlackWebhook,
            "slack webhook" => ShareDestination.SlackWebhook,
            "discord" => ShareDestination.DiscordWebhook,
            "discord webhook" => ShareDestination.DiscordWebhook,
            "teams" => ShareDestination.MicrosoftTeamsWebhook,
            "microsoft teams" => ShareDestination.MicrosoftTeamsWebhook,
            "teams webhook" => ShareDestination.MicrosoftTeamsWebhook,
            "microsoft teams webhook" => ShareDestination.MicrosoftTeamsWebhook,
            "webdav" => ShareDestination.WebDav,
            "web dav" => ShareDestination.WebDav,
            "ftp" => ShareDestination.FtpFtps,
            "ftps" => ShareDestination.FtpFtps,
            "ftp/ftps" => ShareDestination.FtpFtps,
            "custom script" => ShareDestination.CustomScript,
            "custom webhook" => ShareDestination.CustomWebhook,
            _ => ShareDestination.ClipboardImage
        };
    }

    public static bool IsExternalDestination(ShareDestination destination)
    {
        return destination is ShareDestination.EmailAttachment or ShareDestination.S3Compatible or ShareDestination.Imgur or ShareDestination.Sftp or ShareDestination.Cloudinary or ShareDestination.Dropbox or ShareDestination.GoogleDrive or ShareDestination.GooglePhotos or ShareDestination.OneDrive or ShareDestination.YouTube or ShareDestination.OneNote or ShareDestination.Linear or ShareDestination.GitHubIssues or ShareDestination.Jira or ShareDestination.AzureDevOps or ShareDestination.SlackWebhook or ShareDestination.DiscordWebhook or ShareDestination.MicrosoftTeamsWebhook or ShareDestination.WebDav or ShareDestination.FtpFtps or ShareDestination.CustomScript or ShareDestination.CustomWebhook;
    }

    private async Task<ShareResult> UploadToDropboxAsync(CaptureItem item, CancellationToken cancellationToken)
    {
        var accessToken = _secretStore.ReadDropboxAccessToken();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "Dropbox upload needs a DPAPI-saved OAuth access token."
            };
        }

        var dropboxPath = BuildDropboxPath(item);
        var uploadUri = BuildDropboxApiUri(_settings.DropboxContentApiBaseUrl, "2/files/upload");
        var uploadArg = new
        {
            path = dropboxPath,
            mode = "add",
            autorename = true,
            mute = false,
            strict_conflict = false
        };

        await using var fileStream = File.OpenRead(item.FilePath);
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, uploadUri);
        uploadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
        uploadRequest.Headers.TryAddWithoutValidation("Dropbox-API-Arg", JsonSerializer.Serialize(uploadArg, CompactJsonOptions));
        uploadRequest.Content = new StreamContent(fileStream);
        uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var uploadResponse = await _httpClient.SendAsync(uploadRequest, cancellationToken);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = $"Dropbox upload failed: {(int)uploadResponse.StatusCode} {uploadResponse.ReasonPhrase} {uploadBody}"
            };
        }

        var uploadedPath = ExtractDropboxPath(uploadBody) ?? dropboxPath;
        var linkResult = await GetDropboxTemporaryLinkAsync(uploadedPath, accessToken.Trim(), cancellationToken);
        if (!string.IsNullOrWhiteSpace(linkResult.Url))
        {
            ClipboardInterop.SetText(linkResult.Url);
        }

        return new ShareResult
        {
            Succeeded = true,
            Url = linkResult.Url,
            Message = string.IsNullOrWhiteSpace(linkResult.Url)
                ? $"Dropbox upload completed: {uploadedPath}. Temporary link was not returned: {linkResult.Message}"
                : $"Dropbox upload completed and copied temporary URL: {linkResult.Url}"
        };
    }

    private async Task<(string? Url, string Message)> GetDropboxTemporaryLinkAsync(
        string dropboxPath,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var linkUri = BuildDropboxApiUri(_settings.DropboxApiBaseUrl, "2/files/get_temporary_link");
        using var request = new HttpRequestMessage(HttpMethod.Post, linkUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { path = dropboxPath }, CompactJsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (null, $"{(int)response.StatusCode} {response.ReasonPhrase} {body}");
        }

        return (ExtractDropboxTemporaryLink(body), "Temporary link returned.");
    }

    private async Task<ShareResult> UploadToGoogleDriveAsync(CaptureItem item, CancellationToken cancellationToken)
    {
        var accessToken = _secretStore.ReadGoogleDriveAccessToken();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "Google Drive upload needs a DPAPI-saved OAuth access token."
            };
        }

        var uploadUri = BuildGoogleDriveUploadUri();
        var metadata = new Dictionary<string, object>
        {
            ["name"] = item.FileName
        };
        if (!string.IsNullOrWhiteSpace(_settings.GoogleDriveFolderId))
        {
            metadata["parents"] = new[] { _settings.GoogleDriveFolderId.Trim() };
        }

        await using var fileStream = File.OpenRead(item.FilePath);
        using var metadataContent = new StringContent(
            JsonSerializer.Serialize(metadata, CompactJsonOptions),
            Encoding.UTF8,
            "application/json");
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(DetectContentType(item.FilePath));
        using var content = new MultipartContent("related");
        content.Add(metadataContent);
        content.Add(fileContent);

        using var request = new HttpRequestMessage(HttpMethod.Post, uploadUri)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());

        using var uploadResponse = await _httpClient.SendAsync(request, cancellationToken);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!uploadResponse.IsSuccessStatusCode)
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = $"Google Drive upload failed: {(int)uploadResponse.StatusCode} {uploadResponse.ReasonPhrase} {uploadBody}"
            };
        }

        var fileId = ExtractGoogleDriveFileId(uploadBody);
        var shareUrl = ExtractGoogleDriveShareUrl(uploadBody);
        var permissionMessage = string.Empty;
        if (_settings.GoogleDriveCreateAnyoneReaderLink)
        {
            if (string.IsNullOrWhiteSpace(fileId))
            {
                return new ShareResult
                {
                    Succeeded = false,
                    Url = shareUrl,
                    Message = $"Google Drive upload completed, but the response did not include a file ID for permission creation. Response: {uploadBody}"
                };
            }

            var permissionResult = await CreateGoogleDriveAnyoneReaderPermissionAsync(fileId, accessToken.Trim(), cancellationToken);
            if (!permissionResult.Succeeded)
            {
                return new ShareResult
                {
                    Succeeded = false,
                    Url = shareUrl,
                    Message = $"Google Drive upload completed, but creating an anyone-reader link failed: {permissionResult.Message}"
                };
            }

            permissionMessage = " Anyone-reader link permission was created.";
        }

        if (!string.IsNullOrWhiteSpace(shareUrl))
        {
            ClipboardInterop.SetText(shareUrl);
        }

        return new ShareResult
        {
            Succeeded = true,
            Url = shareUrl,
            Message = string.IsNullOrWhiteSpace(shareUrl)
                ? $"Google Drive upload completed: {fileId ?? item.FileName}.{permissionMessage}"
                : $"Google Drive upload completed and copied URL: {shareUrl}.{permissionMessage}"
        };
    }

    private async Task<(bool Succeeded, string Message)> CreateGoogleDriveAnyoneReaderPermissionAsync(
        string fileId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var permissionUri = BuildGoogleDriveApiUri(
            _settings.GoogleDriveApiBaseUrl,
            $"files/{Uri.EscapeDataString(fileId)}/permissions",
            new Dictionary<string, string>
            {
                ["fields"] = "id",
                ["supportsAllDrives"] = "true"
            });
        using var request = new HttpRequestMessage(HttpMethod.Post, permissionUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { type = "anyone", role = "reader" }, CompactJsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return response.IsSuccessStatusCode
            ? (true, "Permission created.")
            : (false, $"{(int)response.StatusCode} {response.ReasonPhrase} {body}");
    }

    private async Task<ShareResult> UploadToOneDriveAsync(CaptureItem item, CancellationToken cancellationToken)
    {
        var accessToken = _secretStore.ReadOneDriveAccessToken();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "OneDrive upload needs a DPAPI-saved Microsoft Graph OAuth access token."
            };
        }

        var upload = await UploadOneDriveFileAsync(item, accessToken.Trim(), cancellationToken);
        if (!upload.Succeeded)
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = upload.Message
            };
        }

        var itemId = ExtractOneDriveItemId(upload.ResponseBody);
        var shareUrl = ExtractOneDriveWebUrl(upload.ResponseBody);
        var linkMessage = string.Empty;
        if (_settings.OneDriveCreateAnonymousViewLink)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return new ShareResult
                {
                    Succeeded = false,
                    Url = shareUrl,
                    Message = $"OneDrive upload completed, but the response did not include an item ID for share-link creation. Response: {upload.ResponseBody}"
                };
            }

            var linkResult = await CreateOneDriveAnonymousViewLinkAsync(itemId, accessToken.Trim(), cancellationToken);
            if (!linkResult.Succeeded)
            {
                return new ShareResult
                {
                    Succeeded = false,
                    Url = shareUrl,
                    Message = $"OneDrive upload completed, but creating an anonymous view link failed: {linkResult.Message}"
                };
            }

            shareUrl = linkResult.Url ?? shareUrl;
            linkMessage = " Anonymous view link was created.";
        }

        if (!string.IsNullOrWhiteSpace(shareUrl))
        {
            ClipboardInterop.SetText(shareUrl);
        }

        return new ShareResult
        {
            Succeeded = true,
            Url = shareUrl,
            Message = string.IsNullOrWhiteSpace(shareUrl)
                ? $"OneDrive {upload.Mode} upload completed: {itemId ?? item.FileName}.{linkMessage}"
                : $"OneDrive {upload.Mode} upload completed and copied URL: {shareUrl}.{linkMessage}"
        };
    }

    private async Task<(bool Succeeded, string ResponseBody, string Message, string Mode)> UploadOneDriveFileAsync(
        CaptureItem item,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var length = new FileInfo(item.FilePath).Length;
        return length >= OneDriveUploadSessionThresholdBytes
            ? await UploadOneDriveFileWithSessionAsync(item, accessToken, length, cancellationToken)
            : await UploadOneDriveSmallFileAsync(item, accessToken, cancellationToken);
    }

    private async Task<(bool Succeeded, string ResponseBody, string Message, string Mode)> UploadOneDriveSmallFileAsync(
        CaptureItem item,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var uploadUri = BuildOneDriveUploadUri(item);
        await using var fileStream = File.OpenRead(item.FilePath);
        using var request = new HttpRequestMessage(HttpMethod.Put, uploadUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StreamContent(fileStream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(DetectContentType(item.FilePath));

        using var uploadResponse = await _httpClient.SendAsync(request, cancellationToken);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
        return uploadResponse.IsSuccessStatusCode
            ? (true, uploadBody, "OneDrive small-file upload completed.", "small-file")
            : (false, uploadBody, $"OneDrive upload failed: {(int)uploadResponse.StatusCode} {uploadResponse.ReasonPhrase} {uploadBody}", "small-file");
    }

    private async Task<(bool Succeeded, string ResponseBody, string Message, string Mode)> UploadOneDriveFileWithSessionAsync(
        CaptureItem item,
        string accessToken,
        long length,
        CancellationToken cancellationToken)
    {
        using var createRequest = new HttpRequestMessage(HttpMethod.Post, BuildOneDriveUploadSessionUri(item));
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        createRequest.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                item = new Dictionary<string, string>
                {
                    ["@microsoft.graph.conflictBehavior"] = "rename",
                    ["name"] = BuildRemoteFileName(item)
                }
            }, CompactJsonOptions),
            Encoding.UTF8,
            "application/json");

        using var createResponse = await _httpClient.SendAsync(createRequest, cancellationToken);
        var createBody = await createResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!createResponse.IsSuccessStatusCode)
        {
            return (false, createBody, $"OneDrive upload session creation failed: {(int)createResponse.StatusCode} {createResponse.ReasonPhrase} {createBody}", "upload-session");
        }

        var uploadUrl = ExtractOneDriveUploadUrl(createBody);
        if (string.IsNullOrWhiteSpace(uploadUrl))
        {
            return (false, createBody, $"OneDrive upload session creation did not return an uploadUrl. Response: {createBody}", "upload-session");
        }

        await using var fileStream = File.OpenRead(item.FilePath);
        var buffer = new byte[OneDriveUploadSessionChunkBytes];
        long offset = 0;
        string lastBody = string.Empty;
        while (offset < length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = length - offset;
            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read = await ReadExactlyOrEndAsync(fileStream, buffer, toRead, cancellationToken);
            if (read <= 0)
            {
                break;
            }

            var start = offset;
            var end = offset + read - 1;
            using var chunk = new ByteArrayContent(buffer, 0, read);
            chunk.Headers.ContentLength = read;
            chunk.Headers.ContentRange = new ContentRangeHeaderValue(start, end, length);
            chunk.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var chunkResponse = await _httpClient.PutAsync(uploadUrl, chunk, cancellationToken);
            lastBody = await chunkResponse.Content.ReadAsStringAsync(cancellationToken);
            if (chunkResponse.StatusCode == HttpStatusCode.Accepted)
            {
                offset += read;
                continue;
            }

            if (chunkResponse.IsSuccessStatusCode)
            {
                return (true, lastBody, $"OneDrive upload session completed in {Math.Ceiling((double)(end + 1) / OneDriveUploadSessionChunkBytes):0} chunk(s).", "upload-session");
            }

            return (false, lastBody, $"OneDrive upload session chunk failed at bytes {start}-{end}: {(int)chunkResponse.StatusCode} {chunkResponse.ReasonPhrase} {lastBody}", "upload-session");
        }

        return (false, lastBody, "OneDrive upload session ended before Microsoft Graph returned a completed drive item.", "upload-session");
    }

    private async Task<(bool Succeeded, string? Url, string Message)> CreateOneDriveAnonymousViewLinkAsync(
        string itemId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var linkUri = BuildOneDriveGraphUri($"me/drive/items/{Uri.EscapeDataString(itemId)}/createLink");
        using var request = new HttpRequestMessage(HttpMethod.Post, linkUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { type = "view", scope = "anonymous" }, CompactJsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (false, null, $"{(int)response.StatusCode} {response.ReasonPhrase} {body}");
        }

        return (true, ExtractOneDriveSharingLink(body), "Sharing link returned.");
    }

    private Uri BuildDropboxApiUri(string? baseUrl, string route)
    {
        var resolved = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.dropboxapi.com"
            : baseUrl.Trim();
        if (!resolved.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !resolved.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            resolved = $"https://{resolved}";
        }

        var builder = new UriBuilder(resolved);
        var basePath = builder.Path.Trim('/');
        builder.Path = string.IsNullOrWhiteSpace(basePath)
            ? route.Trim('/')
            : $"{basePath}/{route.Trim('/')}";
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private Uri BuildGoogleDriveUploadUri()
    {
        return BuildGoogleDriveApiUri(
            _settings.GoogleDriveUploadApiBaseUrl,
            "files",
            new Dictionary<string, string>
            {
                ["uploadType"] = "multipart",
                ["fields"] = "id,name,webViewLink,webContentLink",
                ["supportsAllDrives"] = "true"
            });
    }

    private static Uri BuildGoogleDriveApiUri(
        string? baseUrl,
        string route,
        IReadOnlyDictionary<string, string>? query = null)
    {
        var resolved = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://www.googleapis.com/drive/v3"
            : baseUrl.Trim();
        if (!resolved.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !resolved.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            resolved = $"https://{resolved}";
        }

        var builder = new UriBuilder(resolved);
        var basePath = builder.Path.Trim('/');
        builder.Path = string.IsNullOrWhiteSpace(basePath)
            ? route.Trim('/')
            : $"{basePath}/{route.Trim('/')}";
        builder.Query = query is null || query.Count == 0
            ? string.Empty
            : string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return builder.Uri;
    }

    private Uri BuildOneDriveUploadUri(CaptureItem item)
    {
        var fileName = BuildRemoteFileName(item);
        var folder = NormalizeCloudFolderPath(_settings.OneDriveRemoteFolder);
        var path = string.IsNullOrWhiteSpace(folder)
            ? EncodeCloudPathSegment(fileName)
            : $"{folder}/{EncodeCloudPathSegment(fileName)}";
        return BuildOneDriveGraphUri($"me/drive/root:/{path}:/content");
    }

    private Uri BuildOneDriveUploadSessionUri(CaptureItem item)
    {
        var fileName = BuildRemoteFileName(item);
        var folder = NormalizeCloudFolderPath(_settings.OneDriveRemoteFolder);
        var path = string.IsNullOrWhiteSpace(folder)
            ? EncodeCloudPathSegment(fileName)
            : $"{folder}/{EncodeCloudPathSegment(fileName)}";
        return BuildOneDriveGraphUri($"me/drive/root:/{path}:/createUploadSession");
    }

    private Uri BuildOneDriveGraphUri(string route)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.OneDriveGraphApiBaseUrl)
            ? "https://graph.microsoft.com/v1.0"
            : _settings.OneDriveGraphApiBaseUrl.Trim();
        if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = $"https://{baseUrl}";
        }

        var builder = new UriBuilder(baseUrl);
        var basePath = builder.Path.Trim('/');
        builder.Path = string.IsNullOrWhiteSpace(basePath)
            ? route.Trim('/')
            : $"{basePath}/{route.Trim('/')}";
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private string BuildDropboxPath(CaptureItem item)
    {
        var folder = string.IsNullOrWhiteSpace(_settings.DropboxRemoteFolder)
            ? "/GoatShot"
            : _settings.DropboxRemoteFolder.Replace('\\', '/').Trim();
        if (folder.Length == 0 || folder == ".")
        {
            folder = "/";
        }

        if (!folder.StartsWith('/'))
        {
            folder = $"/{folder}";
        }

        folder = folder.TrimEnd('/');
        var fileName = string.Join("_", item.FileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        var remoteFileName = $"{item.Id}-{fileName}";
        return folder == "/"
            ? $"/{remoteFileName}"
            : $"{folder}/{remoteFileName}";
    }

    private static string? ExtractDropboxPath(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            foreach (var property in new[] { "path_display", "path_lower" })
            {
                if (root.TryGetProperty(property, out var path) &&
                    path.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(path.GetString()))
                {
                    return path.GetString();
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string? ExtractDropboxTemporaryLink(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("link", out var link) &&
                link.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(link.GetString()))
            {
                return link.GetString();
            }
        }
        catch (JsonException)
        {
            return FirstUrl(responseBody);
        }

        return FirstUrl(responseBody);
    }

    private static string? ExtractGoogleDriveFileId(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(id.GetString()))
            {
                return id.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string? ExtractGoogleDriveShareUrl(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            foreach (var property in new[] { "webViewLink", "webContentLink" })
            {
                if (root.TryGetProperty(property, out var link) &&
                    link.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(link.GetString()))
                {
                    return link.GetString();
                }
            }

            if (root.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(id.GetString()))
            {
                return $"https://drive.google.com/file/d/{Uri.EscapeDataString(id.GetString()!)}/view";
            }
        }
        catch (JsonException)
        {
            return FirstUrl(responseBody);
        }

        return FirstUrl(responseBody);
    }

    private static string? ExtractOneDriveItemId(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(id.GetString()))
            {
                return id.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string? ExtractOneDriveWebUrl(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("webUrl", out var webUrl) &&
                webUrl.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(webUrl.GetString()))
            {
                return webUrl.GetString();
            }
        }
        catch (JsonException)
        {
            return FirstUrl(responseBody);
        }

        return FirstUrl(responseBody);
    }

    private static string? ExtractOneDriveSharingLink(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.TryGetProperty("link", out var link) &&
                link.ValueKind == JsonValueKind.Object &&
                link.TryGetProperty("webUrl", out var webUrl) &&
                webUrl.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(webUrl.GetString()))
            {
                return webUrl.GetString();
            }

            if (root.TryGetProperty("webUrl", out var rootWebUrl) &&
                rootWebUrl.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(rootWebUrl.GetString()))
            {
                return rootWebUrl.GetString();
            }
        }
        catch (JsonException)
        {
            return FirstUrl(responseBody);
        }

        return FirstUrl(responseBody);
    }

    private static string? ExtractOneDriveUploadUrl(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("uploadUrl", out var uploadUrl) &&
                uploadUrl.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(uploadUrl.GetString()))
            {
                return uploadUrl.GetString();
            }
        }
        catch (JsonException)
        {
            return FirstUrl(responseBody);
        }

        return FirstUrl(responseBody);
    }

    private static async Task<int> ReadExactlyOrEndAsync(
        Stream stream,
        byte[] buffer,
        int count,
        CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, count - total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static string DetectContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            ".avi" => "video/x-msvideo",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".json" => "application/json",
            _ => "application/octet-stream"
        };
    }

    private static string NormalizeCloudFolderPath(string? value)
    {
        var folder = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace('\\', '/').Trim().Trim('/');
        return string.Join(
            "/",
            folder
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(EncodeCloudPathSegment));
    }

    private static string EncodeCloudPathSegment(string value)
    {
        return Uri.EscapeDataString(value)
            .Replace("%2D", "-", StringComparison.OrdinalIgnoreCase)
            .Replace("%2E", ".", StringComparison.OrdinalIgnoreCase)
            .Replace("%5F", "_", StringComparison.OrdinalIgnoreCase)
            .Replace("%7E", "~", StringComparison.OrdinalIgnoreCase);
    }

    internal static string? FindSftpExecutable(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configuredPath.Trim());
            return File.Exists(expanded) ? expanded : null;
        }

        var environmentPath = Environment.GetEnvironmentVariable("GOATSHOT_SFTP_PATH");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(environmentPath.Trim());
            return File.Exists(expanded) ? expanded : null;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim(), "sftp.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        var systemCandidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "OpenSSH",
            "sftp.exe");
        return File.Exists(systemCandidate) ? systemCandidate : null;
    }

    private static string BuildRemoteFileName(CaptureItem item)
    {
        var fileName = string.Join("_", item.FileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return $"{item.Id}-{fileName}";
    }

    private static string? FirstUrl(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"https?://[^\s""'<>]+");
        return match.Success ? match.Value : null;
    }

    public static string RedactHistoryText(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : WorkflowTextRedactor.Redact(text);
    }
}
