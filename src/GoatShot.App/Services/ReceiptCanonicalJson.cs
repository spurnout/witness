using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public static class ReceiptCanonicalJson
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.Default,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };

    public static byte[] Serialize(ReceiptManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return SerializeValue(manifest);
    }

    public static string SerializeToString(ReceiptManifest manifest) =>
        Encoding.UTF8.GetString(Serialize(manifest));

    public static byte[] SerializeForSignature(ReceiptManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var serialized = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        using var document = JsonDocument.Parse(serialized);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false
        }))
        {
            WriteElement(
                writer,
                document.RootElement,
                omittedRootProperty: null,
                omitSignatureValue: true);
        }

        return stream.ToArray();
    }

    public static ReceiptManifest Deserialize(ReadOnlySpan<byte> json)
    {
        var manifest = JsonSerializer.Deserialize<ReceiptManifest>(json, JsonOptions);
        return manifest ?? throw new JsonException("Receipt manifest JSON was empty.");
    }

    internal static byte[] SerializeValue<T>(T value)
    {
        var serialized = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        using var document = JsonDocument.Parse(serialized);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false
        }))
        {
            WriteElement(writer, document.RootElement, omittedRootProperty: null);
        }

        return stream.ToArray();
    }

    private static void WriteElement(
        Utf8JsonWriter writer,
        JsonElement element,
        string? omittedRootProperty,
        bool isRoot = true,
        bool isSignatureObject = false,
        bool omitSignatureValue = false)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element
                             .EnumerateObject()
                             .Where(property => !isRoot || !property.Name.Equals(omittedRootProperty, StringComparison.Ordinal))
                             .Where(property => !omitSignatureValue ||
                                                !isSignatureObject ||
                                                !property.Name.Equals("signatureBase64", StringComparison.Ordinal))
                             .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteElement(
                        writer,
                        property.Value,
                        omittedRootProperty,
                        isRoot: false,
                        isSignatureObject: isRoot && property.Name.Equals("signature", StringComparison.Ordinal),
                        omitSignatureValue: omitSignatureValue);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(
                        writer,
                        item,
                        omittedRootProperty,
                        isRoot: false,
                        omitSignatureValue: omitSignatureValue);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                WriteNumber(writer, element);
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;

            default:
                throw new JsonException($"Unsupported JSON token '{element.ValueKind}' in receipt manifest.");
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.TryGetInt64(out var signedInteger))
        {
            writer.WriteNumberValue(signedInteger);
            return;
        }

        if (element.TryGetUInt64(out var unsignedInteger))
        {
            writer.WriteNumberValue(unsignedInteger);
            return;
        }

        if (element.TryGetDecimal(out var decimalValue))
        {
            writer.WriteNumberValue(decimalValue);
            return;
        }

        if (element.TryGetDouble(out var doubleValue) && double.IsFinite(doubleValue))
        {
            writer.WriteNumberValue(doubleValue);
            return;
        }

        throw new JsonException("Receipt manifest contained a non-finite or unsupported number.");
    }
}
