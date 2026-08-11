using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GoatShot.App.Services;

public sealed class CompanionPortalHostService
{
    private const int MinimumAccessTokenLength = 16;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly CompanionPortalExportService _exportService;

    public CompanionPortalHostService(CompanionPortalExportService exportService)
    {
        _exportService = exportService;
    }

    public async Task<CompanionPortalHostSession> StartAsync(
        CompanionPortalHostRequest request,
        CancellationToken cancellationToken = default)
    {
        var bind = ResolveBindAddress(request);
        if (!bind.Succeeded)
        {
            return CompanionPortalHostSession.Failed(new CompanionPortalHostResult
            {
                Succeeded = false,
                Message = bind.Message,
                Host = request.Host ?? string.Empty,
                Port = request.Port,
                LoopbackOnly = true,
                RemoteClientsAllowed = false,
                MutatingRoutesEnabled = false,
                WouldContactPortal = false,
                WouldSync = false,
                WouldUpload = false,
                WouldAttachMedia = false,
                WouldReadSecrets = false,
                WouldMutatePolicy = false,
                AuthRequired = !string.IsNullOrWhiteSpace(request.AccessToken),
                AccessControlMode = string.IsNullOrWhiteSpace(request.AccessToken) ? "none" : "shared-token"
            });
        }

        var bindAddress = bind.Address!;
        var remoteClientsAllowed = !IPAddress.IsLoopback(bindAddress);
        var authRequired = !string.IsNullOrWhiteSpace(request.AccessToken);
        var accessControlMode = authRequired ? "shared-token" : "none";
        var exportRequest = new CompanionPortalExportRequest
        {
            OutputPath = request.OutputPath,
            ManualValidationPath = request.ManualValidationPath,
            ProofRootPath = request.ProofRootPath,
            ShareHistoryLimit = request.ShareHistoryLimit,
            HostedPreview = true,
            RemoteClientsAllowed = remoteClientsAllowed,
            AuthRequired = authRequired,
            MediaPaths = request.MediaPaths,
            AcceptMediaCopy = request.AcceptMediaCopy,
            MaxMediaBytes = request.MaxMediaBytes
        };
        var export = await _exportService.ExportAsync(exportRequest, cancellationToken);
        if (!export.Succeeded)
        {
            return CompanionPortalHostSession.Failed(new CompanionPortalHostResult
            {
                Succeeded = false,
                Message = export.Message,
                Host = bindAddress.ToString(),
                Port = request.Port,
                OutputRoot = export.OutputRoot,
                ReportJsonPath = export.ReportJsonPath,
                ReportHtmlPath = export.ReportHtmlPath,
                LoopbackOnly = !remoteClientsAllowed,
                RemoteClientsAllowed = remoteClientsAllowed,
                MutatingRoutesEnabled = false,
                WouldContactPortal = export.Report.Boundary.WouldContactPortal,
                WouldSync = export.Report.Boundary.WouldSync,
                WouldUpload = export.Report.Boundary.WouldUpload,
                WouldAttachMedia = export.Report.Boundary.WouldAttachMedia,
                WouldReadSecrets = export.Report.Boundary.WouldReadSecrets,
                WouldMutatePolicy = export.Report.Boundary.WouldMutatePolicy,
                MediaReviewPagesEnabled = export.Report.Boundary.MediaReviewPagesEnabled,
                MediaReviewItemCount = export.Report.MediaReview.ItemCount,
                AuthRequired = authRequired,
                AccessControlMode = accessControlMode
            });
        }

        var listener = new TcpListener(bindAddress, request.Port);
        listener.Start();
        var actualEndpoint = (IPEndPoint)listener.LocalEndpoint;
        var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var host = FormatPreviewHostForUrl(actualEndpoint.Address);
        var url = $"http://{host}:{actualEndpoint.Port}/";
        var result = new CompanionPortalHostResult
        {
            Succeeded = true,
            Message = remoteClientsAllowed
                ? "Companion portal read-only preview is serving for explicitly accepted remote clients with shared-token access control. Only GET/HEAD for allowlisted endpoints are enabled."
                : authRequired
                    ? "Companion portal read-only preview is serving on loopback with shared-token access control. Only GET/HEAD for allowlisted endpoints are enabled."
                    : "Companion portal read-only preview is serving on loopback. Only GET/HEAD for allowlisted endpoints are enabled.",
            Mode = export.Report.Mode,
            Url = url,
            Host = actualEndpoint.Address.ToString(),
            Port = actualEndpoint.Port,
            OutputRoot = export.OutputRoot,
            ReportJsonPath = export.ReportJsonPath,
            ReportHtmlPath = export.ReportHtmlPath,
            LoopbackOnly = !remoteClientsAllowed,
            RemoteClientsAllowed = remoteClientsAllowed,
            MutatingRoutesEnabled = false,
            WouldContactPortal = export.Report.Boundary.WouldContactPortal,
            WouldSync = export.Report.Boundary.WouldSync,
            WouldUpload = export.Report.Boundary.WouldUpload,
            WouldAttachMedia = export.Report.Boundary.WouldAttachMedia,
            WouldReadSecrets = export.Report.Boundary.WouldReadSecrets,
            WouldMutatePolicy = export.Report.Boundary.WouldMutatePolicy,
            MediaReviewPagesEnabled = export.Report.Boundary.MediaReviewPagesEnabled,
            MediaReviewItemCount = export.Report.MediaReview.ItemCount,
            AuthRequired = authRequired,
            AccessControlMode = accessControlMode
        };
        var loopTask = Task.Run(
            () => AcceptLoopAsync(listener, export.OutputRoot, export.Report, result, request.AccessToken, linkedSource.Token),
            CancellationToken.None);
        return new CompanionPortalHostSession(result, export, listener, linkedSource, loopTask);
    }

