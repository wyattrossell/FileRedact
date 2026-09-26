namespace FileRedact.Core.Model;

/// <summary>
/// A loaded document. Whatever the input format, it is normalised to a PDF on disk
/// (<see cref="WorkingPdfPath"/>) which is what gets displayed, analysed and redacted.
/// </summary>
public sealed class RedactDocument
{
    public required string SourcePath { get; init; }
    public required string WorkingPdfPath { get; init; }
    public required IReadOnlyList<PageModel> Pages { get; init; }
    public List<Finding> Findings { get; } = new();

    public string DisplayName => Path.GetFileName(SourcePath);

    public string FullText => string.Join("\n\f\n", Pages.Select(p => p.Text));

    public IEnumerable<Finding> AcceptedFindings => Findings.Where(f => f.Accepted);
}
