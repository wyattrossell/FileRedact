using FileRedact.Core.Detection.Detectors;
using FileRedact.Core.Model;

namespace FileRedact.Core.Detection;

public sealed class DetectionOptions
{
    public IReadOnlyList<string> CustomTerms { get; init; } = Array.Empty<string>();
    /// <summary>Findings below this confidence are shown but not pre-selected.</summary>
    public double AutoAcceptThreshold { get; init; } = 0.6;
    /// <summary>Whether tokens of detected names should be searched for throughout the document.</summary>
    public bool PropagateNames { get; init; } = true;
    public ISet<PiiCategory> DisabledCategories { get; init; } = new HashSet<PiiCategory>();
}

/// <summary>
/// Runs every detector over each page, resolves overlapping spans, maps the surviving spans onto word
/// boxes and produces <see cref="Finding"/>s with one rectangle per visual line.
/// </summary>
public sealed class DetectionEngine
{
    private readonly IReadOnlyList<IPiiDetector> _detectors;

    public DetectionEngine() : this(DefaultDetectors()) { }

    public DetectionEngine(IReadOnlyList<IPiiDetector> detectors) => _detectors = detectors;

    public static IReadOnlyList<IPiiDetector> DefaultDetectors() => new IPiiDetector[]
    {
        new SsnDetector(),
        new DateDetector(),
        new PhoneDetector(),
        new EmailDetector(),
        new AddressDetector(),
        new DriverLicenseDetector(),
        new PassportDetector(),
        new FinancialDetector(),
        new VehicleDetector(),
        new CriminalJusticeIdDetector(),
        new PlaceOfBirthDetector(),
        new MaidenNameDetector(),
        new IpAddressDetector(),
        new OnlineHandleDetector(),
        new WebAddressDetector(),
        new NameDetector(),
        new CustomTermDetector(),
    };

    /// <summary>
    /// Whether a name span is trustworthy enough for its tokens to be searched for throughout the document.
    /// Confident spans always are; moderately confident ones only when written in mixed case, because the
    /// weak ALL-CAPS spans are form labels mistaken for names ("OFFENDER SUSPECTED") and would spread across
    /// every page. A lone given name or a phrase before a verb of action ("Aid Funds received") never seeds.
    /// </summary>
    private static bool Propagates(TextSpan s, string text)
        => s.Confidence >= 0.8
           || (s.Confidence >= 0.7 && s.Reason != "Known given name" && text.AsSpan(s.Start, s.Length).ContainsAny(Lower));

    private static readonly System.Buffers.SearchValues<char> Lower = System.Buffers.SearchValues.Create("abcdefghijklmnopqrstuvwxyz");