    private static async Task AcceptLoopAsync(
        TcpListener listener,
        string outputRoot,
        CompanionPortalExportReport report,
        CompanionPortalHostResult result,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(
                    () => HandleClientAsync(client, outputRoot, report, result, accessToken, cancellationToken),
                    CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static async Task HandleClientAsync(
        TcpClient client,
        string outputRoot,
        CompanionPortalExportReport report,
        CompanionPortalHostResult result,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        using var _ = client;
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return;
        }

        var headers = await ReadHeadersAsync(reader, cancellationToken);

        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await WriteResponseAsync(stream, 400, "Bad Request", "text/plain; charset=utf-8", "Bad request.", headOnly: false, cancellationToken);
            return;
        }

        var method = parts[0].ToUpperInvariant();
        var headOnly = method.Equals("HEAD", StringComparison.OrdinalIgnoreCase);
        if (!method.Equals("GET", StringComparison.OrdinalIgnoreCase) && !headOnly)
        {
            await WriteResponseAsync(
                stream,
                405,
                "Method Not Allowed",
                "text/plain; charset=utf-8",
                "This preview is read-only.",
                headOnly: false,
                cancellationToken,
                "Allow: GET, HEAD\r\n");
            return;
        }

        if (result.AuthRequired && !IsAuthorized(headers, accessToken))
        {
            await WriteResponseAsync(
                stream,
                401,
                "Unauthorized",
                "text/plain; charset=utf-8",
                "Authentication required.",
                headOnly,
                cancellationToken,
                "WWW-Authenticate: Basic realm=\"Receipts Companion Portal\"\r\n");
            return;
        }

        var path = NormalizeRequestPath(parts[1]);
        var filePath = path switch
        {
            "/" or "/index.html" => Path.Combine(outputRoot, CompanionPortalExportService.ReportHtmlFileName),
            "/companion-portal-report.json" => Path.Combine(outputRoot, CompanionPortalExportService.ReportJsonFileName),
            _ => null
        };

        if (path.Equals("/health.json", StringComparison.OrdinalIgnoreCase))
        {
            var healthJson = JsonSerializer.Serialize(new CompanionPortalHostHealth
            {
                Status = "ready",
                Mode = result.Mode,
                Url = result.Url,
                LoopbackOnly = result.LoopbackOnly,
                RemoteClientsAllowed = result.RemoteClientsAllowed,
                MutatingRoutesEnabled = result.MutatingRoutesEnabled,
                WouldContactPortal = result.WouldContactPortal,
                WouldSync = result.WouldSync,
                WouldUpload = result.WouldUpload,
                WouldAttachMedia = result.WouldAttachMedia,
                WouldReadSecrets = result.WouldReadSecrets,
                WouldMutatePolicy = result.WouldMutatePolicy,
                MediaReviewPagesEnabled = result.MediaReviewPagesEnabled,
                MediaReviewItemCount = result.MediaReviewItemCount,
                AuthRequired = result.AuthRequired,
                AccessControlMode = result.AccessControlMode
            }, JsonOptions);
            await WriteResponseAsync(stream, 200, "OK", "application/json; charset=utf-8", healthJson, headOnly, cancellationToken);
            return;
        }

        var staticFile = ResolveStaticFile(path, outputRoot, report);
        filePath ??= staticFile?.FilePath;
        var contentType = staticFile?.ContentType ?? (Path.GetFileName(filePath ?? string.Empty).Equals(CompanionPortalExportService.ReportJsonFileName, StringComparison.OrdinalIgnoreCase)
            ? "application/json; charset=utf-8"
            : "text/html; charset=utf-8");

        if (filePath is null || !File.Exists(filePath))
        {
            await WriteResponseAsync(stream, 404, "Not Found", "text/plain; charset=utf-8", "Not found.", headOnly, cancellationToken);
            return;
        }

        if (staticFile?.Binary == true)
        {
            await WriteBytesResponseAsync(
                stream,
                200,
                "OK",
                contentType,
                await File.ReadAllBytesAsync(filePath, cancellationToken),
                headOnly,
                cancellationToken);
            return;
        }

        await WriteResponseAsync(stream, 200, "OK", contentType, await File.ReadAllTextAsync(filePath, cancellationToken), headOnly, cancellationToken);
    }

