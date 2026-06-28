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

    private async Task<ShareResult> CreateGitHubIssueAsync(CaptureItem item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.GitHubRepository))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "GitHub issue creation needs a repository setting in owner/repo form."
            };
        }

        var token = _secretStore.ReadGitHubToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "GitHub issue creation needs a DPAPI-saved personal access token."
            };
        }

        var repository = ParseGitHubRepository(_settings.GitHubRepository);
        if (repository is null)
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "GitHub repository must be configured as owner/repo."
            };
        }

        var requestBody = new Dictionary<string, object?>
        {
            ["title"] = ExpandProviderMessage(_settings.GitHubIssueTitleTemplate, item, "GitHub Issues"),
            ["body"] = BuildIssueMarkdownBody(item, "GitHub Issues")
        };
        var labels = SplitProviderList(_settings.GitHubLabels);
        if (labels.Count > 0)
        {
            requestBody["labels"] = labels;
        }

        var assignees = SplitProviderList(_settings.GitHubAssignees);
        if (assignees.Count > 0)
        {
            requestBody["assignees"] = assignees;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildGitHubIssueUri(repository.Value.Owner, repository.Value.Repo))
        {
            Content = JsonContent(requestBody)
        };
        request.Headers.UserAgent.ParseAdd("GoatShot/0.1");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var issueUrl = response.IsSuccessStatusCode ? ExtractJsonString(body, "html_url") ?? FirstUrl(body) : null;
        if (!string.IsNullOrWhiteSpace(issueUrl))
        {
            ClipboardInterop.SetText(issueUrl);
        }

        return new ShareResult
        {
            Succeeded = response.IsSuccessStatusCode,
            Url = issueUrl,
            Message = response.IsSuccessStatusCode
                ? string.IsNullOrWhiteSpace(issueUrl)
                    ? $"GitHub issue created: {(int)response.StatusCode} {response.ReasonPhrase}"
                    : $"GitHub issue created and copied URL: {issueUrl}"
                : $"GitHub issue creation failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}"
        };
    }

    private async Task<ShareResult> CreateJiraIssueAsync(CaptureItem item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.JiraBaseUrl) ||
            string.IsNullOrWhiteSpace(_settings.JiraProjectKey))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "Jira issue creation needs site base URL and project key settings."
            };
        }

        if (string.IsNullOrWhiteSpace(_settings.JiraAccountEmail))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "Jira issue creation needs an account email setting."
            };
        }

        var apiToken = _secretStore.ReadJiraApiToken();
        if (string.IsNullOrWhiteSpace(apiToken))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "Jira issue creation needs a DPAPI-saved API token."
            };
        }

        var fields = new Dictionary<string, object?>
        {
            ["project"] = new { key = _settings.JiraProjectKey.Trim() },
            ["summary"] = ExpandProviderMessage(_settings.JiraSummaryTemplate, item, "Jira"),
            ["issuetype"] = new { name = string.IsNullOrWhiteSpace(_settings.JiraIssueType) ? "Bug" : _settings.JiraIssueType.Trim() },
            ["description"] = BuildJiraAdfDescription(item)
        };
        var labels = SplitProviderList(_settings.JiraLabels);
        if (labels.Count > 0)
        {
            fields["labels"] = labels;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildJiraIssueUri())
        {
            Content = JsonContent(new { fields })
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.JiraAccountEmail.Trim()}:{apiToken.Trim()}")));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var issueKey = response.IsSuccessStatusCode ? ExtractJsonString(body, "key") : null;
        var issueUrl = response.IsSuccessStatusCode ? BuildJiraBrowseUrl(issueKey) ?? ExtractJsonString(body, "self") ?? FirstUrl(body) : null;
        if (!string.IsNullOrWhiteSpace(issueUrl))
        {
            ClipboardInterop.SetText(issueUrl);
        }

        return new ShareResult
        {
            Succeeded = response.IsSuccessStatusCode,
            Url = issueUrl,
            Message = response.IsSuccessStatusCode
                ? string.IsNullOrWhiteSpace(issueUrl)
                    ? $"Jira issue created: {(int)response.StatusCode} {response.ReasonPhrase}"
                    : $"Jira issue created and copied URL: {issueUrl}"
                : $"Jira issue creation failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}"
        };
    }

    private async Task<ShareResult> CreateAzureDevOpsWorkItemAsync(CaptureItem item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.AzureDevOpsOrganization) ||
            string.IsNullOrWhiteSpace(_settings.AzureDevOpsProject))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "Azure DevOps work item creation needs organization and project settings."
            };
        }

        var pat = _secretStore.ReadAzureDevOpsPat();
        if (string.IsNullOrWhiteSpace(pat))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "Azure DevOps work item creation needs a DPAPI-saved personal access token."
            };
        }

        var patch = new List<Dictionary<string, object?>>
        {
            JsonPatchAdd("/fields/System.Title", ExpandProviderMessage(_settings.AzureDevOpsTitleTemplate, item, "Azure DevOps")),
            JsonPatchAdd("/fields/System.Description", BuildAzureDevOpsDescriptionHtml(item))
        };
        var tags = BuildAzureDevOpsTags(_settings.AzureDevOpsTags);
        if (!string.IsNullOrWhiteSpace(tags))
        {
            patch.Add(JsonPatchAdd("/fields/System.Tags", tags));
        }

        if (!string.IsNullOrWhiteSpace(_settings.AzureDevOpsAssignedTo))
        {
            patch.Add(JsonPatchAdd("/fields/System.AssignedTo", _settings.AzureDevOpsAssignedTo.Trim()));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildAzureDevOpsCreateWorkItemUri())
        {
            Content = JsonContent(patch)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json-patch+json");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($":{pat.Trim()}")));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var id = response.IsSuccessStatusCode ? ExtractJsonInt(body, "id") : null;
        var workItemUrl = response.IsSuccessStatusCode
            ? ExtractAzureDevOpsWorkItemUrl(body) ?? BuildAzureDevOpsBrowseUrl(id)
            : null;
        if (!string.IsNullOrWhiteSpace(workItemUrl))
        {
            ClipboardInterop.SetText(workItemUrl);
        }

        return new ShareResult
        {
            Succeeded = response.IsSuccessStatusCode,
            Url = workItemUrl,
            Message = response.IsSuccessStatusCode
                ? string.IsNullOrWhiteSpace(workItemUrl)
                    ? $"Azure DevOps work item created: {(int)response.StatusCode} {response.ReasonPhrase}"
                    : $"Azure DevOps work item created and copied URL: {workItemUrl}"
                : $"Azure DevOps work item creation failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}"
        };
    }

    private async Task<ShareResult> PostToSlackWebhookAsync(CaptureItem item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.SlackWebhookUrl))
        {
            return new ShareResult { Succeeded = false, Message = "Slack webhook share needs a configured incoming webhook URL." };
        }

        var text = ExpandProviderMessage(_settings.SlackMessageTemplate, item, "Slack");
        using var content = JsonContent(new
        {
            text,
            unfurl_links = false,
            unfurl_media = false
        });
        using var response = await _httpClient.PostAsync(_settings.SlackWebhookUrl.Trim(), content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return new ShareResult
        {
            Succeeded = response.IsSuccessStatusCode,
            Message = response.IsSuccessStatusCode
                ? "Slack webhook notification sent. Incoming webhooks do not upload local files; use Discord, WebDAV, FTP/FTPS, or a file provider for media transfer."
                : $"Slack webhook notification failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}"
        };
    }

    private async Task<ShareResult> UploadToDiscordWebhookAsync(CaptureItem item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.DiscordWebhookUrl))
        {
            return new ShareResult { Succeeded = false, Message = "Discord webhook upload needs a configured webhook URL." };
        }

        var text = ExpandProviderMessage(_settings.DiscordMessageTemplate, item, "Discord");
        await using var fileStream = File.OpenRead(item.FilePath);
        using var content = new MultipartFormDataContent();
        content.Add(JsonContentString(new { content = text }), "payload_json");
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(DetectContentType(item.FilePath));
        content.Add(fileContent, "files[0]", item.FileName);

        using var response = await _httpClient.PostAsync(_settings.DiscordWebhookUrl.Trim(), content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var url = FirstUrl(body);
        if (!string.IsNullOrWhiteSpace(url))
        {
            ClipboardInterop.SetText(url);
        }

        return new ShareResult
        {
            Succeeded = response.IsSuccessStatusCode,
            Url = url,
            Message = response.IsSuccessStatusCode
                ? string.IsNullOrWhiteSpace(url)
                    ? $"Discord webhook upload completed: {(int)response.StatusCode} {response.ReasonPhrase}"
                    : $"Discord webhook upload completed and copied URL: {url}"
                : $"Discord webhook upload failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}"
        };
    }

    private async Task<ShareResult> PostToTeamsWebhookAsync(CaptureItem item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.TeamsWebhookUrl))
        {
            return new ShareResult { Succeeded = false, Message = "Microsoft Teams webhook share needs a configured incoming webhook URL." };
        }

        var text = ExpandProviderMessage(_settings.TeamsMessageTemplate, item, "Microsoft Teams");
        using var content = JsonContent(new
        {
            type = "message",
            attachments = new[]
            {
                new
                {
                    contentType = "application/vnd.microsoft.card.adaptive",
                    content = new
                    {
                        schema = "http://adaptivecards.io/schemas/adaptive-card.json",
                        type = "AdaptiveCard",
                        version = "1.4",
                        body = new object[]
                        {
                            new
                            {
                                type = "TextBlock",
                                text,
                                wrap = true
                            }
                        }
                    }
                }
            }
        });
        using var response = await _httpClient.PostAsync(_settings.TeamsWebhookUrl.Trim(), content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return new ShareResult
        {
            Succeeded = response.IsSuccessStatusCode,
            Message = response.IsSuccessStatusCode
                ? "Microsoft Teams webhook notification sent. Incoming webhooks do not upload local files; use WebDAV, FTP/FTPS, or a file provider for media transfer."
                : $"Microsoft Teams webhook notification failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}"
        };
    }

    private async Task<ShareResult> UploadToWebDavAsync(CaptureItem item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.WebDavBaseUrl))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "WebDAV upload needs a base URL setting."
            };
        }

        var password = _secretStore.ReadWebDavPassword();
        if (!string.IsNullOrWhiteSpace(_settings.WebDavUsername) && string.IsNullOrWhiteSpace(password))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "WebDAV upload has a username configured but no DPAPI-saved password."
            };
        }

        var requestUri = BuildWebDavUploadUri(item);
        await using var fileStream = File.OpenRead(item.FilePath);
        using var request = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = new StreamContent(fileStream)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(DetectContentType(item.FilePath));
        if (!string.IsNullOrWhiteSpace(_settings.WebDavUsername))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.WebDavUsername.Trim()}:{password!.Trim()}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var shareUrl = response.IsSuccessStatusCode ? BuildWebDavPublicUrl(Path.GetFileName(requestUri.LocalPath)) : null;
        if (!string.IsNullOrWhiteSpace(shareUrl))
        {
            ClipboardInterop.SetText(shareUrl);
        }

        return new ShareResult
        {
            Succeeded = response.IsSuccessStatusCode,
            Url = shareUrl,
            Message = response.IsSuccessStatusCode
                ? string.IsNullOrWhiteSpace(shareUrl)
                    ? $"WebDAV upload completed: {requestUri}"
                    : $"WebDAV upload completed and copied URL: {shareUrl}"
                : $"WebDAV upload failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}"
        };
    }

