namespace FileRedact.Core.Model;

public sealed class PageModel
{
    /// <summary>One-based page number.</summary>
    public required int Number { get; init; }
    /// <summary>Displayed page width in points (after applying the page /Rotate).</summary>
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required IReadOnlyList<WordBox> Words { get; init; }
    /// <summary>Reading-order text of the page. Word offsets map into this string.</summary>
    public required string Text { get; init; }
    /// <summary>True when the text came from OCR rather than from the PDF text layer.</summary>
    public bool FromOcr { get; init; }

    public IEnumerable<WordBox> WordsInSpan(int start, int length)
    {
        var end = start + length;
        foreach (var w in Words)
        {
            if (w.TextStart < end && w.TextEnd > start && w.Text.Trim().Length > 0)
                yield return w;
        }
    }

    public IEnumerable<WordBox> WordsIntersecting(RectPt rect)
        => Words.Where(w => w.Rect.Intersects(rect) && w.Rect.IntersectionArea(rect) > 0.3 * w.Rect.Area);
}
