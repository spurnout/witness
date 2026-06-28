namespace GoatShot.App.Models;

public sealed record AiTextGenerationResult(
    bool Succeeded,
    string? Text,
    string Message,
    string ModelId);
