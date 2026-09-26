using System.Reflection;
using System.Text.RegularExpressions;
using FileRedact.Core.Model;
using static FileRedact.Core.Detection.RegexHelpers;

namespace FileRedact.Core.Detection.Detectors;

/// <summary>
/// Heuristic person-name detector. There is no perfect regex for names, so several independent signals
/// are combined, each producing spans with a confidence that reflects how reliable the signal is:
/// <list type="bullet">
/// <item>a role label ("Defendant:", "Victim -", "Name:") followed by capitalised words,</item>
/// <item>an honorific or rank ("Mr.", "Det.", "Officer") followed by capitalised words,</item>
/// <item>the "LAST, FIRST MIDDLE" layout common on law-enforcement forms,</item>
/// <item>a known given name (gazetteer) followed by one or two capitalised words,</item>
/// <item>tokens of names already found elsewhere in the document (propagation).</item>
/// </list>
/// The word lists live in embedded resources so they can be extended without code changes.
/// </summary>
public sealed class NameDetector : IPiiDetector
{
    private static readonly Lazy<HashSet<string>> FirstNames = new(() => LoadList("FirstNames.txt"));
    private static readonly Lazy<HashSet<string>> StopWords = new(() => LoadList("StopWords.txt"));

    // A capitalised name token: "John", "O'Brien", "Smith-Jones", "J.", "de", "van", or an all-caps token "SMITH".
    private const string Tok = @"(?:[A-Z][A-Za-z'’\-]+|[A-Z]\.?(?![a-z])|(?:de|del|la|le|van|von|der|da|di|du|st\.?|mc|mac)\b)";
    private const string NameSeq = @"(?<name>" + Tok + @"(?:[ \t]+" + Tok + @"){0,3})";
    private const string LastFirst = @"(?<name>[A-Z][A-Za-z'’\-]+,[ \t]+[A-Z][A-Za-z'’\-]+(?:[ \t]+[A-Z][A-Za-z'’\-]*\.?)?)";

    private static readonly Regex Titled = new(
        @"\b(?:Mr|Mrs|Ms|Miss|Mx|Dr|Prof|Rev|Hon|Officer|Ofc|Off|Det|Detective|Sgt|Sergeant|Lt|Lieutenant|Capt|Captain|Cpl|Corporal|Deputy|Dep|Trooper|Tpr|Judge|Justice|Attorney|Atty|Agent|Inspector|Insp|Chief|Sheriff|Marshal|Warden|Nurse|Pastor|Father|Sister|Brother|Coach|Investigator|Inv|Commander|Cmdr|Major|Maj|Colonel|Col|Private|Pvt|Specialist|Spc|Sen|Rep|Gov|Mayor)\.?[ \t]+" + NameSeq,
        Default | RegexOptions.ExplicitCapture);

    private static readonly Regex Labelled = new(
        @"\b(?i:name|full[ \t]+name|first[ \t]+name|last[ \t]+name|surname|defendant|victim|suspect|subject|arrestee|witness|complainant|reporting[ \t]+party|applicant|patient|client|claimant|plaintiff|respondent|petitioner|offender|juvenile|guardian|parent|mother|father|spouse|husband|wife|son|daughter|employee|employer|owner|driver|passenger|operator|contact|emergency[ \t]+contact|next[ \t]+of[ \t]+kin|signature|signed|prepared[ \t]+by|reported[ \t]+by|reviewed[ \t]+by|approved[ \t]+by|interviewed|arrested|involved|party|person|individual|decedent|deceased|inmate|probationer|parolee|caller|informant|co-?defendant|attorney[ \t]+for|represented[ \t]+by|alias|aka|a\.k\.a\.)(?:\(s\))?\s*(?i:[:#\-–]|of|is|was)?[ \t]*(?:" + LastFirst + "|" + NameSeq + ")",
        Default | RegexOptions.ExplicitCapture);

    // ALL CAPS "SMITH, JOHN A" - very common on booking sheets and citations.
    private static readonly Regex CapsLastFirst = new(@"(?<![A-Za-z])(?<last>[A-Z][A-Z'’\-]{1,})(?:,[ \t]+|[ \t]{2,})(?<first>[A-Z][A-Z'’\-]{1,})(?:[ \t]+(?<mid>[A-Z][A-Z'’\-]*\.?))?(?![A-Za-z])", Default);
    // Mixed case "Smith, John A."
    private static readonly Regex MixedLastFirst = new(@"(?<![A-Za-z])(?<last>[A-Z][a-z'’\-]+),[ \t]+(?<first>[A-Z][a-z'’\-]+)(?:[ \t]+(?<mid>[A-Z][a-z'’\-]*\.?))?(?![A-Za-z])", Default);

    private static readonly Regex Token = new(@"[A-Za-z][A-Za-z'’\-]*\.?", Default);

    public string Name => "Name";

