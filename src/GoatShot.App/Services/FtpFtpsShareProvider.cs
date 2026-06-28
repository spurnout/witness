using System.Net;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class FtpFtpsShareProvider : IShareProvider
{
    private readonly AppSettings _settings;
    private readonly SecretStore _secretStore;
    private readonly IFtpUploadClient _ftpClient;

    public FtpFtpsShareProvider(AppSettings settings, SecretStore secretStore)
        : this(settings, secretStore, new FtpWebRequestUploadClient())
    {
    }

    public FtpFtpsShareProvider(AppSettings settings, SecretStore secretStore, IFtpUploadClient ftpClient)
    {
        _settings = settings;
        _secretStore = secretStore;
        _ftpClient = ftpClient;
    }

    public ShareDestination? Destination => ShareDestination.FtpFtps;
    public string ProviderName => "FTP/FTPS";
    public string AuthType => "Password";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => true;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.FtpHost) ||
            string.IsNullOrWhiteSpace(_settings.FtpUsername))
        {
            return Task.FromResult(new ProviderHealth(false, "FTP/FTPS host and username are not configured."));
        }

        if (!_secretStore.HasFtpPassword)
        {
            return Task.FromResult(new ProviderHealth(false, "FTP/FTPS password is not saved with Windows DPAPI."));
        }

        return Task.FromResult(new ProviderHealth(true, "FTP/FTPS is configured for local upload attempts."));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.FtpHost) ||
            string.IsNullOrWhiteSpace(_settings.FtpUsername))
        {
            return new ShareUploadResult(false, null, "FTP/FTPS upload needs host and username settings.");
        }

        var password = _secretStore.ReadFtpPassword();
        if (string.IsNullOrWhiteSpace(password))
        {
            return new ShareUploadResult(false, null, "FTP/FTPS upload needs a DPAPI-saved password.");
        }

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return new ShareUploadResult(false, null, $"Source file does not exist: {request.FilePath}");
        }

        var requestUri = BuildUploadUri(request);
        var useFtps = _settings.FtpUseFtps ||
            _settings.FtpHost.Trim().StartsWith("ftps://", StringComparison.OrdinalIgnoreCase);
        var uploadRequest = new FtpUploadRequest(
            requestUri,
            _settings.FtpUsername.Trim(),
            password.Trim(),
            useFtps,
            request.FilePath);
        var uploadResult = await _ftpClient.UploadAsync(uploadRequest, cancellationToken);
        var shareUrl = BuildPublicUrl(Path.GetFileName(requestUri.LocalPath));
        if (!string.IsNullOrWhiteSpace(shareUrl))
        {
            ClipboardInterop.SetText(shareUrl);
        }

        var statusDescription = string.IsNullOrWhiteSpace(uploadResult.StatusDescription)
            ? "upload accepted by server"
            : uploadResult.StatusDescription.Trim();

        return new ShareUploadResult(
            true,
            shareUrl,
            string.IsNullOrWhiteSpace(shareUrl)
                ? $"FTP/FTPS upload completed: {statusDescription}"
                : $"FTP/FTPS upload completed and copied URL: {shareUrl}");
    }

    private Uri BuildUploadUri(ShareUploadRequest request)
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
        var fileName = EncodeCloudPathSegment(BuildRemoteFileName(request));
        builder.Path = CombineUriPath(basePath, folder, fileName);
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private string? BuildPublicUrl(string remoteFileName)
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

    private static string BuildRemoteFileName(ShareUploadRequest request)
    {
        var fileName = MetadataValue(request, "fileName", Path.GetFileName(request.FilePath));
        var safeFileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return $"{MetadataValue(request, "id", Guid.NewGuid().ToString("N"))}-{safeFileName}";
    }

    private static string MetadataValue(ShareUploadRequest request, string key, string fallback)
    {
        return request.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
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

    private static string CombineUriPath(params string?[] parts)
    {
        return string.Join(
            "/",
            parts
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim('/')));
    }

    private static string EncodeCloudPathSegment(string value)
    {
        return Uri.EscapeDataString(value)
            .Replace("%2D", "-", StringComparison.OrdinalIgnoreCase)
            .Replace("%2E", ".", StringComparison.OrdinalIgnoreCase)
            .Replace("%5F", "_", StringComparison.OrdinalIgnoreCase)
            .Replace("%7E", "~", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record FtpUploadRequest(
    Uri RequestUri,
    string Username,
    string Password,
    bool EnableSsl,
    string FilePath);

public sealed record FtpUploadResult(string StatusDescription);

public interface IFtpUploadClient
{
    Task<FtpUploadResult> UploadAsync(FtpUploadRequest request, CancellationToken cancellationToken);
}

public sealed class FtpWebRequestUploadClient : IFtpUploadClient
{
#pragma warning disable SYSLIB0014
    public async Task<FtpUploadResult> UploadAsync(FtpUploadRequest request, CancellationToken cancellationToken)
    {
        var webRequest = (FtpWebRequest)WebRequest.Create(request.RequestUri);
        webRequest.Method = WebRequestMethods.Ftp.UploadFile;
        webRequest.Credentials = new NetworkCredential(request.Username, request.Password);
        webRequest.EnableSsl = request.EnableSsl;
        webRequest.UseBinary = true;
        webRequest.KeepAlive = false;

        await using (var requestStream = await webRequest.GetRequestStreamAsync())
        await using (var fileStream = File.OpenRead(request.FilePath))
        {
            await fileStream.CopyToAsync(requestStream, cancellationToken);
        }

        using var response = (FtpWebResponse)await webRequest.GetResponseAsync();
        return new FtpUploadResult(response.StatusDescription ?? string.Empty);
    }
#pragma warning restore SYSLIB0014
}
