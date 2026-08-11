namespace GoatShot.App.Models;

public enum ReceiptOcrChangeKind
{
    PossibleAddition,
    PossibleEdit,
    PossibleDeletion
}

public enum ReceiptChangeReviewState
{
    Pending,
    Confirmed,
    Dismissed
}

public sealed record ReceiptAnalysisOptions(
    bool EnableSceneIndexing,
    bool EnableOcrComparison,
    double Sensitivity = 0.65d)
{
    public ReceiptAnalysisOptions Normalize() => this with
    {
        Sensitivity = Math.Clamp(Sensitivity, 0.05d, 1d)
    };
}

public sealed class ReceiptLocalAnalysis
{
    public string Schema { get; set; } = "receipts.local-analysis.v1";
    public string ReceiptId { get; set; } = string.Empty;
    public DateTimeOffset AnalyzedAtUtc { get; set; }
    public double Sensitivity { get; set; }
    public bool SceneIndexingEnabled { get; set; }
    public bool OcrComparisonEnabled { get; set; }
    public List<ReceiptSceneMarker> Scenes { get; set; } = [];
    public List<ReceiptOcrFrame> Frames { get; set; } = [];
    public List<ReceiptOcrChange> Changes { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public sealed class ReceiptSceneMarker
{
    public string SceneId { get; set; } = Guid.NewGuid().ToString("N");
    public string TrackId { get; set; } = string.Empty;
    public string SegmentId { get; set; } = string.Empty;
    public long MonotonicTicks { get; set; }
    public string RelativeFramePath { get; set; } = string.Empty;
    public bool IsSourceTransition { get; set; }
    public bool IsVisuallyDistinct { get; set; }
}

public sealed class ReceiptOcrFrame
{
    public string FrameId { get; set; } = Guid.NewGuid().ToString("N");
    public string TrackId { get; set; } = string.Empty;
    public string SegmentId { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public long MonotonicTicks { get; set; }
    public string RelativeFramePath { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string LanguageTag { get; set; } = string.Empty;
    public bool OcrSucceeded { get; set; }
}

public sealed class ReceiptOcrChange
{
    public string ChangeId { get; set; } = Guid.NewGuid().ToString("N");
    public ReceiptOcrChangeKind Kind { get; set; }
    public ReceiptChangeReviewState ReviewState { get; set; } = ReceiptChangeReviewState.Pending;
    public string TrackId { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string BeforeFrameId { get; set; } = string.Empty;
    public string AfterFrameId { get; set; } = string.Empty;
    public string BeforeText { get; set; } = string.Empty;
    public string AfterText { get; set; } = string.Empty;
    public double Similarity { get; set; }
    public string Explanation { get; set; } = string.Empty;

    public string Label => Kind switch
    {
        ReceiptOcrChangeKind.PossibleAddition => "Possible addition",
        ReceiptOcrChangeKind.PossibleDeletion => "Possible deletion",
        _ => "Possible edit"
    };
}

public sealed class ReceiptDerivativeLineage
{
    public string Schema { get; set; } = "receipts.derivative-lineage.v1";
    public string DerivativeId { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceReceiptId { get; set; } = string.Empty;
    public string SourceReceiptPath { get; set; } = string.Empty;
    public string ArtifactRole { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string? ParentDerivativeId { get; set; }
    public string? ParentDerivativePath { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<string> SourceSegmentIds { get; set; } = [];
    public long? StartMonotonicTicks { get; set; }
    public long? EndMonotonicTicks { get; set; }
}