#pragma warning disable SYSLIB0014
    private async Task<ShareResult> UploadToFtpFtpsAsync(CaptureItem item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.FtpHost) ||
            string.IsNullOrWhiteSpace(_settings.FtpUsername))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "FTP/FTPS upload needs host and username settings."
            };
        }

        var password = _secretStore.ReadFtpPassword();
        if (string.IsNullOrWhiteSpace(password))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "FTP/FTPS upload needs a DPAPI-saved password."
            };
        }

        var requestUri = BuildFtpUploadUri(item);
        var useFtps = _settings.FtpUseFtps || _settings.FtpHost.Trim().StartsWith("ftps://", StringComparison.OrdinalIgnoreCase);
        var request = (FtpWebRequest)WebRequest.Create(requestUri);
        request.Method = WebRequestMethods.Ftp.UploadFile;
        request.Credentials = new NetworkCredential(_settings.FtpUsername.Trim(), password.Trim());
        request.EnableSsl = useFtps;
        request.UseBinary = true;
        request.KeepAlive = false;

        await using (var requestStream = await request.GetRequestStreamAsync())
        await using (var fileStream = File.OpenRead(item.FilePath))
        {
            await fileStream.CopyToAsync(requestStream, cancellationToken);
        }

        using var response = (FtpWebResponse)await request.GetResponseAsync();
        var shareUrl = BuildFtpPublicUrl(Path.GetFileName(requestUri.LocalPath));
        if (!string.IsNullOrWhiteSpace(shareUrl))
        {
            ClipboardInterop.SetText(shareUrl);
        }

        var statusDescription = string.IsNullOrWhiteSpace(response.StatusDescription)
            ? "upload accepted by server"
            : response.StatusDescription.Trim();

        return new ShareResult
        {
            Succeeded = true,
            Url = shareUrl,
            Message = string.IsNullOrWhiteSpace(shareUrl)
                ? $"FTP/FTPS upload completed: {statusDescription}"
                : $"FTP/FTPS upload completed and copied URL: {shareUrl}"
        };
    }