    public IEnumerable<TextSpan> Detect(string text, DetectionContext context)
    {
        var results = new List<TextSpan>();

        foreach (Match m in Titled.Matches(text))
            AddCleaned(results, text, m.Groups["name"], 0.85, "Name following an honorific or rank");

        foreach (Match m in Labelled.Matches(text))
            AddCleaned(results, text, m.Groups["name"], 0.85, "Name following a role label");

        foreach (Match m in CapsLastFirst.Matches(text))
        {
            var last = m.Groups["last"].Value;
            var first = m.Groups["first"].Value;
            if (IsStop(last) || IsStop(first)) continue;
            if (m.Groups["mid"].Success && m.Groups["mid"].Value.TrimEnd('.').Length > 1 && IsStop(m.Groups["mid"].Value)) continue;
            var known = IsFirstName(first);
            results.Add(new TextSpan(m.Index, m.Length, PiiCategory.Name, known ? 0.9 : 0.65, "LAST, FIRST layout"));
        }

        foreach (Match m in MixedLastFirst.Matches(text))
        {
            var last = m.Groups["last"].Value;
            var first = m.Groups["first"].Value;
            if (IsStop(last) || IsStop(first)) continue;
            if (!IsFirstName(first)) continue; // "Denver, Colorado" style false positives
            results.Add(new TextSpan(m.Index, m.Length, PiiCategory.Name, 0.85, "Last, First layout with a known given name"));
        }

        // Gazetteer: known given name followed by capitalised token(s).
        var tokens = Token.Matches(text);
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (!IsCapitalised(t.Value) || !IsFirstName(t.Value) || IsStop(t.Value)) continue;
            var end = t.Index + t.Length;
            var count = 0;
            var j = i + 1;
            while (j < tokens.Count && count < 3)
            {
                var n = tokens[j];
                // Must be on the same line, separated only by spaces.
                if (!OnlySpacesBetween(text, end, n.Index)) break;
                if (!IsCapitalised(n.Value) || IsStop(n.Value)) break;
                end = n.Index + n.Length;
                count++;
                j++;
            }
            if (count == 0) continue;
            var reason = "Known given name followed by capitalised word(s)";
            results.Add(new TextSpan(t.Index, end - t.Index, PiiCategory.Name, 0.8, reason));
        }

        // Propagation of tokens from names found elsewhere in the document.
        if (context.KnownNameTokens.Count > 0)
        {
            foreach (Match t in tokens)
            {
                var v = t.Value.TrimEnd('.');
                if (v.Length < 3 || !IsCapitalised(v) || IsStop(v)) continue;
                if (!context.KnownNameTokens.Contains(v)) continue;
                results.Add(new TextSpan(t.Index, v.Length, PiiCategory.Name, 0.7, "Matches part of a name found elsewhere in the document"));
            }
        }

        return results;
    }

    /// <summary>Tokens (>= 3 letters, not stop words) of a name that should be propagated through the document.</summary>
    public static IEnumerable<string> NameTokens(string name)
    {
        foreach (Match t in Token.Matches(name))
        {
            var v = t.Value.TrimEnd('.', ',');
            if (v.Length < 3 || IsStop(v)) continue;
            // Very common given names propagate poorly ("Will", "Grant"); surnames and rarer names are fine.
            yield return v;
        }
    }

    private static void AddCleaned(List<TextSpan> results, string text, Group g, double confidence, string reason)
    {
        if (!g.Success) return;
        // Trim trailing tokens that are stop words ("John Smith Police Department" -> "John Smith").
        var toks = Token.Matches(g.Value).Cast<Match>().ToList();
        while (toks.Count > 0 && IsStop(toks[^1].Value) && toks[^1].Value.TrimEnd('.').Length > 1) toks.RemoveAt(toks.Count - 1);
        while (toks.Count > 0 && IsStop(toks[0].Value) && toks[0].Value.TrimEnd('.').Length > 1) toks.RemoveAt(0);
        // A lone initial is not a name.
        if (toks.Count == 1 && toks[0].Value.TrimEnd('.').Length == 1) return;
        if (toks.Count == 0) return;
        var start = g.Index + toks[0].Index;
        var end = g.Index + toks[^1].Index + toks[^1].Length;
        // A single capitalised token is weak evidence unless it is a known given name or ALL CAPS.
        var single = toks.Count == 1;
        var conf = confidence;
        if (single && !IsFirstName(toks[0].Value) && toks[0].Value != toks[0].Value.ToUpperInvariant()) conf -= 0.25;
        results.Add(new TextSpan(start, end - start, PiiCategory.Name, conf, reason));
    }

    private static bool OnlySpacesBetween(string text, int from, int to)
    {
        if (to <= from || to - from > 3) return false;
        for (var i = from; i < to; i++) if (text[i] != ' ' && text[i] != '\t') return false;
        return true;
    }

    private static bool IsCapitalised(string s) => s.Length > 0 && char.IsUpper(s[0]);
    private static bool IsFirstName(string s) => FirstNames.Value.Contains(s.TrimEnd('.', ',').ToLowerInvariant());
    private static bool IsStop(string s) => StopWords.Value.Contains(s.TrimEnd('.', ',').ToLowerInvariant());

    private static HashSet<string> LoadList(string file)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resName = asm.GetManifestResourceNames().First(n => n.EndsWith(file, StringComparison.OrdinalIgnoreCase));
        using var s = asm.GetManifestResourceStream(resName)!;
        using var r = new StreamReader(s);
        var set = new HashSet<string>(StringComparer.Ordinal);
        string? line;
        while ((line = r.ReadLine()) != null)
        {
            if (line.StartsWith('#')) continue;
            foreach (var w in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                set.Add(w.ToLowerInvariant());
        }
        return set;
    }
}