    private static CompanionPortalStaticFile? ResolveStaticFile(
        string requestPath,
        string outputRoot,
        CompanionPortalExportReport report)
    {
        if (report.MediaReview.Enabled)
        {
            if (requestPath.Equals("/" + CompanionPortalExportService.MediaReviewHtmlFileName, StringComparison.OrdinalIgnoreCase))
            {
                return TextFile(outputRoot, CompanionPortalExportService.MediaReviewHtmlFileName, "text/html; charset=utf-8");
            }

            if (requestPath.Equals("/" + CompanionPortalExportService.MediaReviewJsonFileName, StringComparison.OrdinalIgnoreCase))
            {
                return TextFile(outputRoot, CompanionPortalExportService.MediaReviewJsonFileName, "application/json; charset=utf-8");
            }

            var requestedRelativePath = requestPath.TrimStart('/');
            var mediaItem = report.MediaReview.Items.FirstOrDefault(item =>
                item.ReviewRelativePath.Equals(requestedRelativePath, StringComparison.OrdinalIgnoreCase));
            if (mediaItem is not null)
            {
                var filePath = Path.GetFullPath(Path.Combine(outputRoot, mediaItem.ReviewRelativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (IsUnderRoot(outputRoot, filePath))
                {
                    return new CompanionPortalStaticFile(filePath, mediaItem.ContentType, Binary: true);
                }
            }
        }

        return null;
    }

    private static CompanionPortalStaticFile TextFile(string outputRoot, string relativePath, string contentType)
    {
        return new CompanionPortalStaticFile(Path.GetFullPath(Path.Combine(outputRoot, relativePath)), contentType, Binary: false);
    }

    private static async Task<Dictionary<string, string>> ReadHeadersAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line))
            {
                return headers;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
    }

