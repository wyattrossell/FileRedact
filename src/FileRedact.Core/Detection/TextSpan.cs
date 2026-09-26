using FileRedact.Core.Model;

namespace FileRedact.Core.Detection;

/// <summary>A detected span of PII within a page's text.</summary>
public sealed record TextSpan(int Start, int Length, PiiCategory Category, double Confidence, string Reason)
{
    public int End => Start + Length;
    public bool Overlaps(TextSpan o) => Start < o.End && o.Start < End;
}

/// <summary>Shared state passed to detectors.</summary>
public sealed class DetectionContext
{
    public IReadOnlyList<string> CustomTerms { get; init; } = Array.Empty<string>();
    /// <summary>Name tokens already discovered elsewhere in the document (for propagation).</summary>
    public ISet<string> KnownNameTokens { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

public interface IPiiDetector
{
    string Name { get; }
    IEnumerable<TextSpan> Detect(string text, DetectionContext context);
}
