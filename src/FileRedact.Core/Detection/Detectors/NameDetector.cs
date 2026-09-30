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
    // Given names that are also ordinary words ("chase", "will", "mark"): never matched in lower case.
    private static readonly Lazy<HashSet<string>> CommonWordNames = new(() => LoadList("CommonWordNames.txt"));

    // A capitalised name token: "John", "O'Brien", "Smith-Jones", "J.", "de", "van", or an all-caps token "SMITH".
    private const string Tok = @"(?:[A-Z][A-Za-z'’\-]+|[A-Z]\.?(?![a-z])|(?<![A-Za-z'’\-])(?:de|del|la|le|van|von|der|da|di|du|st\.?|mc|mac)\b)";
    private const string NameSeq = @"(?<name>" + Tok + @"(?:[ \t]+" + Tok + @"){0,3})";
    // "Smith, John A." and also the signature form "Richardson, D." with only an initial.
    private const string LastFirst = @"(?<name>[A-Z][A-Za-z'’\-]+,[ \t]+(?:[A-Z][A-Za-z'’\-]+|[A-Z]\.?)(?:[ \t]+[A-Z][A-Za-z'’\-]*\.?)?)";

    private static readonly Regex Titled = new(
        @"\b(?:Mr|Mrs|Ms|Miss|Mx|Dr|Prof|Rev|Hon|Officer|Ofc|Off|Det|Detective|Sgt|Sergeant|Lt|Lieutenant|Capt|Captain|Cpl|Corporal|Deputy|Dep|Trooper|Tpr|Judge|Justice|Attorney|Atty|Agent|Inspector|Insp|Chief|Sheriff|Marshal|Warden|Nurse|Pastor|Father|Sister|Brother|Coach|Investigator|Inv|Commander|Cmdr|Major|Maj|Colonel|Col|Private|Pvt|Specialist|Spc|Sen|Rep|Gov|Mayor)\.?[ \t]+" + NameSeq,
        Default | RegexOptions.ExplicitCapture);

    private static readonly Regex Labelled = new(
        @"\b(?i:name|full[ \t]+name|first[ \t]+name|last[ \t]+name|surname|defendant|victim|suspect|subject|arrestee|witness|complainant|reporting[ \t]+party|applicant|patient|client|claimant|plaintiff|respondent|petitioner|offender|juvenile|guardian|parent|mother|father|spouse|husband|wife|son|daughter|employee|employer|owner|driver|passenger|operator|contact|emergency[ \t]+contact|next[ \t]+of[ \t]+kin|signature|signed|prepared[ \t]+by|reported[ \t]+by|reviewed[ \t]+by|approved[ \t]+by|interviewed|arrested|involved|party|person|individual|decedent|deceased|inmate|probationer|parolee|caller|informant|co-?defendant|attorney[ \t]+for|represented[ \t]+by|alias|aka|a\.k\.a\.|interview[ \t]+with|interviewed[ \t]+with|identified[ \t]+as|known[ \t]+as|friend|girlfriend|boyfriend|sister|brother|stepsister|stepbrother|stepdad|stepfather|stepmom|stepmother|grandmother|grandfather|grandma|grandpa|cousin|aunt|uncle|nephew|niece|roommate|neighbor|neighbour|techs?|named|name[ \t]+of|belongs[ \t]+to|belonging[ \t]+to|resident[ \t]+at|residents?[ \t]+of|met[ \t]+with|spoke[ \t]+(?:with|to)|talked[ \t]+(?:with|to))(?:\(s\))?(?![A-Za-z])[ \t]*(?i:[:#\-–]|of|is|was)?[ \t]*(?:\r?\n[ \t]*)?(?:" + LastFirst + "|" + NameSeq + ")",
        Default | RegexOptions.ExplicitCapture);

    // "techs E. Burbrink and A Forlines", "Det. S. Barrow": an initial and a surname, inside a sentence (a word
    // precedes it), so that outline headings at the start of a line ("B. Scope", "C. Use") do not qualify.
    // Without the full stop the initial must follow "and", or "A Fragment" would qualify.
    private static readonly Regex InitialSurname = new(
        @"(?<=[a-z,][ \t]+)(?<name>(?:[A-Z]\.|(?<=\band[ \t]+)[A-Z])[ \t]+[A-Z][a-z][A-Za-z'’\-]+)(?![A-Za-z])",
        Default | RegexOptions.ExplicitCapture);

    // People speak; organisations, funds and vehicles do not. A name before a verb of speech is trusted more
    // than one before a verb of action ("Aid Funds received").
    private static readonly Regex SpeechVerb = new(
        @"(?i)\b(?:stated|states|said|says|told|tells|explained|explains|advised|advises|related|relates|confirmed|confirms|admitted|admits|acknowledged|acknowledges|believes|thinks|thought|texted|asked|replied|denied|denies|claimed|claims|recalled|informed|mentioned|described|describes)$",
        Default);

    // "Haylee Hartman was interviewed", "Kaitlyn stated", "Mikey told her": a capitalised word or two before a
    // verb that only a person performs in a report. Catches people with no title and a first name the
    // gazetteer does not know.
    private static readonly Regex Speech = new(
        NameSeq + @"(?:’s|'s)?[ \t]+(?i:(?:was|were|is|has|had|also|then|later|again|further)[ \t]+)?(?i:stated|states|said|says|told|tells|explained|explains|advised|advises|related|relates|confirmed|confirms|admitted|admits|acknowledged|acknowledges|believes|thinks|thought|texted|asked|replied|denied|denies|reported|reports|described|describes|mentioned|responded|agreed|indicated|claimed|claims|recalled|went|gave|knew|knows|informed|interviewed|arrested|charged|transported|contacted|identified|located|observed|witnessed|heard|saw|spoke|met|lives|lived|resides|resided|works|worked|drove|drives|owns|owned|sent|received|answered|called|turned)(?![A-Za-z])",
        Default | RegexOptions.ExplicitCapture);

    // ALL CAPS "SMITH, JOHN A" - very common on booking sheets and citations. The surname may be two words
    // ("FONTE PAREDES, YASUAN"); when the first of them is a label word ("WITNESS SMITH, JOHN") it is dropped.
    private static readonly Regex CapsLastFirst = new(@"(?<![A-Za-z])(?:(?<last1>[A-Z][A-Z'’\-]{1,})[ \t])?(?<last>[A-Z][A-Z'’\-]{1,})(?:,[ \t]+|[ \t]{2,})(?<first>[A-Z][A-Z'’\-]{1,})(?:[ \t]+(?<mid>[A-Z][A-Z'’\-]*\.?))?(?![A-Za-z])", Default);
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
        {
            // A value on the line below the label is real on a form ("OFFICER SIGNATURE\nRichardson, D.",
            // "identified as\nDJ Jewel") but a heading or the first word of the next paragraph is not
            // ("JUVENILE\nKYIBRS REPORT", "JUVENILE\nYea"): across a line break insist on "Last, First" or two words.
            var g = m.Groups["name"];
            if (m.Value.Contains('\n'))
            {
                if (g.Value == g.Value.ToUpperInvariant()) continue;
                if (!g.Value.Contains(',') && Token.Matches(g.Value).Count < 2) continue;
            }
            AddCleaned(results, text, g, 0.85, "Name following a role label");
        }

        foreach (Match m in Speech.Matches(text))
        {
            var verb = m.Value.TrimEnd().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[^1];
            var speaks = SpeechVerb.IsMatch(verb);
            AddCleaned(results, text, m.Groups["name"], speaks ? 0.8 : 0.65,
                speaks ? "Name before a verb of speech" : "Name before a verb of action", minConfidence: 0.6,
                singleMixedCase: speaks ? 0.7 : 0.55);
        }

        foreach (Match m in InitialSurname.Matches(text))
        {
            var g = m.Groups["name"];
            var surname = g.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[^1];
            if (IsStop(surname) || IsCommonWordName(surname)) continue;
            results.Add(new TextSpan(g.Index, g.Length, PiiCategory.Name, 0.65, "Initial and surname"));
        }

        foreach (Match m in CapsLastFirst.Matches(text))
        {
            var last = m.Groups["last"].Value;
            var first = m.Groups["first"].Value;
            if (IsStop(last) || IsStop(first)) continue;
            var start = m.Groups["last1"].Success && IsStop(m.Groups["last1"].Value) ? m.Groups["last"].Index : m.Index;
            var end = m.Groups["first"].Index + first.Length;
            // Keep a middle initial or name; leave out a filler such as "NONE" or "NMN" rather than dropping the match.
            var mid = m.Groups["mid"];
            if (mid.Success && (mid.Value.TrimEnd('.').Length == 1 || !IsStop(mid.Value))) end = mid.Index + mid.Length;
            var known = IsFirstName(first);
            results.Add(new TextSpan(start, end - start, PiiCategory.Name, known ? 0.9 : 0.65, "LAST, FIRST layout"));
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
            // A given name that ends a sentence ("... spoke with Sarah.") does not continue into the next one.
            var stop = t.Value.EndsWith('.');
            while (!stop && j < tokens.Count && count < 3)
            {
                var n = tokens[j];
                // Must be on the same line, separated only by spaces.
                if (!OnlySpacesBetween(text, end, n.Index)) break;
                if (!IsCapitalised(n.Value) || IsStop(n.Value)) break;
                end = n.Index + n.Length;
                count++;
                // "Floyd. Large" / "Connor. Following": a full stop ends the name unless the token is an initial.
                if (n.Value.EndsWith('.') && n.Value.Length > 2) break;
                j++;
            }
            if (count == 0)
            {
                // A given name on its own is weaker evidence, but in a report it is usually a person. Not when it
                // is ALL CAPS (a form label), very short, a common word, or part of a place ("Jefferson County").
                var w = t.Value.TrimEnd('.');
                if (w.Length < 3 || w == w.ToUpperInvariant() || IsCommonWordName(w)) continue;
                var prevCapitalised = i > 0 && IsCapitalised(tokens[i - 1].Value) && !IsStop(tokens[i - 1].Value) && OnlySpacesBetween(text, tokens[i - 1].Index + tokens[i - 1].Length, t.Index);
                if (prevCapitalised) continue; // the tail of a name found by another rule
                var nextIsPlaceWord = j < tokens.Count && OnlySpacesBetween(text, end, tokens[j].Index) && IsCapitalised(tokens[j].Value) && IsStop(tokens[j].Value);
                if (nextIsPlaceWord) continue;
                results.Add(new TextSpan(t.Index, t.Value.TrimEnd('.').Length, PiiCategory.Name, 0.6, "Known given name"));
                continue;
            }
            var reason = "Known given name followed by capitalised word(s)";
            results.Add(new TextSpan(t.Index, end - t.Index, PiiCategory.Name, 0.8, reason));
        }

        // Propagation of tokens from names found elsewhere in the document.
        if (context.KnownNameTokens.Count > 0)
        {
            var longKnown = context.KnownNameTokens.Where(k => k.Length >= 5).ToList();
            foreach (Match t in tokens)
            {
                var v = t.Value.TrimEnd('.');
                // "Johnson's" / "JOHNSON’S" -> compare the base word, but redact the whole token. "Molina-" is the
                // first half of a hyphenated name wrapped onto the next line.
                var baseWord = v.TrimEnd('-');
                var possessive = false;
                if (baseWord.EndsWith("'s", StringComparison.OrdinalIgnoreCase) || baseWord.EndsWith("’s", StringComparison.OrdinalIgnoreCase))
                {
                    baseWord = baseWord[..^2];
                    possessive = true;
                }
                if (baseWord.Length < 3 || IsStop(baseWord)) continue;
                if (context.KnownNameTokens.Contains(baseWord))
                {
                    // Chat transcripts write names in lower case ("cole", "joe"). Only a known given name is accepted
                    // that way, and not one that is also an ordinary word ("chase", "will"): a lower-case token that
                    // happens to match a surname or a mistaken name ("deadly", "training") is a word.
                    if (!IsCapitalised(baseWord) && (!IsFirstName(baseWord) || IsCommonWordName(baseWord))) continue;
                    results.Add(new TextSpan(t.Index, v.Length, PiiCategory.Name, 0.7, "Matches part of a name found elsewhere in the document"));
                    continue;
                }
                // Misspellings and typos ("Oritz", "Katlyn", "Jasan", "Orit’s"): one edit away from a name in the
                // document. A dropped or added letter is only trusted on longer words ("Arrow" is not "Barrow").
                if ((baseWord.Length >= 5 || (possessive && baseWord.Length >= 4)) && IsCapitalised(baseWord) && !IsFirstName(baseWord))
                {
                    foreach (var k in longKnown)
                    {
                        if (Math.Abs(k.Length - baseWord.Length) > 1 || !WithinOneEdit(baseWord, k)) continue;
                        if (k.Length != baseWord.Length && baseWord.Length < 6 && !possessive) continue;
                        results.Add(new TextSpan(t.Index, v.Length, PiiCategory.Name, 0.65, $"Probable misspelling of \"{k}\" found elsewhere in the document"));
                        break;
                    }
                }
            }
        }

        return results;
    }

    /// <summary>Damerau-Levenshtein distance of at most one (insert, delete, substitute or swap adjacent), ignoring case.</summary>
    public static bool WithinOneEdit(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        a = a.ToLowerInvariant(); b = b.ToLowerInvariant();
        if (a.Length == b.Length)
        {
            var diffs = new List<int>();
            for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) { diffs.Add(i); if (diffs.Count > 2) return false; }
            if (diffs.Count == 1) return true;
            return diffs.Count == 2 && diffs[1] == diffs[0] + 1 && a[diffs[0]] == b[diffs[1]] && a[diffs[1]] == b[diffs[0]];
        }
        if (Math.Abs(a.Length - b.Length) != 1) return false;
        var (s, l) = a.Length < b.Length ? (a, b) : (b, a);
        var si = 0; var li = 0; var skipped = false;
        while (si < s.Length && li < l.Length)
        {
            if (s[si] == l[li]) { si++; li++; continue; }
            if (skipped) return false;
            skipped = true; li++;
        }
        return true;
    }

    /// <summary>
    /// Tokens (>= 3 letters, not stop words) of a name that should be propagated through the document. The halves
    /// of a hyphenated surname count too, so "Molina-Holland" is found when a line break splits it.
    /// </summary>
    public static IEnumerable<string> NameTokens(string name)
    {
        foreach (Match t in Token.Matches(name))
        {
            var v = t.Value.TrimEnd('.', ',');
            if (v.Length < 3 || IsStop(v)) continue;
            yield return v;
            if (!v.Contains('-')) continue;
            foreach (var part in v.Split('-', StringSplitOptions.RemoveEmptyEntries))
                if (part.Length >= 3 && !IsStop(part)) yield return part;
        }
    }

    private static void AddCleaned(List<TextSpan> results, string text, Group g, double confidence, string reason, double minConfidence = 0, double? singleMixedCase = null)
    {
        if (!g.Success) return;
        // Trim trailing tokens that are stop words ("John Smith Police Department" -> "John Smith").
        var toks = Token.Matches(g.Value).Cast<Match>().ToList();
        // "Springfield Police Department responded": what is left after the organisation words are trimmed is
        // a place, not a person, so a single remaining word gets no credit from the verb.
        var trimmedTrailing = false;
        while (toks.Count > 0 && IsStop(toks[^1].Value) && toks[^1].Value.TrimEnd('.').Length > 1) { toks.RemoveAt(toks.Count - 1); trimmedTrailing = true; }
        if (trimmedTrailing && toks.Count == 1) singleMixedCase = null;
        while (toks.Count > 0 && IsStop(toks[0].Value) && toks[0].Value.TrimEnd('.').Length > 1) toks.RemoveAt(0);
        // A lone initial is not a name, and neither is a run of lower-case particles ("de la").
        if (toks.Count == 1 && toks[0].Value.TrimEnd('.').Length == 1) return;
        if (toks.Count == 0 || !toks.Any(t => IsCapitalised(t.Value))) return;
        var start = g.Index + toks[0].Index;
        var end = g.Index + toks[^1].Index + toks[^1].Length;
        // A single capitalised token is weak evidence unless it is a known given name. An ALL CAPS token is
        // kept as a finding but held below the propagation threshold: on forms the word after a label is
        // often just the next label ("OFFENDER SUSPECTED", "VICTIM DATA").
        var single = toks.Count == 1;
        var conf = confidence;
        if (single && !IsFirstName(toks[0].Value))
            conf = toks[0].Value == toks[0].Value.ToUpperInvariant() ? Math.Min(conf, 0.7) : singleMixedCase ?? conf - 0.25;
        if (conf < minConfidence) return;
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
    private static bool IsCommonWordName(string s) => CommonWordNames.Value.Contains(s.TrimEnd('.', ',').ToLowerInvariant());

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