    private static bool IsAuthorized(IReadOnlyDictionary<string, string> headers, string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken) ||
            !headers.TryGetValue("Authorization", out var authorization) ||
            string.IsNullOrWhiteSpace(authorization))
        {
            return false;
        }

        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return SecureEquals(authorization["Bearer ".Length..].Trim(), accessToken);
        }

        if (authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(authorization["Basic ".Length..].Trim()));
                var separator = decoded.IndexOf(':');
                var password = separator >= 0 ? decoded[(separator + 1)..] : decoded;
                return SecureEquals(password, accessToken);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool SecureEquals(string actual, string expected)
    {
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return actualBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    private static bool IsUnderRoot(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteResponseAsync(
        Stream stream,
        int statusCode,
        string reason,
        string contentType,
        string body,
        bool headOnly,
        CancellationToken cancellationToken,
        string extraHeaders = "")
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {reason}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {bytes.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "X-Receipts-Portal-Boundary: read-only\r\n" +
            "X-GoatShot-Portal-Boundary: read-only\r\n" +
            extraHeaders +
            "Connection: close\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken);
        if (!headOnly)
        {
            await stream.WriteAsync(bytes, cancellationToken);
        }
    }

    private static async Task WriteBytesResponseAsync(
        Stream stream,
        int statusCode,
        string reason,
        string contentType,
        byte[] body,
        bool headOnly,
        CancellationToken cancellationToken,
        string extraHeaders = "")
    {
        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {reason}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "X-Receipts-Portal-Boundary: read-only\r\n" +
            "X-GoatShot-Portal-Boundary: read-only\r\n" +
            extraHeaders +
            "Connection: close\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken);
        if (!headOnly)
        {
            await stream.WriteAsync(body, cancellationToken);
        }
    }

    private static string NormalizeRequestPath(string target)
    {
        var path = target;
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            path = uri.PathAndQuery;
        }

        var queryIndex = path.IndexOf("?", StringComparison.Ordinal);
        if (queryIndex >= 0)
        {
            path = path[..queryIndex];
        }

        path = Uri.UnescapeDataString(path.Replace('\\', '/'));
        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            path = "/" + path;
        }

        return path.Contains("..", StringComparison.Ordinal) ? "/__blocked__" : path;
    }

    private static CompanionPortalBindResolution ResolveBindAddress(CompanionPortalHostRequest request)
    {
        var host = request.Host;
        if (!string.IsNullOrWhiteSpace(request.AccessToken) &&
            request.AccessToken.Trim().Length < MinimumAccessTokenLength)
        {
            return CompanionPortalBindResolution.Failed($"Companion portal preview requires --auth-token with at least {MinimumAccessTokenLength} characters when shared-token access control is enabled.");
        }

        if (string.IsNullOrWhiteSpace(host) ||
            host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return CompanionPortalBindResolution.Success(IPAddress.Loopback);
        }

        if (host.Equals("::1", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("[::1]", StringComparison.OrdinalIgnoreCase))
        {
            return CompanionPortalBindResolution.Success(IPAddress.IPv6Loopback);
        }

        if (IPAddress.TryParse(host, out var parsedAddress) && IPAddress.IsLoopback(parsedAddress))
        {
            return CompanionPortalBindResolution.Success(parsedAddress);
        }

        if (!request.AllowRemoteClients || !request.AcceptRemoteClients)
        {
            return CompanionPortalBindResolution.Failed("Companion portal preview only supports non-loopback hosts when --self-hosted and --accept-remote-clients are both supplied.");
        }

        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            return CompanionPortalBindResolution.Failed($"Companion portal self-hosted preview requires --auth-token with at least {MinimumAccessTokenLength} characters.");
        }

        if (host.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("*", StringComparison.OrdinalIgnoreCase))
        {
            return CompanionPortalBindResolution.Success(IPAddress.Any);
        }

        if (host.Equals("::", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("[::]", StringComparison.OrdinalIgnoreCase))
        {
            return CompanionPortalBindResolution.Success(IPAddress.IPv6Any);
        }

        return IPAddress.TryParse(host, out var remoteAddress) && !IPAddress.IsLoopback(remoteAddress)
            ? CompanionPortalBindResolution.Success(remoteAddress)
            : CompanionPortalBindResolution.Failed("Companion portal self-hosted preview requires an explicit IP bind host such as 0.0.0.0 or a non-loopback address.");
    }

    private static string FormatPreviewHostForUrl(IPAddress address)
    {
        if (address.Equals(IPAddress.Any))
        {
            return IPAddress.Loopback.ToString();
        }

        if (address.Equals(IPAddress.IPv6Any))
        {
            return $"[{IPAddress.IPv6Loopback}]";
        }

        return FormatHostForUrl(address);
    }

    private static string FormatHostForUrl(IPAddress address)
    {
        return address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{address}]"
            : address.ToString();
    }
}