#pragma warning restore SYSLIB0014

    private async Task<ShareResult> UploadToS3Async(CaptureItem item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.S3Endpoint) ||
            string.IsNullOrWhiteSpace(_settings.S3Bucket))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "S3-compatible upload needs endpoint and bucket settings."
            };
        }

        var credentials = _secretStore.ReadS3Credentials();
        if (credentials is null)
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "S3-compatible upload needs DPAPI-saved access key credentials."
            };
        }

        var endpoint = _settings.S3Endpoint.Trim();
        if (!endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            endpoint = $"https://{endpoint}";
        }

        var endpointUri = new Uri(endpoint, UriKind.Absolute);
        var bucket = _settings.S3Bucket.Trim().Trim('/');
        var region = string.IsNullOrWhiteSpace(_settings.S3Region)
            ? "us-east-1"
            : _settings.S3Region.Trim();
        var key = BuildS3ObjectKey(item);
        var requestUri = BuildS3RequestUri(endpointUri, bucket, key);
        var payloadHash = await ComputeFileSha256Async(item.FilePath, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var host = requestUri.IsDefaultPort ? requestUri.Host : $"{requestUri.Host}:{requestUri.Port}";

        await using var fileStream = File.OpenRead(item.FilePath);
        using var request = new HttpRequestMessage(HttpMethod.Put, requestUri);
        request.Headers.Host = host;
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Content = new StreamContent(fileStream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "AWS4-HMAC-SHA256",
            BuildS3Authorization(
                credentials,
                region,
                date,
                amzDate,
                host,
                requestUri.AbsolutePath,
                payloadHash));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var shareUrl = response.IsSuccessStatusCode ? BuildS3PublicUrl(key) : null;
        if (!string.IsNullOrWhiteSpace(shareUrl))
        {
            ClipboardInterop.SetText(shareUrl);
        }

        return new ShareResult
        {
            Succeeded = response.IsSuccessStatusCode,
            Url = shareUrl,
            Message = response.IsSuccessStatusCode
                ? string.IsNullOrWhiteSpace(shareUrl)
                    ? $"S3-compatible upload completed: s3://{bucket}/{key}"
                    : $"S3-compatible upload completed and copied URL: {shareUrl}"
                : $"S3-compatible upload failed: {(int)response.StatusCode} {response.ReasonPhrase} {responseBody}"
        };
    }

    private async Task<ShareResult> UploadToImgurAsync(CaptureItem item, CancellationToken cancellationToken)
    {
        if (!IsImgurSupportedImage(item.FilePath))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "Imgur upload currently supports image files: png, jpg, jpeg, gif, bmp, or webp."
            };
        }

        var clientId = _secretStore.ReadImgurClientId();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "Imgur upload needs a DPAPI-saved Client ID."
            };
        }

        var endpoint = string.IsNullOrWhiteSpace(_settings.ImgurApiEndpoint)
            ? "https://api.imgur.com/3/image"
            : _settings.ImgurApiEndpoint.Trim();
        if (!endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            endpoint = $"https://{endpoint}";
        }

        await using var fileStream = File.OpenRead(item.FilePath);
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "image", item.FileName);
        content.Add(new StringContent("file"), "type");
        content.Add(new StringContent(Path.GetFileNameWithoutExtension(item.FileName)), "title");
        content.Add(new StringContent($"GoatShot capture {item.Id}"), "description");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Client-ID {clientId.Trim()}");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var shareUrl = response.IsSuccessStatusCode ? ExtractImgurLink(body) : null;
        if (!string.IsNullOrWhiteSpace(shareUrl))
        {
            ClipboardInterop.SetText(shareUrl);
        }

        return new ShareResult
        {
            Succeeded = response.IsSuccessStatusCode,
            Url = shareUrl,
            Message = response.IsSuccessStatusCode
                ? string.IsNullOrWhiteSpace(shareUrl)
                    ? $"Imgur upload completed: {(int)response.StatusCode} {response.ReasonPhrase}"
                    : $"Imgur upload completed and copied URL: {shareUrl}"
                : $"Imgur upload failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}"
        };
    }

    private async Task<ShareResult> UploadToSftpAsync(CaptureItem item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.SftpHost) ||
            string.IsNullOrWhiteSpace(_settings.SftpUsername))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "SFTP upload needs host and username settings."
            };
        }

        var sftp = FindSftpExecutable(_settings.SftpExecutablePath);
        if (string.IsNullOrWhiteSpace(sftp))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "SFTP upload needs OpenSSH sftp.exe on PATH, GOATSHOT_SFTP_PATH, or the configured SFTP executable path."
            };
        }

        var port = _settings.SftpPort <= 0 ? 22 : _settings.SftpPort;
        var remoteDirectory = NormalizeSftpRemoteDirectory(_settings.SftpRemoteDirectory);
        var remoteFileName = BuildRemoteFileName(item);
        var remotePath = CombineSftpRemotePath(remoteDirectory, remoteFileName);
        var privateKeyPath = string.IsNullOrWhiteSpace(_settings.SftpPrivateKeyPath)
            ? string.Empty
            : Environment.ExpandEnvironmentVariables(_settings.SftpPrivateKeyPath.Trim());
        if (!string.IsNullOrWhiteSpace(privateKeyPath) && !File.Exists(privateKeyPath))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = $"SFTP private key was configured but not found: {privateKeyPath}"
            };
        }

        Directory.CreateDirectory(_paths.TempRoot);
        var batchPath = Path.Combine(_paths.TempRoot, $"sftp-upload-{item.Id}.txt");
        var localPath = item.FilePath.Replace('\\', '/');
        var batchLines = remoteDirectory == "/"
            ? new[]
            {
                $"put {QuoteSftpPath(localPath)} {QuoteSftpPath(remotePath)}"
            }
            : new[]
            {
                $"-mkdir {QuoteSftpPath(remoteDirectory)}",
                $"put {QuoteSftpPath(localPath)} {QuoteSftpPath(remotePath)}"
            };

        await File.WriteAllLinesAsync(batchPath, batchLines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);

        try
        {
            var target = $"{_settings.SftpUsername.Trim()}@{_settings.SftpHost.Trim()}";
            var start = new ProcessStartInfo
            {
                FileName = sftp,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("-b");
            start.ArgumentList.Add(batchPath);
            start.ArgumentList.Add("-P");
            start.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add("-o");
            start.ArgumentList.Add("BatchMode=yes");
            start.ArgumentList.Add("-o");
            start.ArgumentList.Add("ConnectTimeout=15");
            if (!string.IsNullOrWhiteSpace(privateKeyPath))
            {
                start.ArgumentList.Add("-i");
                start.ArgumentList.Add(privateKeyPath);
            }

            start.ArgumentList.Add(target);

            using var process = Process.Start(start);
            if (process is null)
            {
                return new ShareResult
                {
                    Succeeded = false,
                    Message = "SFTP upload could not start sftp.exe."
                };
            }

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var shareUrl = process.ExitCode == 0 ? BuildSftpPublicUrl(remoteFileName) : null;
            if (!string.IsNullOrWhiteSpace(shareUrl))
            {
                ClipboardInterop.SetText(shareUrl);
            }

            return new ShareResult
            {
                Succeeded = process.ExitCode == 0,
                Url = shareUrl,
                Message = process.ExitCode == 0
                    ? string.IsNullOrWhiteSpace(shareUrl)
                        ? $"SFTP upload completed: {target}:{remotePath}"
                        : $"SFTP upload completed and copied URL: {shareUrl}"
                    : $"SFTP upload failed with exit code {process.ExitCode}: {ShortProcessOutput(stderr, stdout)}"
            };
        }
        finally
        {
            try
            {
                File.Delete(batchPath);
            }
            catch
            {
                // Temp batch cleanup is best-effort.
            }
        }
    }

    private async Task<ShareResult> UploadToCloudinaryAsync(CaptureItem item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.CloudinaryCloudName))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "Cloudinary upload needs a cloud name setting."
            };
        }

        var credentials = _secretStore.ReadCloudinaryCredentials();
        var unsigned = credentials is null;
        if (unsigned && string.IsNullOrWhiteSpace(_settings.CloudinaryUploadPreset))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "Cloudinary unsigned upload needs an upload preset, or save API key/secret credentials for authenticated upload."
            };
        }

        var endpoint = BuildCloudinaryUploadUri();
        await using var fileStream = File.OpenRead(item.FilePath);
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", item.FileName);
        if (!string.IsNullOrWhiteSpace(_settings.CloudinaryUploadPreset))
        {
            content.Add(new StringContent(_settings.CloudinaryUploadPreset.Trim()), "upload_preset");
        }

        if (!string.IsNullOrWhiteSpace(_settings.CloudinaryFolder))
        {
            content.Add(new StringContent(_settings.CloudinaryFolder.Trim().Trim('/')), "folder");
        }

        content.Add(new StringContent($"goatshot_{item.Id}"), "public_id");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = content
        };
        if (credentials is not null)
        {
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.ApiKey}:{credentials.ApiSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var shareUrl = response.IsSuccessStatusCode ? ExtractCloudinaryUrl(body) : null;
        if (!string.IsNullOrWhiteSpace(shareUrl))
        {
            ClipboardInterop.SetText(shareUrl);
        }

        return new ShareResult
        {
            Succeeded = response.IsSuccessStatusCode,
            Url = shareUrl,
            Message = response.IsSuccessStatusCode
                ? string.IsNullOrWhiteSpace(shareUrl)
                    ? $"Cloudinary upload completed: {(int)response.StatusCode} {response.ReasonPhrase}"
                    : $"Cloudinary upload completed and copied URL: {shareUrl}"
                : $"Cloudinary upload failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}"
        };
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

    private async Task<ShareResult> UploadToLinearAsync(CaptureItem item, CancellationToken cancellationToken)
    {
        var credential = _secretStore.ReadLinearCredential();
        if (string.IsNullOrWhiteSpace(credential))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "Linear upload needs a DPAPI-saved personal API key or OAuth access token."
            };
        }

        var existingIssueId = TrimToNull(_settings.LinearIssueId);
        var teamId = TrimToNull(_settings.LinearTeamId);
        if (string.IsNullOrWhiteSpace(existingIssueId) && string.IsNullOrWhiteSpace(teamId))
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = "Linear upload needs either an existing issue ID or a team ID for issue creation."
            };
        }

        var uploadRequest = await RequestLinearFileUploadAsync(item, credential.Trim(), cancellationToken);
        if (!uploadRequest.Succeeded || uploadRequest.Upload is null)
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = $"Linear file upload request failed: {uploadRequest.Message}"
            };
        }

        var fileUpload = await UploadLinearFileContentAsync(item, uploadRequest.Upload, cancellationToken);
        if (!fileUpload.Succeeded)
        {
            return new ShareResult
            {
                Succeeded = false,
                Message = $"Linear signed file upload failed: {fileUpload.Message}"
            };
        }

        LinearIssueInfo? issue = null;
        var issueId = existingIssueId;
        var createdIssueMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(issueId))
        {
            var issueCreate = await CreateLinearIssueAsync(item, uploadRequest.Upload.AssetUrl, credential.Trim(), cancellationToken);
            if (!issueCreate.Succeeded || issueCreate.Issue is null)
            {
                return new ShareResult
                {
                    Succeeded = false,
                    Url = uploadRequest.Upload.AssetUrl,
                    Message = $"Linear file uploaded, but issue creation failed: {issueCreate.Message}"
                };
            }

            issue = issueCreate.Issue;
            issueId = issue.Id;
            createdIssueMessage = string.IsNullOrWhiteSpace(issue.Identifier)
                ? "Linear issue created."
                : $"Linear issue {issue.Identifier} created.";
        }

        var attachmentMessage = string.Empty;
        if (_settings.LinearCreateAttachment)
        {
            var attachment = await CreateLinearAttachmentAsync(
                issueId!,
                item,
                uploadRequest.Upload.AssetUrl,
                credential.Trim(),
                cancellationToken);
            if (!attachment.Succeeded)
            {
                return new ShareResult
                {
                    Succeeded = false,
                    Url = issue?.Url ?? uploadRequest.Upload.AssetUrl,
                    Message = $"Linear file uploaded, but attachment linking failed: {attachment.Message}"
                };
            }

            attachmentMessage = " Linear attachment was linked to the issue.";
        }

        var shareUrl = issue?.Url ?? uploadRequest.Upload.AssetUrl;
        if (!string.IsNullOrWhiteSpace(shareUrl))
        {
            ClipboardInterop.SetText(shareUrl);
        }

        var targetMessage = issue is null
            ? $"Linear file uploaded and attached to existing issue {issueId}."
            : createdIssueMessage;
        return new ShareResult
        {
            Succeeded = true,
            Url = shareUrl,
            Message = string.IsNullOrWhiteSpace(shareUrl)
                ? $"{targetMessage}{attachmentMessage} Uploaded file is stored in Linear private file storage."
                : $"{targetMessage}{attachmentMessage} Copied URL: {shareUrl}"
        };
    }

    private async Task<(bool Succeeded, LinearUploadFile? Upload, string Message)> RequestLinearFileUploadAsync(
        CaptureItem item,
        string credential,
        CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(item.FilePath);
        if (fileInfo.Length > int.MaxValue)
        {
            return (false, null, "Linear file upload size is too large for the GraphQL fileUpload request.");
        }

        const string query =
            """
            mutation RequestUpload($filename: String!, $contentType: String!, $size: Int!) {
              fileUpload(filename: $filename, contentType: $contentType, size: $size) {
                success
                uploadFile {
                  uploadUrl
                  assetUrl
                  headers {
                    key
                    value
                  }
                }
              }
            }
            """;
        var variables = new
        {
            filename = BuildRemoteFileName(item),
            contentType = DetectContentType(item.FilePath),
            size = (int)fileInfo.Length
        };

        var result = await SendLinearGraphQlAsync(query, variables, credential, cancellationToken);
        if (!result.Succeeded)
        {
            return (false, null, result.Message);
        }

        var upload = ExtractLinearUploadFile(result.Body);
        return upload is null
            ? (false, null, "Linear GraphQL response did not include an upload URL and asset URL.")
            : (true, upload, "Linear upload URL returned.");
    }

    private async Task<(bool Succeeded, string Message)> UploadLinearFileContentAsync(
        CaptureItem item,
        LinearUploadFile upload,
        CancellationToken cancellationToken)
    {
        await using var fileStream = File.OpenRead(item.FilePath);
        using var request = new HttpRequestMessage(HttpMethod.Put, upload.UploadUrl);
        request.Content = new StreamContent(fileStream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(DetectContentType(item.FilePath));
        ApplyLinearUploadHeaders(request, upload.Headers);
        if (upload.Headers.All(header => !header.Key.Equals("Cache-Control", StringComparison.OrdinalIgnoreCase)))
        {
            request.Headers.TryAddWithoutValidation("Cache-Control", "public, max-age=31536000");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return response.IsSuccessStatusCode
            ? (true, "Linear signed upload completed.")
            : (false, $"{(int)response.StatusCode} {response.ReasonPhrase} {body}");
    }

    private async Task<(bool Succeeded, LinearIssueInfo? Issue, string Message)> CreateLinearIssueAsync(
        CaptureItem item,
        string assetUrl,
        string credential,
        CancellationToken cancellationToken)
    {
        var teamId = TrimToNull(_settings.LinearTeamId);
        if (string.IsNullOrWhiteSpace(teamId))
        {
            return (false, null, "Linear team ID is required when creating a new issue.");
        }

        const string query =
            """
            mutation CreateIssue($input: IssueCreateInput!) {
              issueCreate(input: $input) {
                success
                issue {
                  id
                  identifier
                  url
                  title
                }
              }
            }
            """;
        var variables = new
        {
            input = new
            {
                teamId,
                title = BuildLinearIssueTitle(item),
                description = BuildLinearIssueDescription(item, assetUrl)
            }
        };

        var result = await SendLinearGraphQlAsync(query, variables, credential, cancellationToken);
        if (!result.Succeeded)
        {
            return (false, null, result.Message);
        }

        var issue = ExtractLinearIssue(result.Body);
        return issue is null
            ? (false, null, "Linear GraphQL response did not include the created issue.")
            : (true, issue, "Linear issue created.");
    }

    private async Task<(bool Succeeded, string Message)> CreateLinearAttachmentAsync(
        string issueId,
        CaptureItem item,
        string assetUrl,
        string credential,
        CancellationToken cancellationToken)
    {
        const string query =
            """
            mutation CreateAttachment($input: AttachmentCreateInput!) {
              attachmentCreate(input: $input) {
                success
                attachment {
                  id
                  url
                }
              }
            }
            """;
        var variables = new
        {
            input = new
            {
                issueId,
                title = $"GoatShot capture: {item.FileName}",
                subtitle = $"{item.Kind} capture, {item.Width}x{item.Height}, {item.BytesLabel}",
                url = assetUrl
            }
        };

        var result = await SendLinearGraphQlAsync(query, variables, credential, cancellationToken);
        if (!result.Succeeded)
        {
            return (false, result.Message);
        }

        var attachmentId = ExtractLinearAttachmentId(result.Body);
        return string.IsNullOrWhiteSpace(attachmentId)
            ? (false, "Linear GraphQL response did not include the created attachment.")
            : (true, $"Linear attachment created: {attachmentId}.");
    }

    private async Task<(bool Succeeded, string Body, string Message)> SendLinearGraphQlAsync(
        string query,
        object variables,
        string credential,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildLinearGraphQlUri());
        ApplyLinearAuthorization(request, credential);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { query, variables }, CompactJsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (false, body, $"{(int)response.StatusCode} {response.ReasonPhrase} {body}");
        }

        var graphQlErrors = ExtractGraphQlErrors(body);
        return string.IsNullOrWhiteSpace(graphQlErrors)
            ? (true, body, "Linear GraphQL request completed.")
            : (false, body, graphQlErrors);
    }

    private void ApplyLinearAuthorization(HttpRequestMessage request, string credential)
    {
        if (_settings.LinearUseOAuthBearerToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Trim());
            return;
        }

        request.Headers.TryAddWithoutValidation("Authorization", credential.Trim());
    }

    private Uri BuildLinearGraphQlUri()
    {
        var endpoint = string.IsNullOrWhiteSpace(_settings.LinearGraphqlEndpoint)
            ? "https://api.linear.app/graphql"
            : _settings.LinearGraphqlEndpoint.Trim();
        if (!endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            endpoint = $"https://{endpoint}";
        }

        return new Uri(endpoint, UriKind.Absolute);
    }

    private string BuildLinearIssueTitle(CaptureItem item)
    {
        var template = string.IsNullOrWhiteSpace(_settings.LinearIssueTitleTemplate)
            ? "GoatShot capture: {file}"
            : _settings.LinearIssueTitleTemplate.Trim();
        return template
            .Replace("{file}", item.FileName, StringComparison.OrdinalIgnoreCase)
            .Replace("{id}", item.Id, StringComparison.OrdinalIgnoreCase)
            .Replace("{capture_type}", item.Kind.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{source_app}", item.SourceApp ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{width}", item.Width.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{height}", item.Height.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", item.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", item.CreatedAt.LocalDateTime.ToString("HH-mm-ss", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static string BuildLinearIssueDescription(CaptureItem item, string assetUrl)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Created from GoatShot.");
        builder.AppendLine();
        if (DetectContentType(item.FilePath).StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine($"![{EscapeMarkdownLabel(item.FileName)}]({assetUrl})");
        }
        else
        {
            builder.AppendLine($"[{EscapeMarkdownLabel(item.FileName)}]({assetUrl})");
        }

        builder.AppendLine();
        builder.AppendLine("| Field | Value |");
        builder.AppendLine("| --- | --- |");
        builder.AppendLine($"| Capture ID | `{item.Id}` |");
        builder.AppendLine($"| File | `{item.FileName}` |");
        builder.AppendLine($"| Type | `{item.Kind}` |");
        builder.AppendLine($"| Size | `{item.Width}x{item.Height}`, `{item.BytesLabel}` |");
        builder.AppendLine($"| Created | `{item.CreatedAt:O}` |");
        if (!string.IsNullOrWhiteSpace(item.SourceApp))
        {
            builder.AppendLine($"| Source app | `{EscapeMarkdownTableValue(item.SourceApp)}` |");
        }

        if (!string.IsNullOrWhiteSpace(item.SourceWindowTitle))
        {
            builder.AppendLine($"| Window | `{EscapeMarkdownTableValue(item.SourceWindowTitle)}` |");
        }

        if (item.Bounds is not null)
        {
            builder.AppendLine($"| Bounds | `{EscapeMarkdownTableValue(item.Bounds.Display)}` |");
        }

        return builder.ToString().Trim();
    }

    private static void ApplyLinearUploadHeaders(HttpRequestMessage request, IReadOnlyList<LinearUploadHeader> headers)
    {
        foreach (var header in headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key) || string.IsNullOrWhiteSpace(header.Value))
            {
                continue;
            }

            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    request.Content!.Headers.ContentType = MediaTypeHeaderValue.Parse(header.Value);
                }
                catch (FormatException)
                {
                    request.Content!.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                continue;
            }

            if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
            {
                request.Content!.Headers.TryAddWithoutValidation(header.Key, header.Value);
                continue;
            }

            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private static LinearUploadFile? ExtractLinearUploadFile(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!TryGetDataObject(document.RootElement, "fileUpload", out var upload) ||
                !ReadSuccess(upload) ||
                !upload.TryGetProperty("uploadFile", out var uploadFile) ||
                uploadFile.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var uploadUrl = ReadString(uploadFile, "uploadUrl");
            var assetUrl = ReadString(uploadFile, "assetUrl");
            if (string.IsNullOrWhiteSpace(uploadUrl) || string.IsNullOrWhiteSpace(assetUrl))
            {
                return null;
            }

            var headers = new List<LinearUploadHeader>();
            if (uploadFile.TryGetProperty("headers", out var headerArray) &&
                headerArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var header in headerArray.EnumerateArray())
                {
                    var key = ReadString(header, "key");
                    var value = ReadString(header, "value");
                    if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                    {
                        headers.Add(new LinearUploadHeader(key, value));
                    }
                }
            }

            return new LinearUploadFile(uploadUrl, assetUrl, headers);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LinearIssueInfo? ExtractLinearIssue(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!TryGetDataObject(document.RootElement, "issueCreate", out var issueCreate) ||
                !ReadSuccess(issueCreate) ||
                !issueCreate.TryGetProperty("issue", out var issue) ||
                issue.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var id = ReadString(issue, "id");
            return string.IsNullOrWhiteSpace(id)
                ? null
                : new LinearIssueInfo(
                    id,
                    ReadString(issue, "identifier"),
                    ReadString(issue, "url"),
                    ReadString(issue, "title"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractLinearAttachmentId(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!TryGetDataObject(document.RootElement, "attachmentCreate", out var attachmentCreate) ||
                !ReadSuccess(attachmentCreate) ||
                !attachmentCreate.TryGetProperty("attachment", out var attachment) ||
                attachment.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return ReadString(attachment, "id");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractGraphQlErrors(string responseBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("errors", out var errors) ||
                errors.ValueKind != JsonValueKind.Array ||
                errors.GetArrayLength() == 0)
            {
                return null;
            }

            var messages = errors
                .EnumerateArray()
                .Select(error => ReadString(error, "message"))
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .ToList();
            return messages.Count == 0
                ? "Linear GraphQL returned one or more errors."
                : $"Linear GraphQL error: {string.Join("; ", messages)}";
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetDataObject(JsonElement root, string propertyName, out JsonElement value)
    {
        value = default;
        return root.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty(propertyName, out value) &&
            value.ValueKind == JsonValueKind.Object;
    }

    private static bool ReadSuccess(JsonElement element)
    {
        return !element.TryGetProperty("success", out var success) ||
            success.ValueKind != JsonValueKind.False;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string EscapeMarkdownLabel(string value)
    {
        return value
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
    }

    private static string EscapeMarkdownTableValue(string value)
    {
        return value
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("`", "'", StringComparison.Ordinal);
    }

    private static string? TrimToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static (string Owner, string Repo)? ParseGitHubRepository(string value)
    {
        var parts = value.Trim().Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 && parts.All(part => !string.IsNullOrWhiteSpace(part))
            ? (parts[0], parts[1])
            : null;
    }

    private Uri BuildGitHubIssueUri(string owner, string repo)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.GitHubApiBaseUrl)
            ? "https://api.github.com"
            : NormalizeHttpBaseUrl(_settings.GitHubApiBaseUrl, "GitHub API base URL");
        var builder = new UriBuilder(baseUrl);
        var basePath = builder.Path.Trim('/');
        builder.Path = CombineUriPath(basePath, "repos", EncodeCloudPathSegment(owner), EncodeCloudPathSegment(repo), "issues");
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private Uri BuildJiraIssueUri()
    {
        var baseUrl = NormalizeHttpBaseUrl(_settings.JiraBaseUrl, "Jira base URL");
        var builder = new UriBuilder(baseUrl);
        var basePath = builder.Path.Trim('/');
        builder.Path = basePath.EndsWith("rest/api/3", StringComparison.OrdinalIgnoreCase)
            ? CombineUriPath(basePath, "issue")
            : CombineUriPath(basePath, "rest", "api", "3", "issue");
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private string? BuildJiraBrowseUrl(string? issueKey)
    {
        if (string.IsNullOrWhiteSpace(issueKey))
        {
            return null;
        }

        var baseUrl = NormalizeHttpBaseUrl(_settings.JiraBaseUrl, "Jira base URL");
        var builder = new UriBuilder(baseUrl);
        var basePath = builder.Path.Trim('/');
        builder.Path = CombineUriPath(basePath, "browse", EncodeCloudPathSegment(issueKey.Trim()));
        builder.Query = string.Empty;
        return builder.Uri.ToString();
    }

    private Uri BuildAzureDevOpsCreateWorkItemUri()
    {
        var baseUrl = NormalizeHttpBaseUrl(_settings.AzureDevOpsBaseUrl, "Azure DevOps base URL");
        var builder = new UriBuilder(baseUrl);
        var basePath = builder.Path.Trim('/');
        builder.Path = CombineUriPath(
            basePath,
            EncodeCloudPathSegment(_settings.AzureDevOpsOrganization.Trim()),
            EncodeCloudPathSegment(_settings.AzureDevOpsProject.Trim()),
            "_apis",
            "wit",
            "workitems",
            $"${EncodeCloudPathSegment(string.IsNullOrWhiteSpace(_settings.AzureDevOpsWorkItemType) ? "Bug" : _settings.AzureDevOpsWorkItemType.Trim())}");
        builder.Query = "api-version=7.1";
        return builder.Uri;
    }

    private string? BuildAzureDevOpsBrowseUrl(int? workItemId)
    {
        if (workItemId is null)
        {
            return null;
        }

        var baseUrl = NormalizeHttpBaseUrl(_settings.AzureDevOpsBaseUrl, "Azure DevOps base URL");
        var builder = new UriBuilder(baseUrl);
        var basePath = builder.Path.Trim('/');
        builder.Path = CombineUriPath(
            basePath,
            EncodeCloudPathSegment(_settings.AzureDevOpsOrganization.Trim()),
            EncodeCloudPathSegment(_settings.AzureDevOpsProject.Trim()),
            "_workitems",
            "edit",
            workItemId.Value.ToString(CultureInfo.InvariantCulture));
        builder.Query = string.Empty;
        return builder.Uri.ToString();
    }

    private static Dictionary<string, object?> JsonPatchAdd(string path, object? value)
    {
        return new Dictionary<string, object?>
        {
            ["op"] = "add",
            ["path"] = path,
            ["value"] = value
        };
    }

    private static string BuildAzureDevOpsTags(string? value)
    {
        return string.Join("; ", SplitProviderList(value));
    }

    private static IReadOnlyList<string> SplitProviderList(string? value)
    {
        return (value ?? string.Empty)
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildIssueMarkdownBody(CaptureItem item, string destinationName)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Created from GoatShot.");
        builder.AppendLine();
        builder.AppendLine("| Field | Value |");
        builder.AppendLine("|---|---|");
        builder.AppendLine($"| File | {EscapeMarkdownTableValue(item.FileName)} |");
        builder.AppendLine($"| Capture type | {item.Kind} |");
        builder.AppendLine($"| Capture ID | {EscapeMarkdownTableValue(item.Id)} |");
        builder.AppendLine($"| Created | {item.CreatedAt:O} |");
        builder.AppendLine($"| Size | {EscapeMarkdownTableValue(item.BytesLabel)} |");
        builder.AppendLine($"| Dimensions | {item.Width} x {item.Height} |");
        if (!string.IsNullOrWhiteSpace(item.SourceApp))
        {
            builder.AppendLine($"| Source app | {EscapeMarkdownTableValue(item.SourceApp)} |");
        }

        if (!string.IsNullOrWhiteSpace(item.SourceWindowTitle))
        {
            builder.AppendLine($"| Window title | {EscapeMarkdownTableValue(item.SourceWindowTitle)} |");
        }

        builder.AppendLine();
        builder.AppendLine($"{destinationName} issue creation does not upload the local capture file. Upload the media through a file destination such as S3, WebDAV, FTP/FTPS, Dropbox, Google Drive, OneDrive, Cloudinary, Discord, or Linear when a remote asset is needed.");

        AppendRedactedMarkdownSection(builder, "Notes", item.Notes, 4_000);
        AppendRedactedMarkdownSection(builder, "OCR", item.OcrText, 4_000);
        return builder.ToString().Trim();
    }

    private static string BuildAzureDevOpsDescriptionHtml(CaptureItem item)
    {
        var lines = BuildIssuePlainLines(item, "Azure DevOps");
        var htmlLines = lines.Select(line => string.IsNullOrWhiteSpace(line)
            ? "<br/>"
            : $"{WebUtility.HtmlEncode(line)}<br/>");
        return string.Concat(htmlLines);
    }

    private static object BuildJiraAdfDescription(CaptureItem item)
    {
        var lines = BuildIssuePlainLines(item, "Jira");
        return new
        {
            version = 1,
            type = "doc",
            content = lines
                .Select(line => new
                {
                    type = "paragraph",
                    content = string.IsNullOrWhiteSpace(line)
                        ? Array.Empty<object>()
                        : new object[]
                        {
                            new
                            {
                                type = "text",
                                text = line
                            }
                        }
                })
                .ToArray()
        };
    }

    private static IReadOnlyList<string> BuildIssuePlainLines(CaptureItem item, string destinationName)
    {
        var lines = new List<string>
        {
            "Created from GoatShot.",
            string.Empty,
            $"File: {item.FileName}",
            $"Capture type: {item.Kind}",
            $"Capture ID: {item.Id}",
            $"Created: {item.CreatedAt:O}",
            $"Size: {item.BytesLabel}",
            $"Dimensions: {item.Width} x {item.Height}"
        };
        if (!string.IsNullOrWhiteSpace(item.SourceApp))
        {
            lines.Add($"Source app: {item.SourceApp}");
        }

        if (!string.IsNullOrWhiteSpace(item.SourceWindowTitle))
        {
            lines.Add($"Window title: {item.SourceWindowTitle}");
        }

        lines.Add(string.Empty);
        lines.Add($"{destinationName} issue creation does not upload the local capture file. Upload the media through a file destination when a remote asset is needed.");

        AppendRedactedPlainSection(lines, "Notes", item.Notes, 4_000);
        AppendRedactedPlainSection(lines, "OCR", item.OcrText, 4_000);
        return lines;
    }

    private static void AppendRedactedMarkdownSection(StringBuilder builder, string heading, string? text, int maxLength)
    {
        var redacted = RedactAndTrim(text, maxLength);
        if (string.IsNullOrWhiteSpace(redacted))
        {
            return;
        }

        builder.AppendLine();
        builder.AppendLine($"## {heading}");
        builder.AppendLine();
        builder.AppendLine("```text");
        builder.AppendLine(redacted);
        builder.AppendLine("```");
    }

    private static void AppendRedactedPlainSection(List<string> lines, string heading, string? text, int maxLength)
    {
        var redacted = RedactAndTrim(text, maxLength);
        if (string.IsNullOrWhiteSpace(redacted))
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add($"{heading}:");
        lines.Add(redacted);
    }

    private static string RedactAndTrim(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var redacted = SensitiveTextDetector.Scan(text).RedactedText.Trim();
        return redacted.Length <= maxLength
            ? redacted
            : redacted[..maxLength] + "...";
    }

    private static string? ExtractJsonString(string responseBody, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return ReadString(document.RootElement, propertyName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ExtractJsonInt(string responseBody, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty(propertyName, out var value))
            {
                return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
                    ? number
                    : null;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private string? ExtractAzureDevOpsWorkItemUrl(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.TryGetProperty("_links", out var links) &&
                links.ValueKind == JsonValueKind.Object &&
                links.TryGetProperty("html", out var html) &&
                html.ValueKind == JsonValueKind.Object)
            {
                var href = ReadString(html, "href");
                if (!string.IsNullOrWhiteSpace(href))
                {
                    return href;
                }
            }

            var url = ReadString(root, "url");
            if (!string.IsNullOrWhiteSpace(url))
            {
                return url;
            }
        }
        catch (JsonException)
        {
            return FirstUrl(responseBody);
        }

        return FirstUrl(responseBody);
    }

    private static StringContent JsonContent(object value)
    {
        return JsonContentString(value);
    }

    private static StringContent JsonContentString(object value)
    {
        return new StringContent(JsonSerializer.Serialize(value, CompactJsonOptions), Encoding.UTF8, "application/json");
    }

    private static string ExpandProviderMessage(string? template, CaptureItem item, string providerName)
    {
        var resolved = string.IsNullOrWhiteSpace(template)
            ? "GoatShot capture ready: {file} ({bytes} bytes)"
            : template.Trim();

        return resolved
            .Replace("{provider}", providerName, StringComparison.OrdinalIgnoreCase)
            .Replace("{file}", item.FileName, StringComparison.OrdinalIgnoreCase)
            .Replace("{path}", item.FilePath, StringComparison.OrdinalIgnoreCase)
            .Replace("{id}", item.Id, StringComparison.OrdinalIgnoreCase)
            .Replace("{capture_type}", item.Kind.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{bytes}", item.Bytes.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{width}", item.Width.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{height}", item.Height.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{created_at}", item.CreatedAt.ToString("O", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    private string BuildS3ObjectKey(CaptureItem item)
    {
        var prefix = (_settings.S3KeyPrefix ?? string.Empty)
            .Replace('\\', '/')
            .Trim('/');
        var fileName = string.Join("_", item.FileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        var objectName = $"{item.Id}-{fileName}";
        return string.IsNullOrWhiteSpace(prefix)
            ? objectName
            : $"{prefix}/{objectName}";
    }

    private static Uri BuildS3RequestUri(Uri endpoint, string bucket, string key)
    {
        var builder = new UriBuilder(endpoint);
        var basePath = builder.Path.Trim('/');
        var path = string.IsNullOrWhiteSpace(basePath)
            ? $"{EncodeS3PathSegment(bucket)}/{EncodeS3Key(key)}"
            : $"{EncodeS3Key(basePath)}/{EncodeS3PathSegment(bucket)}/{EncodeS3Key(key)}";
        builder.Path = path;
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private string? BuildS3PublicUrl(string key)
    {
        if (string.IsNullOrWhiteSpace(_settings.S3PublicBaseUrl))
        {
            return null;
        }

        var publicBaseUrl = _settings.S3PublicBaseUrl.Trim();
        if (!publicBaseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !publicBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            publicBaseUrl = $"https://{publicBaseUrl}";
        }

        var builder = new UriBuilder(publicBaseUrl);
        var basePath = builder.Path.Trim('/');
        builder.Path = string.IsNullOrWhiteSpace(basePath)
            ? EncodeS3Key(key)
            : $"{EncodeS3Key(basePath)}/{EncodeS3Key(key)}";
        return builder.Uri.ToString();
    }

    private static string BuildS3Authorization(
        S3Credentials credentials,
        string region,
        string date,
        string amzDate,
        string host,
        string canonicalUri,
        string payloadHash)
    {
        const string signedHeaders = "content-type;host;x-amz-content-sha256;x-amz-date";
        var credentialScope = $"{date}/{region}/s3/aws4_request";
        var canonicalHeaders =
            "content-type:application/octet-stream\n" +
            $"host:{host}\n" +
            $"x-amz-content-sha256:{payloadHash}\n" +
            $"x-amz-date:{amzDate}\n";
        var canonicalRequest = string.Join(
            "\n",
            "PUT",
            canonicalUri,
            string.Empty,
            canonicalHeaders,
            signedHeaders,
            payloadHash);
        var stringToSign = string.Join(
            "\n",
            "AWS4-HMAC-SHA256",
            amzDate,
            credentialScope,
            Sha256Hex(canonicalRequest));
        var signature = HmacSha256Hex(
            GetS3SigningKey(credentials.SecretAccessKey, date, region),
            stringToSign);

        return $"Credential={credentials.AccessKeyId}/{credentialScope}, SignedHeaders={signedHeaders}, Signature={signature}";
    }

    private static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Sha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static byte[] GetS3SigningKey(string secretAccessKey, string date, string region)
    {
        var dateKey = HmacSha256(Encoding.UTF8.GetBytes($"AWS4{secretAccessKey}"), date);
        var dateRegionKey = HmacSha256(dateKey, region);
        var dateRegionServiceKey = HmacSha256(dateRegionKey, "s3");
        return HmacSha256(dateRegionServiceKey, "aws4_request");
    }

    private static byte[] HmacSha256(byte[] key, string value)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
    }

    private static string HmacSha256Hex(byte[] key, string value)
    {
        return Convert.ToHexString(HmacSha256(key, value)).ToLowerInvariant();
    }

    private static string EncodeS3Key(string key)
    {
        return string.Join(
            "/",
            key.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(EncodeS3PathSegment));
    }

    private static string EncodeS3PathSegment(string segment)
    {
        return Uri.EscapeDataString(segment)
            .Replace("%2D", "-", StringComparison.OrdinalIgnoreCase)
            .Replace("%2E", ".", StringComparison.OrdinalIgnoreCase)
            .Replace("%5F", "_", StringComparison.OrdinalIgnoreCase)
            .Replace("%7E", "~", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImgurSupportedImage(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp";
    }

    private Uri BuildWebDavUploadUri(CaptureItem item)
    {
        var baseUrl = NormalizeHttpBaseUrl(_settings.WebDavBaseUrl, "WebDAV base URL");
        var builder = new UriBuilder(baseUrl);
        var basePath = builder.Path.Trim('/');
        var folder = NormalizeCloudFolderPath(_settings.WebDavRemoteDirectory);
        var fileName = EncodeCloudPathSegment(BuildRemoteFileName(item));
        builder.Path = CombineUriPath(basePath, folder, fileName);
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private string? BuildWebDavPublicUrl(string remoteFileName)
    {
        if (string.IsNullOrWhiteSpace(_settings.WebDavPublicBaseUrl))
        {
            return null;
        }

        var publicBaseUrl = NormalizeHttpBaseUrl(_settings.WebDavPublicBaseUrl, "WebDAV public base URL");
        var builder = new UriBuilder(publicBaseUrl);
        var basePath = builder.Path.Trim('/');
        builder.Path = CombineUriPath(basePath, EncodeCloudPathSegment(remoteFileName));
        builder.Query = string.Empty;
        return builder.Uri.ToString();
    }

    private Uri BuildFtpUploadUri(CaptureItem item)
    {
        var rawHost = _settings.FtpHost.Trim();
        if (rawHost.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase))
        {
            rawHost = $"ftp://{rawHost[7..]}";
        }
        else if (!rawHost.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
        {
            rawHost = $"ftp://{rawHost}";
        }

        var original = new Uri(rawHost, UriKind.Absolute);
        var builder = new UriBuilder(original)
        {
            Scheme = Uri.UriSchemeFtp
        };
        if (_settings.FtpPort > 0 && (_settings.FtpPort != 21 || original.IsDefaultPort))
        {
            builder.Port = _settings.FtpPort;
        }

        var basePath = builder.Path.Trim('/');
        var folder = NormalizeCloudFolderPath(_settings.FtpRemoteDirectory);
        var fileName = EncodeCloudPathSegment(BuildRemoteFileName(item));
        builder.Path = CombineUriPath(basePath, folder, fileName);
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private string? BuildFtpPublicUrl(string remoteFileName)
    {
        if (string.IsNullOrWhiteSpace(_settings.FtpPublicBaseUrl))
        {
            return null;
        }

        var publicBaseUrl = NormalizeHttpBaseUrl(_settings.FtpPublicBaseUrl, "FTP/FTPS public base URL");
        var builder = new UriBuilder(publicBaseUrl);
        var basePath = builder.Path.Trim('/');
        builder.Path = CombineUriPath(basePath, EncodeCloudPathSegment(remoteFileName));
        builder.Query = string.Empty;
        return builder.Uri.ToString();
    }

    private static string NormalizeHttpBaseUrl(string value, string settingName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{settingName} is not configured.");
        }

        var baseUrl = value.Trim();
        if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = $"https://{baseUrl}";
        }

        return baseUrl;
    }

    private static string CombineUriPath(params string?[] parts)
    {
        return string.Join(
            "/",
            parts
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim('/')));
    }

    private Uri BuildCloudinaryUploadUri()
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.CloudinaryApiBaseUrl)
            ? "https://api.cloudinary.com"
            : _settings.CloudinaryApiBaseUrl.Trim();
        if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = $"https://{baseUrl}";
        }

        var resourceType = NormalizeCloudinaryResourceType(_settings.CloudinaryResourceType);
        var builder = new UriBuilder(baseUrl);
        var basePath = builder.Path.Trim('/');
        var pathParts = new[]
        {
            basePath,
            "v1_1",
            Uri.EscapeDataString(_settings.CloudinaryCloudName.Trim()),
            resourceType,
            "upload"
        }.Where(part => !string.IsNullOrWhiteSpace(part));
        builder.Path = string.Join("/", pathParts);
        builder.Query = string.Empty;
        return builder.Uri;
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

    private static string NormalizeCloudinaryResourceType(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "image" or "video" or "raw" or "auto"
            ? normalized
            : "auto";
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

    private static string? ExtractCloudinaryUrl(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.TryGetProperty("secure_url", out var secureUrl) &&
                secureUrl.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(secureUrl.GetString()))
            {
                return secureUrl.GetString();
            }

            if (root.TryGetProperty("url", out var url) &&
                url.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(url.GetString()))
            {
                return url.GetString();
            }
        }
        catch (JsonException)
        {
            // Compatible local mocks may return plain text; URL fallback keeps them useful.
        }

        return FirstUrl(responseBody);
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

    private static string NormalizeSftpRemoteDirectory(string? value)
    {
        var directory = string.IsNullOrWhiteSpace(value)
            ? "/"
            : value.Replace('\\', '/').Trim();

        if (directory.Length == 0 || directory == ".")
        {
            return ".";
        }

        if (directory == "/")
        {
            return "/";
        }

        return directory.TrimEnd('/');
    }

    private static string CombineSftpRemotePath(string directory, string fileName)
    {
        return directory is "" or "." or "/"
            ? directory == "/" ? $"/{fileName}" : fileName
            : $"{directory.TrimEnd('/')}/{fileName}";
    }

    private string? BuildSftpPublicUrl(string remoteFileName)
    {
        if (string.IsNullOrWhiteSpace(_settings.SftpPublicBaseUrl))
        {
            return null;
        }

        var publicBaseUrl = _settings.SftpPublicBaseUrl.Trim();
        if (!publicBaseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !publicBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            publicBaseUrl = $"https://{publicBaseUrl}";
        }

        var builder = new UriBuilder(publicBaseUrl);
        var basePath = builder.Path.Trim('/');
        builder.Path = string.IsNullOrWhiteSpace(basePath)
            ? EncodeS3PathSegment(remoteFileName)
            : $"{EncodeS3Key(basePath)}/{EncodeS3PathSegment(remoteFileName)}";
        return builder.Uri.ToString();
    }

    private static string QuoteSftpPath(string path)
    {
        return $"\"{path.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string ShortProcessOutput(string stderr, string stdout)
    {
        var text = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        text = text.ReplaceLineEndings(" ").Trim();
        return text.Length <= 360 ? text : text[..360] + "...";
    }

    private static string? ExtractImgurLink(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Object &&
                data.TryGetProperty("link", out var linkElement) &&
                linkElement.ValueKind == JsonValueKind.String)
            {
                var link = linkElement.GetString();
                return string.IsNullOrWhiteSpace(link) ? null : link;
            }
        }
        catch (JsonException)
        {
            // Some compatible mocks return plain text; URL fallback below keeps tests and custom endpoints useful.
        }

        return FirstUrl(responseBody);
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

    private sealed record LinearUploadFile(
        string UploadUrl,
        string AssetUrl,
        IReadOnlyList<LinearUploadHeader> Headers);

    private sealed record LinearUploadHeader(string Key, string Value);

    private sealed record LinearIssueInfo(
        string Id,
        string? Identifier,
        string? Url,
        string? Title);
}
