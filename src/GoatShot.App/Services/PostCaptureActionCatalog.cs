using GoatShot.App.Models;

namespace GoatShot.App.Services;

/// <summary>One selectable post-capture behavior plus the copy the settings window shows for it.</summary>
public sealed record PostCaptureActionOption(PostCaptureAction Action, string Label, string Description)
{
    /// <summary>The value written to settings.json. Stable across releases; never localize it.</summary>
    public string StorageValue => Action.ToString();
}

/// <summary>
/// Reads <see cref="AppSettings.PostCaptureAction"/>, which is stored as a string like the other
/// enum-shaped settings. Everything here falls back rather than throws: a hand-edited settings file
/// must never be able to stop a capture from completing.
/// </summary>
public static class PostCaptureActionCatalog
{
    public const PostCaptureAction Default = PostCaptureAction.CopyQuietly;

    public static IReadOnlyList<PostCaptureActionOption> Options { get; } =
    [
        new(PostCaptureAction.CopyQuietly,
            "Copy quietly",
            "Copies to the clipboard and saves to the library without opening anything."),
        new(PostCaptureAction.ShowActionsWindow,
            "Show capture actions",
            "Opens the actions window with Open, Edit, Copy, Share, AI, and export."),
        new(PostCaptureAction.OpenEditor,
            "Open the editor",
            "Opens the annotation editor straight onto the new capture.")
    ];

    public static PostCaptureAction Parse(string? value)
    {
        // TryParse happily accepts "7" and returns an undefined member, so IsDefined has to gate it.
        return Enum.TryParse<PostCaptureAction>(value?.Trim(), ignoreCase: true, out var parsed) &&
            Enum.IsDefined(parsed)
            ? parsed
            : Default;
    }

    public static string Normalize(string? value) => Parse(value).ToString();

    public static PostCaptureActionOption Describe(PostCaptureAction action)
    {
        return Options.First(option => option.Action == action);
    }
}