public sealed class CompanionPortalHostSession : IAsyncDisposable
{
    private readonly TcpListener? _listener;
    private readonly CancellationTokenSource? _cancellation;
    private readonly Task? _loopTask;

    internal CompanionPortalHostSession(
        CompanionPortalHostResult result,
        CompanionPortalExportResult? export,
        TcpListener? listener,
        CancellationTokenSource? cancellation,
        Task? loopTask)
    {
        Result = result;
        Export = export;
        _listener = listener;
        _cancellation = cancellation;
        _loopTask = loopTask;
    }

    public CompanionPortalHostResult Result { get; }
    public CompanionPortalExportResult? Export { get; }

    public static CompanionPortalHostSession Failed(CompanionPortalHostResult result)
    {
        return new CompanionPortalHostSession(result, null, null, null, null);
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation?.Cancel();
        _listener?.Stop();
        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _cancellation?.Dispose();
    }
}

public sealed class CompanionPortalHostRequest
{
    public string? OutputPath { get; set; }
    public string? ManualValidationPath { get; set; }
    public string? ProofRootPath { get; set; }
    public int ShareHistoryLimit { get; set; } = 200;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; }
    public List<string> MediaPaths { get; set; } = new();
    public bool AcceptMediaCopy { get; set; }
    public long MaxMediaBytes { get; set; } = 250L * 1024L * 1024L;
    public bool AllowRemoteClients { get; set; }
    public bool AcceptRemoteClients { get; set; }
    public string? AccessToken { get; set; }
}

public sealed class CompanionPortalHostResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string OutputRoot { get; set; } = string.Empty;
    public string ReportJsonPath { get; set; } = string.Empty;
    public string ReportHtmlPath { get; set; } = string.Empty;
    public bool LoopbackOnly { get; set; }
    public bool RemoteClientsAllowed { get; set; }
    public bool MutatingRoutesEnabled { get; set; }
    public bool WouldContactPortal { get; set; }
    public bool WouldSync { get; set; }
    public bool WouldUpload { get; set; }
    public bool WouldAttachMedia { get; set; }
    public bool WouldReadSecrets { get; set; }
    public bool WouldMutatePolicy { get; set; }
    public bool MediaReviewPagesEnabled { get; set; }
    public int MediaReviewItemCount { get; set; }
    public bool AuthRequired { get; set; }
    public string AccessControlMode { get; set; } = string.Empty;
}

public sealed class CompanionPortalHostHealth
{
    public string Status { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool LoopbackOnly { get; set; }
    public bool RemoteClientsAllowed { get; set; }
    public bool MutatingRoutesEnabled { get; set; }
    public bool WouldContactPortal { get; set; }
    public bool WouldSync { get; set; }
    public bool WouldUpload { get; set; }
    public bool WouldAttachMedia { get; set; }
    public bool WouldReadSecrets { get; set; }
    public bool WouldMutatePolicy { get; set; }
    public bool MediaReviewPagesEnabled { get; set; }
    public int MediaReviewItemCount { get; set; }
    public bool AuthRequired { get; set; }
    public string AccessControlMode { get; set; } = string.Empty;
}

internal sealed record CompanionPortalBindResolution(bool Succeeded, IPAddress? Address, string Message)
{
    public static CompanionPortalBindResolution Success(IPAddress address) => new(true, address, string.Empty);

    public static CompanionPortalBindResolution Failed(string message) => new(false, null, message);
}

internal sealed record CompanionPortalStaticFile(string FilePath, string ContentType, bool Binary);