    public List<Finding> Detect(RedactDocument doc, DetectionOptions options)
    {
        var ctx = new DetectionContext { CustomTerms = options.CustomTerms };
        var perPage = new List<List<TextSpan>>();

        // Pass 1: all detectors on every page.
        foreach (var page in doc.Pages)
        {
            var spans = new List<TextSpan>();
            foreach (var d in _detectors)
                spans.AddRange(d.Detect(page.Text, ctx));
            // Boxed forms: values that sit beneath a label and are scattered in the reading-order text.
            spans.AddRange(FormFieldDetector.Detect(page));
            perPage.Add(spans);
        }

        // Pass 2: propagate name tokens document-wide so that "Smith" alone is caught once "John Smith" is known.
        if (options.PropagateNames)
        {
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var areaCodes = new HashSet<string>();
            for (var i = 0; i < doc.Pages.Count; i++)
            {
                var text = doc.Pages[i].Text;
                foreach (var s in perPage[i].Where(s => s.Category is PiiCategory.Name or PiiCategory.CustomTerm && Propagates(s, text)))
                    foreach (var t in NameDetector.NameTokens(text.Substring(s.Start, s.Length)))
                        tokens.Add(t);
                // Only handles confirmed by a platform label spread; the shape-based guesses do not.
                foreach (var s in perPage[i].Where(s => s.Category == PiiCategory.OnlineHandle && s.Confidence >= 0.85))
                    handles.Add(text.Substring(s.Start, s.Length));
                foreach (var s in perPage[i].Where(s => s.Category == PiiCategory.PhoneNumber && s.Confidence >= 0.9))
                {
                    var digits = new string(text.AsSpan(s.Start, s.Length).ToArray().Where(char.IsDigit).ToArray());
                    if (digits.Length == 11 && digits[0] == '1') digits = digits[1..];
                    if (digits.Length == 10) areaCodes.Add(digits[..3]);
                }
            }
            if (tokens.Count > 0 || handles.Count > 0 || areaCodes.Count > 0)
            {
                var nameDetector = new NameDetector();
                var handleDetector = new OnlineHandleDetector();
                var phoneDetector = new PhoneDetector();
                var propCtx = new DetectionContext { CustomTerms = Array.Empty<string>(), KnownNameTokens = tokens, KnownHandles = handles, KnownAreaCodes = areaCodes };
                for (var i = 0; i < doc.Pages.Count; i++)
                {
                    var text = doc.Pages[i].Text;
                    if (tokens.Count > 0)
                        perPage[i].AddRange(nameDetector.Detect(text, propCtx).Where(s => s.Reason.StartsWith("Matches part") || s.Reason.StartsWith("Probable misspelling")));
                    if (handles.Count > 0)
                        perPage[i].AddRange(handleDetector.Detect(text, propCtx).Where(s => s.Reason.StartsWith("Matches a user name") || s.Reason.StartsWith("Sender of")));
                    if (areaCodes.Count > 0)
                        perPage[i].AddRange(phoneDetector.Detect(text, propCtx).Where(s => s.Reason.StartsWith("10 digits with an area code")));
                }
            }
        }

        var findings = new List<Finding>();
        for (var i = 0; i < doc.Pages.Count; i++)
        {
            var page = doc.Pages[i];
            var spans = perPage[i].Where(s => !options.DisabledCategories.Contains(s.Category));
            foreach (var span in ResolveOverlaps(spans))
            {
                var f = ToFinding(page, span, options);
                if (f != null) findings.Add(f);
            }
        }
        return findings;
    }

    /// <summary>Find every occurrence of a literal term and return findings for it (used by "redact all occurrences").</summary>
    public static List<Finding> FindTerm(RedactDocument doc, string term, PiiCategory category, bool accepted = true)
    {
        var re = CustomTermDetector.BuildTermRegex(term);
        var list = new List<Finding>();
        foreach (var page in doc.Pages)
        {
            foreach (System.Text.RegularExpressions.Match m in re.Matches(page.Text))
            {
                var span = new TextSpan(m.Index, m.Length, category, 1.0, $"Occurrence of \"{term}\"");
                var f = ToFinding(page, span, new DetectionOptions { AutoAcceptThreshold = 0 });
                if (f != null)
                {
                    f.Accepted = accepted;
                    list.Add(f);
                }
            }
        }
        return list;
    }

    /// <summary>Greedy resolution: highest confidence wins, then the longer span. Overlapping losers are dropped.</summary>
    public static List<TextSpan> ResolveOverlaps(IEnumerable<TextSpan> spans)
    {
        var ordered = spans
            .Where(s => s.Length > 0)
            .OrderByDescending(s => s.Confidence)
            .ThenByDescending(s => s.Length)
            .ThenBy(s => s.Start)
            .ToList();
        var kept = new List<TextSpan>();
        foreach (var s in ordered)
        {
            if (kept.Any(k => k.Overlaps(s))) continue;
            kept.Add(s);
        }
        kept.Sort((a, b) => a.Start.CompareTo(b.Start));
        return kept;
    }

    public static Finding? ToFinding(PageModel page, TextSpan span, DetectionOptions options)
    {
        var words = page.WordsInSpan(span.Start, span.Length).ToList();
        if (words.Count == 0) return null;
        var rects = MergeByLine(words);
        var text = page.Text.Substring(span.Start, span.Length).Replace('\n', ' ').Trim();
        return new Finding
        {
            Category = span.Category,
            Page = page.Number,
            Text = text,
            Rects = rects,
            Confidence = span.Confidence,
            Reason = span.Reason,
            TextStart = span.Start,
            TextLength = span.Length,
            Accepted = span.Category.AcceptedByDefault() && span.Confidence >= options.AutoAcceptThreshold,
        };
    }

    /// <summary>One rectangle per visual line, so multi-line findings do not black out the gap between lines.</summary>
    public static List<RectPt> MergeByLine(IEnumerable<WordBox> words)
    {
        return words
            .GroupBy(w => w.Line)
            .OrderBy(g => g.Key)
            .Select(g => g.Select(w => w.Rect).Aggregate((a, b) => a.Union(b)))
            .ToList();
    }
}
