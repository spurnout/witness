namespace GoatShot.App.Models;

public sealed record AiAudioTranscriptionResult(
    bool Succeeded,
    string? Text,
    string Message,
    string ModelId);
