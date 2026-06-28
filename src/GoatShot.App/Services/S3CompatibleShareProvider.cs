using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class S3CompatibleShareProvider : IShareProvider
{
    private readonly AppSettings _settings;
    private readonly SecretStore _secretStore;
    private readonly HttpClient _httpClient;

    public S3CompatibleShareProvider(AppSettings settings, SecretStore secretStore)
        : this(settings, secretStore, new HttpClient())
    {
    }

    public S3CompatibleShareProvider(AppSettings settings, SecretStore secretStore, HttpClient httpClient)
    {
        _settings = settings;
        _secretStore = secretStore;
        _httpClient = httpClient;
    }

    public ShareDestination? Destination => ShareDestination.S3Compatible;
    public string ProviderName => "S3-compatible";
    public string AuthType => "Access key";
    public bool IsImplemented => true;
    public bool SupportsPublicLinks => true;
    public bool SupportsPrivateLinks => true;
    public bool SupportsExpiration => false;
    public bool SupportsPassword => false;

    public Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.S3Endpoint) ||
            string.IsNullOrWhiteSpace(_settings.S3Bucket))
        {
            return Task.FromResult(new ProviderHealth(false, "S3-compatible endpoint and bucket are not configured."));
        }

        if (_secretStore.ReadS3Credentials() is null)
        {
            return Task.FromResult(new ProviderHealth(false, "S3-compatible DPAPI-saved access key credentials are missing."));
        }

        return Task.FromResult(new ProviderHealth(true, "S3-compatible upload is configured for local attempts."));
    }

    public async Task<ShareUploadResult> UploadAsync(ShareUploadRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(_settings.S3Endpoint) ||
            string.IsNullOrWhiteSpace(_settings.S3Bucket))
        {
            return new ShareUploadResult(false, null, "S3-compatible upload needs endpoint and bucket settings.");
        }

        if (string.IsNullOrWhiteSpace(request.FilePath) || !File.Exists(request.FilePath))
        {
            return new ShareUploadResult(false, null, $"Source file does not exist: {request.FilePath}");
        }

        var credentials = _secretStore.ReadS3Credentials();
        if (credentials is null)
        {
            return new ShareUploadResult(false, null, "S3-compatible upload needs DPAPI-saved access key credentials.");
        }

        var endpointUri = new Uri(NormalizeHttpBaseUrl(_settings.S3Endpoint), UriKind.Absolute);
        var bucket = _settings.S3Bucket.Trim().Trim('/');
        var region = string.IsNullOrWhiteSpace(_settings.S3Region)
            ? "us-east-1"
            : _settings.S3Region.Trim();
        var key = BuildObjectKey(request);
        var requestUri = BuildRequestUri(endpointUri, bucket, key);
        var payloadHash = await ComputeFileSha256Async(request.FilePath, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var date = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var host = requestUri.IsDefaultPort ? requestUri.Host : $"{requestUri.Host}:{requestUri.Port}";

        await using var fileStream = File.OpenRead(request.FilePath);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = new StreamContent(fileStream)
        };
        httpRequest.Headers.Host = host;
        httpRequest.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        httpRequest.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "AWS4-HMAC-SHA256",
            BuildAuthorization(
                credentials,
                region,
                date,
                amzDate,
                host,
                requestUri.AbsolutePath,
                payloadHash));

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var shareUrl = response.IsSuccessStatusCode ? BuildPublicUrl(key) : null;
        if (!string.IsNullOrWhiteSpace(shareUrl))
        {
            ClipboardInterop.SetText(shareUrl);
        }

        return new ShareUploadResult(
            response.IsSuccessStatusCode,
            shareUrl,
            response.IsSuccessStatusCode
                ? string.IsNullOrWhiteSpace(shareUrl)
                    ? $"S3-compatible upload completed: s3://{bucket}/{key}"
                    : $"S3-compatible upload completed and copied URL: {shareUrl}"
                : $"S3-compatible upload failed: {(int)response.StatusCode} {response.ReasonPhrase} {body}");
    }

    private string BuildObjectKey(ShareUploadRequest request)
    {
        var prefix = (_settings.S3KeyPrefix ?? string.Empty)
            .Replace('\\', '/')
            .Trim('/');
        var fileName = string.Join("_", FileName(request).Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        var objectName = $"{MetadataValue(request, "id", Guid.NewGuid().ToString("N"))}-{fileName}";
        return string.IsNullOrWhiteSpace(prefix)
            ? objectName
            : $"{prefix}/{objectName}";
    }

    private static Uri BuildRequestUri(Uri endpoint, string bucket, string key)
    {
        var builder = new UriBuilder(endpoint);
        var basePath = builder.Path.Trim('/');
        builder.Path = string.IsNullOrWhiteSpace(basePath)
            ? $"{EncodeKey(bucket)}/{EncodeKey(key)}"
            : $"{EncodeKey(basePath)}/{EncodeKey(bucket)}/{EncodeKey(key)}";
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private string? BuildPublicUrl(string key)
    {
        if (string.IsNullOrWhiteSpace(_settings.S3PublicBaseUrl))
        {
            return null;
        }

        var builder = new UriBuilder(NormalizeHttpBaseUrl(_settings.S3PublicBaseUrl));
        var basePath = builder.Path.Trim('/');
        builder.Path = string.IsNullOrWhiteSpace(basePath)
            ? EncodeKey(key)
            : $"{EncodeKey(basePath)}/{EncodeKey(key)}";
        builder.Query = string.Empty;
        return builder.Uri.ToString();
    }

    private static string BuildAuthorization(
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
            GetSigningKey(credentials.SecretAccessKey, date, region),
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

    private static byte[] GetSigningKey(string secretAccessKey, string date, string region)
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

    private static string EncodeKey(string key)
    {
        return string.Join("/", key.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(EncodePathSegment));
    }

    private static string EncodePathSegment(string segment)
    {
        return Uri.EscapeDataString(segment)
            .Replace("%2D", "-", StringComparison.OrdinalIgnoreCase)
            .Replace("%2E", ".", StringComparison.OrdinalIgnoreCase)
            .Replace("%5F", "_", StringComparison.OrdinalIgnoreCase)
            .Replace("%7E", "~", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHttpBaseUrl(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"https://{trimmed}";
    }

    private static string FileName(ShareUploadRequest request)
    {
        return MetadataValue(request, "fileName", Path.GetFileName(request.FilePath));
    }

    private static string MetadataValue(ShareUploadRequest request, string key, string fallback)
    {
        return request.Metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }
}
