namespace FileRedact.Core.Model;

/// <summary>A proposed (or user-created) redaction region on a single page.</summary>
public sealed class Finding
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required PiiCategory Category { get; init; }
    public required int Page { get; init; }
    /// <summary>The text that will be removed. Used for display and for "redact all occurrences".</summary>
    public required string Text { get; init; }
    /// <summary>One rectangle per visual line covered by the finding, in page points (top-left origin).</summary>
    public required IReadOnlyList<RectPt> Rects { get; init; }
    public double Confidence { get; init; } = 1.0;
    public string Reason { get; init; } = "";
    public bool IsManual { get; init; }
    /// <summary>Whether the reviewer has confirmed this finding for redaction.</summary>
    public bool Accepted { get; set; }
    public int TextStart { get; init; } = -1;
    public int TextLength { get; init; }

    public RectPt Bounds => Rects.Aggregate(Rects[0], (a, b) => a.Union(b));
}
