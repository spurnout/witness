using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class BrowserNativeMessagingHostRunner
{
    public const int DefaultMaximumMessageBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly BrowserExtensionNativeBridgeService _bridge;
    private readonly int _maximumMessageBytes;

    public BrowserNativeMessagingHostRunner(
        BrowserExtensionNativeBridgeService bridge,
        int maximumMessageBytes = DefaultMaximumMessageBytes)
    {
        _bridge = bridge;
        _maximumMessageBytes = maximumMessageBytes;
    }

    public async Task<int> RunOnceAsync(
        Stream input,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        BrowserNativeHostMessageResult result;
        try
        {
            var message = await ReadMessageAsync(input, cancellationToken);
            result = await HandleMessageAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or ArgumentException)
        {
            result = new BrowserNativeHostMessageResult
            {
                Succeeded = false,
                Message = SensitiveTextDetector.Redact(ex.Message)
            };
        }

        await WriteMessageAsync(output, result, cancellationToken);
        return result.Succeeded ? 0 : 1;
    }

    public async Task<BrowserNativeHostMessageResult> HandleMessageAsync(
        string messageJson,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageJson))
        {
            return new BrowserNativeHostMessageResult
            {
                Succeeded = false,
                Message = "Native messaging payload was empty."
            };
        }

        using var document = JsonDocument.Parse(messageJson);
        var root = document.RootElement;
        if (root.TryGetProperty("type", out var type) &&
            type.ValueKind == JsonValueKind.String &&
            type.GetString()!.Equals("GOATSHOT_PING", StringComparison.Ordinal))
        {
            return new BrowserNativeHostMessageResult
            {
                Succeeded = true,
                Message = "Receipts native host is reachable.",
                NativeHostVersion = typeof(BrowserNativeMessagingHostRunner).Assembly.GetName().Version?.ToString() ?? string.Empty
            };
        }

        var payloadElement = root.TryGetProperty("payload", out var nestedPayload)
            ? nestedPayload
            : root;
        var payload = payloadElement.Deserialize<BrowserExtensionCapturePayload>(JsonOptions);
        var screenshotPath = root.TryGetProperty("screenshotPath", out var screenshot)
            ? screenshot.GetString()
            : null;
        var stitchPackagePath = root.TryGetProperty("stitchPackagePath", out var stitchPackage)
            ? stitchPackage.GetString()
            : null;

        var bridgeResult = await _bridge.AcceptPayloadAsync(
            payload,
            screenshotPath,
            stitchPackagePath: stitchPackagePath,
            confineImportsToBridgeRoot: true,
            cancellationToken: cancellationToken);
        return new BrowserNativeHostMessageResult
        {
            Succeeded = bridgeResult.Succeeded,
            Message = bridgeResult.Message,
            RedactedPayloadPath = bridgeResult.RedactedPayloadPath,
            StitchPackagePath = bridgeResult.StitchPackagePath,
            WorkspaceFilePath = bridgeResult.Item?.FilePath,
            Issues = bridgeResult.Issues,
            Warnings = bridgeResult.Warnings
        };
    }

    public static async Task WriteMessageAsync<T>(
        Stream output,
        T message,
        CancellationToken cancellationToken = default)
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, JsonOptions));
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException("Browser native messaging length headers require little-endian encoding.");
        }

        var header = BitConverter.GetBytes(payload.Length);
        await output.WriteAsync(header, cancellationToken);
        await output.WriteAsync(payload, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private async Task<string> ReadMessageAsync(Stream input, CancellationToken cancellationToken)
    {
        var header = await ReadExactAsync(input, 4, cancellationToken);
        if (header.Length == 0)
        {
            throw new InvalidOperationException("No browser native messaging payload was received.");
        }

        if (header.Length < 4)
        {
            throw new InvalidOperationException("Browser native messaging length header was incomplete.");
        }

        var length = BitConverter.ToInt32(header, 0);
        if (length <= 0 || length > _maximumMessageBytes)
        {
            throw new InvalidOperationException($"Browser native messaging payload length is outside the supported range: {length}.");
        }

        var payload = await ReadExactAsync(input, length, cancellationToken);
        if (payload.Length != length)
        {
            throw new InvalidOperationException("Browser native messaging payload was incomplete.");
        }

        return Encoding.UTF8.GetString(payload);
    }

    private static async Task<byte[]> ReadExactAsync(
        Stream input,
        int count,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await input.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return offset == count ? buffer : buffer.Take(offset).ToArray();
    }
}

public sealed class BrowserNativeHostMessageResult
{
    public bool Succeeded { get; set; }
    public string Message { get; set; } = string.Empty;
    public string NativeHostVersion { get; set; } = string.Empty;
    public string RedactedPayloadPath { get; set; } = string.Empty;
    public string StitchPackagePath { get; set; } = string.Empty;
    public string? WorkspaceFilePath { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
