using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GoatShot.App.Models;

namespace GoatShot.App.Services;

public sealed class GeminiImageProvider : IAiImageProvider
{
    private const long MaxInlineAudioBytes = 20 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AppSettings _settings;
    private readonly AppPaths _paths;
    private readonly SecretStore _secretStore;
    private readonly HttpClient _httpClient;

    public GeminiImageProvider(AppSettings settings, AppPaths paths, SecretStore secretStore)
        : this(settings, paths, secretStore, new HttpClient())
    {
    }

    internal GeminiImageProvider(AppSettings settings, AppPaths paths, SecretStore secretStore, HttpMessageHandler httpMessageHandler)
        : this(settings, paths, secretStore, new HttpClient(httpMessageHandler, disposeHandler: true))
    {
    }

    private GeminiImageProvider(AppSettings settings, AppPaths paths, SecretStore secretStore, HttpClient httpClient)
    {
        _settings = settings;
        _paths = paths;
        _secretStore = secretStore;
        _httpClient = httpClient;
    }

    public string ProviderName => "Gemini";

    public async Task<ProviderHealth> ValidateCredentialsAsync(CancellationToken cancellationToken)
    {
        if (!ManagedPolicyService.IsAiAllowed(_settings, out var policyReason))
        {
            return new ProviderHealth(false, policyReason);
        }

        var apiKey = _secretStore.ReadGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ProviderHealth(false, "No Gemini API key is saved.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}/models");
            request.Headers.Add("x-goog-api-key", apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new ProviderHealth(true, "Gemini credentials validated.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new ProviderHealth(false, FriendlyError(response.StatusCode, body));
        }
        catch (Exception ex)
        {
            return new ProviderHealth(false, $"Gemini credential check failed: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<AiModelDescriptor>> ListModelsAsync(CancellationToken cancellationToken)
    {
        var manifestModels = LoadManifestModels();
        if (!ManagedPolicyService.IsAiAllowed(_settings, out _))
        {
            return manifestModels;
        }

        var apiKey = _secretStore.ReadGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return manifestModels;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}/models");
            request.Headers.Add("x-goog-api-key", apiKey);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return manifestModels;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("models", out var modelsElement) || modelsElement.ValueKind != JsonValueKind.Array)
            {
                return manifestModels;
            }

            var apiModels = new List<AiModelDescriptor>();
            foreach (var model in modelsElement.EnumerateArray())
            {
                var name = model.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
                var id = name.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? name["models/".Length..] : name;
                var displayName = model.TryGetProperty("displayName", out var displayNameElement)
                    ? displayNameElement.GetString() ?? id
                    : id;

                var supportsGenerateContent = model.TryGetProperty("supportedGenerationMethods", out var methods) &&
                    methods.ValueKind == JsonValueKind.Array &&
                    methods.EnumerateArray().Any(method => (method.GetString() ?? string.Empty).Contains("generateContent", StringComparison.OrdinalIgnoreCase));

                if (!supportsGenerateContent)
                {
                    continue;
                }

                var looksImageCapable = id.Contains("image", StringComparison.OrdinalIgnoreCase) ||
                    displayName.Contains("image", StringComparison.OrdinalIgnoreCase);

                apiModels.Add(new AiModelDescriptor(
                    id,
                    displayName,
                    looksImageCapable,
                    true,
                    looksImageCapable ? "Gemini Model API" : "Gemini Model API; image capability not advertised"));
            }

            return MergeModels(apiModels, manifestModels);
        }
        catch
        {
            return manifestModels;
        }
    }

    public async Task<AiImageEditResult> EditImageAsync(AiImageEditRequest request, CancellationToken cancellationToken)
    {
        if (!ManagedPolicyService.IsAiAllowed(_settings, out var policyReason))
        {
            return new AiImageEditResult(false, null, policyReason);
        }

        var apiKey = _secretStore.ReadGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiImageEditResult(false, null, "No Gemini API key is saved.");
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return new AiImageEditResult(false, null, "Gemini edit prompt is required.");
        }

        if (!File.Exists(request.ImagePath))
        {
            return new AiImageEditResult(false, null, $"Image not found: {request.ImagePath}");
        }

        var modelId = string.IsNullOrWhiteSpace(request.ModelId) ? _settings.GeminiDefaultModelId : request.ModelId;
        var bytes = await File.ReadAllBytesAsync(request.ImagePath, cancellationToken);
        var mime = MimeTypeFor(request.ImagePath);
        var payload = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { inline_data = new { mime_type = mime, data = Convert.ToBase64String(bytes) } },
                        new { text = request.Prompt }
                    }
                }
            },
            generationConfig = new
            {
                responseModalities = new[] { "TEXT", "IMAGE" }
            }
        };

        var url = $"{Endpoint}/models/{Uri.EscapeDataString(modelId)}:generateContent";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Add("x-goog-api-key", apiKey);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new AiImageEditResult(false, null, FriendlyError(response.StatusCode, body));
            }

            var image = ExtractFirstInlineImage(body);
            if (image is null)
            {
                var text = ExtractFirstText(body);
                return new AiImageEditResult(false, null, string.IsNullOrWhiteSpace(text)
                    ? "Gemini response did not include an edited image."
                    : $"Gemini returned text but no image: {text}");
            }

            Directory.CreateDirectory(_paths.AiOutputRoot);
            var extension = ExtensionForMimeType(image.Value.MimeType);
            var outputPath = Path.Combine(
                _paths.AiOutputRoot,
                $"{Path.GetFileNameWithoutExtension(request.ImagePath)}-gemini-{DateTimeOffset.Now:yyyyMMdd-HHmmss}{extension}");
            await File.WriteAllBytesAsync(outputPath, image.Value.Bytes, cancellationToken);
            return new AiImageEditResult(true, outputPath, $"Gemini edited image saved: {outputPath}");
        }
        catch (Exception ex)
        {
            return new AiImageEditResult(false, null, $"Gemini edit failed: {ex.Message}");
        }
    }

    public async Task<AiImageAnalysisResult> AnalyzeImageAsync(string imagePath, string prompt, string modelId, CancellationToken cancellationToken)
    {
        if (!ManagedPolicyService.IsAiAllowed(_settings, out var policyReason))
        {
            return new AiImageAnalysisResult(false, null, policyReason);
        }

        var apiKey = _secretStore.ReadGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiImageAnalysisResult(false, null, "No Gemini API key is saved.");
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new AiImageAnalysisResult(false, null, "Gemini analysis prompt is required.");
        }

        if (!File.Exists(imagePath))
        {
            return new AiImageAnalysisResult(false, null, $"Image not found: {imagePath}");
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new object[]
                        {
                            new { inline_data = new { mime_type = MimeTypeFor(imagePath), data = Convert.ToBase64String(bytes) } },
                            new { text = prompt }
                        }
                    }
                }
            };

            var selectedModel = string.IsNullOrWhiteSpace(modelId) ? _settings.GeminiDefaultModelId : modelId;
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint}/models/{Uri.EscapeDataString(selectedModel)}:generateContent");
            httpRequest.Headers.Add("x-goog-api-key", apiKey);
            httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new AiImageAnalysisResult(false, null, FriendlyError(response.StatusCode, body));
            }

            var text = ExtractFirstText(body);
            return string.IsNullOrWhiteSpace(text)
                ? new AiImageAnalysisResult(false, null, "Gemini analysis completed without text output.")
                : new AiImageAnalysisResult(true, text, "Gemini analysis completed.");
        }
        catch (Exception ex)
        {
            return new AiImageAnalysisResult(false, null, $"Gemini analysis failed: {ex.Message}");
        }
    }

    public async Task<AiTextGenerationResult> GenerateTextAsync(
        string prompt,
        string modelId,
        CancellationToken cancellationToken)
    {
        var selectedModel = string.IsNullOrWhiteSpace(modelId) ? _settings.GeminiDefaultModelId : modelId.Trim();
        if (!ManagedPolicyService.IsAiAllowed(_settings, out var policyReason))
        {
            return new AiTextGenerationResult(false, null, policyReason, selectedModel);
        }

        var apiKey = _secretStore.ReadGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiTextGenerationResult(false, null, "No Gemini API key is saved.", selectedModel);
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new AiTextGenerationResult(false, null, "Gemini text prompt is required.", selectedModel);
        }

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint}/models/{Uri.EscapeDataString(selectedModel)}:generateContent");
        httpRequest.Headers.Add("x-goog-api-key", apiKey);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new AiTextGenerationResult(false, null, FriendlyError(response.StatusCode, body), selectedModel);
            }

            var text = ExtractFirstText(body);
            return string.IsNullOrWhiteSpace(text)
                ? new AiTextGenerationResult(false, null, "Gemini text generation completed without text output.", selectedModel)
                : new AiTextGenerationResult(true, text, "Gemini text generation completed.", selectedModel);
        }
        catch (Exception ex)
        {
            return new AiTextGenerationResult(false, null, $"Gemini text generation failed: {ex.Message}", selectedModel);
        }
    }

    public async Task<AiAudioTranscriptionResult> TranscribeAudioAsync(
        string audioPath,
        string prompt,
        string modelId,
        CancellationToken cancellationToken)
    {
        var selectedModel = string.IsNullOrWhiteSpace(modelId)
            ? _settings.GeminiSpeechToTextModelId
            : modelId.Trim();
        if (string.IsNullOrWhiteSpace(selectedModel))
        {
            selectedModel = _settings.GeminiDefaultModelId;
        }

        if (!ManagedPolicyService.IsAiAllowed(_settings, out var policyReason))
        {
            return new AiAudioTranscriptionResult(false, null, policyReason, selectedModel);
        }

        var apiKey = _secretStore.ReadGeminiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiAudioTranscriptionResult(false, null, "No Gemini API key is saved.", selectedModel);
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new AiAudioTranscriptionResult(false, null, "Gemini audio transcription prompt is required.", selectedModel);
        }

        if (!File.Exists(audioPath))
        {
            return new AiAudioTranscriptionResult(false, null, $"Audio file not found: {audioPath}", selectedModel);
        }

        var info = new FileInfo(audioPath);
        if (info.Length > MaxInlineAudioBytes)
        {
            return new AiAudioTranscriptionResult(
                false,
                null,
                $"Audio is {info.Length} bytes, which exceeds Receipts' current {MaxInlineAudioBytes} byte inline Gemini transcription limit. Use supplied subtitles or an explicitly configured external Whisper installation for longer recordings.",
                selectedModel);
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(audioPath, cancellationToken);
            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new object[]
                        {
                            new { inline_data = new { mime_type = MimeTypeForAudio(audioPath), data = Convert.ToBase64String(bytes) } },
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    responseMimeType = "application/json"
                }
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint}/models/{Uri.EscapeDataString(selectedModel)}:generateContent");
            httpRequest.Headers.Add("x-goog-api-key", apiKey);
            httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new AiAudioTranscriptionResult(false, null, FriendlyError(response.StatusCode, body), selectedModel);
            }

            var text = ExtractFirstText(body);
            return string.IsNullOrWhiteSpace(text)
                ? new AiAudioTranscriptionResult(false, null, "Gemini audio transcription completed without text output.", selectedModel)
                : new AiAudioTranscriptionResult(true, text, "Gemini audio transcription completed.", selectedModel);
        }
        catch (Exception ex)
        {
            return new AiAudioTranscriptionResult(false, null, $"Gemini audio transcription failed: {ex.Message}", selectedModel);
        }
    }

    private Uri Endpoint => new((_settings.GeminiApiEndpoint.TrimEnd('/')));

    private IReadOnlyList<AiModelDescriptor> LoadManifestModels()
    {
        if (!string.IsNullOrWhiteSpace(_settings.GeminiModelManifestPath) && File.Exists(_settings.GeminiModelManifestPath))
        {
            try
            {
                var json = File.ReadAllText(_settings.GeminiModelManifestPath);
                var models = JsonSerializer.Deserialize<List<AiModelDescriptor>>(json, JsonOptions);
                if (models is { Count: > 0 })
                {
                    return models;
                }
            }
            catch
            {
                // Fall back to built-in current public image models.
            }
        }

        return new[]
        {
            new AiModelDescriptor("gemini-3.1-flash-image", "Gemini 3.1 Flash Image", true, true, "Built-in manifest from Google AI image-generation docs checked 2026-06-12"),
            new AiModelDescriptor("gemini-3-pro-image", "Gemini 3 Pro Image", true, true, "Built-in manifest from Google AI image-generation docs checked 2026-06-12"),
            new AiModelDescriptor("gemini-2.5-flash-image", "Gemini 2.5 Flash Image", true, true, "Built-in manifest from Google AI image-generation docs checked 2026-06-12")
        };
    }

    private static IReadOnlyList<AiModelDescriptor> MergeModels(
        IReadOnlyList<AiModelDescriptor> apiModels,
        IReadOnlyList<AiModelDescriptor> manifestModels)
    {
        var merged = new Dictionary<string, AiModelDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in manifestModels)
        {
            merged[model.Id] = model;
        }

        foreach (var model in apiModels)
        {
            merged[model.Id] = model;
        }

        return merged.Values
            .OrderByDescending(model => model.SupportsImageEdit)
            .ThenBy(model => model.Id)
            .ToList();
    }

    private static (string MimeType, byte[] Bytes)? ExtractFirstInlineImage(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in parts.EnumerateArray())
            {
                var inline = TryGetProperty(part, "inline_data") ?? TryGetProperty(part, "inlineData");
                if (inline is not { } inlineData)
                {
                    continue;
                }

                var mime = (TryGetProperty(inlineData, "mime_type") ?? TryGetProperty(inlineData, "mimeType"))?.GetString() ?? "image/png";
                var data = TryGetProperty(inlineData, "data")?.GetString();
                if (string.IsNullOrWhiteSpace(data))
                {
                    continue;
                }

                return (mime, Convert.FromBase64String(data));
            }
        }

        return null;
    }

    private static string? ExtractFirstText(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return candidates.EnumerateArray()
            .SelectMany(candidate => candidate.TryGetProperty("content", out var content) &&
                                     content.TryGetProperty("parts", out var parts) &&
                                     parts.ValueKind == JsonValueKind.Array
                ? parts.EnumerateArray()
                : Enumerable.Empty<JsonElement>())
            .Select(part => part.TryGetProperty("text", out var text) ? text.GetString() : null)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
    }

    private static JsonElement? TryGetProperty(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var property) ? property : null;
    }

    private static string MimeTypeFor(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => "image/png"
        };
    }

    private static string MimeTypeForAudio(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".mp4" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".oga" => "audio/ogg",
            ".flac" => "audio/flac",
            ".aac" => "audio/aac",
            _ => "application/octet-stream"
        };
    }

    private static string ExtensionForMimeType(string mimeType)
    {
        return mimeType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            _ => ".png"
        };
    }

    private static string FriendlyError(System.Net.HttpStatusCode statusCode, string body)
    {
        var code = (int)statusCode;
        var lower = body.ToLowerInvariant();
        var hint = code switch
        {
            400 => "The request was rejected. Check model ID, image format, and prompt.",
            401 or 403 => "The Gemini API key is invalid, blocked, lacks permission, or billing/quota access is unavailable.",
            404 => "The selected Gemini model was not found. Refresh the model manifest/list.",
            429 => "Gemini quota or rate limit was reached.",
            >= 500 => "Gemini service returned a server error.",
            _ when lower.Contains("quota") => "Gemini quota or billing may need attention.",
            _ when lower.Contains("api key") => "Gemini API key appears invalid or missing permission.",
            _ => "Gemini request failed."
        };

        return $"{hint} HTTP {code}.";
    }
}
