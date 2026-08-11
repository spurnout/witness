using System.Net;
using System.Net.Sockets;
using System.Text;

namespace GoatShot.App.Services;

public sealed class OAuthCallbackServerService
{
    public OAuthCallbackSession Start(string expectedState, int preferredPort, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(expectedState))
        {
            throw new ArgumentException("OAuth state is required.", nameof(expectedState));
        }

        preferredPort = preferredPort is > 0 and <= 65_535 ? preferredPort : 0;
        var listener = new TcpListener(IPAddress.Loopback, preferredPort);
        listener.Start(8);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return new OAuthCallbackSession(
            listener,
            expectedState,
            new Uri($"http://127.0.0.1:{port}/oauth/callback"),
            timeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(3) : timeout,
            cancellationToken);
    }
}

public sealed class OAuthCallbackSession : IAsyncDisposable
{
    private const int MaxHeaderBytes = 16 * 1024;
    private static readonly TimeSpan ClientReadTimeout = TimeSpan.FromSeconds(5);

    private readonly TcpListener _listener;
    private readonly string _expectedState;
    private readonly TimeSpan _timeout;
    private readonly CancellationToken _cancellationToken;
    private Task<OAuthCallbackResult>? _waitTask;

    internal OAuthCallbackSession(
        TcpListener listener,
        string expectedState,
        Uri callbackUri,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        _listener = listener;
        _expectedState = expectedState;
        CallbackUri = callbackUri;
        _timeout = timeout;
        _cancellationToken = cancellationToken;
    }

    public Uri CallbackUri { get; }

    public Task<OAuthCallbackResult> WaitForCallbackAsync()
    {
        _waitTask ??= WaitForCallbackCoreAsync();
        return _waitTask;
    }

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }

    private async Task<OAuthCallbackResult> WaitForCallbackCoreAsync()
    {
        using var timeout = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, _cancellationToken);

        try
        {
            while (true)
            {
                using var client = await _listener.AcceptTcpClientAsync(linked.Token);
                await using var stream = client.GetStream();
                using var clientTimeout = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                clientTimeout.CancelAfter(ClientReadTimeout);
                try
                {
                    var request = await ReadHttpRequestAsync(stream, clientTimeout.Token);
                    var result = ParseRequest(request);
                    await WriteResponseAsync(stream, result, clientTimeout.Token);
                    if (result.Succeeded || string.Equals(result.State, _expectedState, StringComparison.Ordinal))
                    {
                        return result;
                    }
                }
                catch (OperationCanceledException) when (!linked.IsCancellationRequested)
                {
                    // Ignore a slow or incomplete client and keep waiting for the provider callback.
                }
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return new OAuthCallbackResult(false, null, null, "OAuth callback timed out before the browser returned a code.");
        }
        catch (OperationCanceledException)
        {
            return new OAuthCallbackResult(false, null, null, "OAuth callback was canceled.");
        }
        catch (Exception ex)
        {
            return new OAuthCallbackResult(false, null, null, $"OAuth callback failed: {ex.Message}");
        }
        finally
        {
            _listener.Stop();
        }
    }

    private async Task<string> ReadHttpRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxHeaderBytes];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            if (text.Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                return text;
            }
        }

        return Encoding.ASCII.GetString(buffer, 0, total);
    }

    private OAuthCallbackResult ParseRequest(string request)
    {
        var firstLine = request.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).FirstOrDefault() ?? string.Empty;
        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[0].Equals("GET", StringComparison.OrdinalIgnoreCase))
        {
            return new OAuthCallbackResult(false, null, null, "OAuth callback received an unsupported HTTP request.");
        }

        if (!Uri.TryCreate($"http://127.0.0.1{parts[1]}", UriKind.Absolute, out var uri))
        {
            return new OAuthCallbackResult(false, null, null, "OAuth callback request URL could not be parsed.");
        }

        if (!uri.AbsolutePath.TrimEnd('/').Equals("/oauth/callback", StringComparison.OrdinalIgnoreCase))
        {
            return new OAuthCallbackResult(false, null, null, "OAuth callback path was not recognized.");
        }

        var query = ParseQuery(uri.Query);
        if (query.TryGetValue("error", out var error))
        {
            query.TryGetValue("error_description", out var description);
            return new OAuthCallbackResult(false, null, query.GetValueOrDefault("state"), $"OAuth provider returned an error: {error} {description}".Trim());
        }

        query.TryGetValue("state", out var state);
        if (!string.Equals(state, _expectedState, StringComparison.Ordinal))
        {
            return new OAuthCallbackResult(false, null, state, "OAuth callback state did not match the expected value.");
        }

        query.TryGetValue("code", out var code);
        if (string.IsNullOrWhiteSpace(code))
        {
            return new OAuthCallbackResult(false, null, state, "OAuth callback did not include an authorization code.");
        }

        return new OAuthCallbackResult(true, code, state, "OAuth callback received an authorization code.");
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('=', 2);
            var key = DecodeQueryPart(pieces[0]);
            var value = pieces.Length == 2 ? DecodeQueryPart(pieces[1]) : string.Empty;
            if (!string.IsNullOrWhiteSpace(key))
            {
                values[key] = value;
            }
        }

        return values;
    }

    private static string DecodeQueryPart(string value)
    {
        return Uri.UnescapeDataString(value.Replace('+', ' '));
    }

    private static async Task WriteResponseAsync(NetworkStream stream, OAuthCallbackResult result, CancellationToken cancellationToken)
    {
        var status = result.Succeeded ? "200 OK" : "400 Bad Request";
        var title = result.Succeeded ? "Receipts authorization complete" : "Receipts authorization failed";
        var body = "<!doctype html><html><head><meta charset=\"utf-8\"><title>" +
            WebUtility.HtmlEncode(title) +
            "</title></head><body><h1>" +
            WebUtility.HtmlEncode(title) +
            "</h1><p>" +
            WebUtility.HtmlEncode(result.Message) +
            "</p><p>You can close this browser tab and return to Receipts.</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(body);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");

        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
    }
}
