namespace FileRedact.Core.Model;

/// <summary>A single word on a page with its position. Text offsets refer to <see cref="PageModel.Text"/>.</summary>
public sealed class WordBox
{
    public required int Index { get; init; }
    public required string Text { get; init; }
    public required RectPt Rect { get; init; }
    public required int TextStart { get; init; }
    public int TextEnd => TextStart + Text.Length;
    /// <summary>Zero-based visual line number within the page, used to merge highlight rectangles per line.</summary>
    public required int Line { get; init; }
}
