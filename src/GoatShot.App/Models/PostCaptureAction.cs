namespace GoatShot.App.Models;

/// <summary>What the app does with a capture once it has been saved to the workspace.</summary>
public enum PostCaptureAction
{
    /// <summary>Copy and save with nothing on screen. The quick-screenshot default.</summary>
    CopyQuietly,

    /// <summary>Open the capture actions window with Open, Edit, Copy, Share, AI, and export.</summary>
    ShowActionsWindow,

    /// <summary>Open the annotation editor straight onto the new capture.</summary>
    OpenEditor
}
