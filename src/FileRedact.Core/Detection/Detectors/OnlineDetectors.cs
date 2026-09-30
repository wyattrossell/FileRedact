using System.Text.RegularExpressions;
using FileRedact.Core.Model;
using static FileRedact.Core.Detection.RegexHelpers;

namespace FileRedact.Core.Detection.Detectors;

/// <summary>
/// Social-media user names and handles. Investigative reports quote Instagram / Snapchat / Facebook
/// exports at length ("cole.2943 (Instagram: 52256127893)", "jasonlannan20- Fri Dec 13 ..."), and a
/// handle identifies a person as surely as a name does. Three signals, in decreasing confidence:
/// a platform label next to the token, the shape of a platform export line, and the shape of the
/// token itself (letters mixed with digits, underscores or an interior dot).
/// </summary>
public sealed class OnlineHandleDetector : IPiiDetector
{
    private const string Platforms = @"(?:instagram|insta|ig|snapchat|snap\s*chat|snap|facebook|fb|tiktok|tik\s*tok|twitter|telegram|discord|kik|whatsapp|cash\s*app|venmo|reddit|youtube)";
    private const string Handle = @"(?<h>[A-Za-z0-9](?:[A-Za-z0-9._]{2,38}))";

    // "Instagram handle of kuttaosama", "username of Chloe.g1216", "user name 444.kali", "screen name X". The bare
    // word "handle" is a verb as often as a noun ("how you handle permits"), so on its own it needs "of"/"is".
    private static readonly Regex Labelled = new(
        @"\b(?:" + Platforms + @"\s+(?:handle|account|user\s*name|name|user|id)|user\s*name|screen\s+name|display\s+name|handle(?=\s*(?:of|is|was|:|named?)\b))\s*(?:of|is|was|:|named?)?\s*(?:of\s+)?(?:a\s+|the\s+)?" + Handle + @"(?![A-Za-z0-9_])",
        IgnoreCase);
    // "cole.2943 (Instagram: 52256127893)" — Meta business-record export.
    private static readonly Regex ExportAccount = new(
        @"(?<![A-Za-z0-9._@/])" + Handle + @"\s*\(" + Platforms + @"\s*:?\s*\d{5,}\)",
        IgnoreCase);
    // "jasonlannan20- Fri Dec 13 05:05:51 UTC 2024- ..." and "dylan_barness- Yall heard" — Snapchat export lines.
    private static readonly Regex ExportLine = new(
        @"(?m)^[ \t]*" + Handle + @"-[ \t]*(?=\S)",
        Default);
    // "the Instagram account bigmoney502__", "Snapchat account of jasonlannan20", "user bellax_55", "with a username of X".
    private static readonly Regex PlatformAccount = new(
        @"\b" + Platforms + @"\s+(?:account|user|profile|page)\s*(?:of|named?|is|:)?\s*" + Handle + @"(?![A-Za-z0-9_])",
        IgnoreCase);
    // Token shape: letters plus digits, underscores or an interior dot; never all caps (that is an ID or serial).
    // A dash glued to the token ("2024-10-31", "HPD24-000774") makes it part of something else; a dash followed
    // by a space ("Katiss_spammm- no return") does not.
    private static readonly Regex Shaped = new(
        @"(?<![A-Za-z0-9._@/%&=])(?<![A-Za-z0-9]-)(?<h>[A-Za-z0-9][A-Za-z0-9._]{3,38}[A-Za-z0-9_])(?![A-Za-z0-9_@/])(?!-[A-Za-z0-9])",
        Default);

    private static readonly Regex Ordinal = new(@"^\d+(?:st|nd|rd|th)$", IgnoreCase);
    private static readonly Regex Unit = new(@"^\d+(?:\.\d+)?[a-z]{1,3}$", IgnoreCase);
    private static readonly Regex Version = new(@"^v?\d+(?:\.\d+)+$", IgnoreCase);
    private static readonly Regex Extension = new(@"\.(?:pdf|docx?|xlsx?|pptx?|txt|csv|jpe?g|png|gif|bmp|tiff?|mp[34]|mov|wav|m4a|avi|zip|exe|dll|html?|xml|json|js|css)$", IgnoreCase);
    private static readonly Regex Domain = new(@"\.(?:com|net|org|gov|edu|us|io|co|uk|ca|me|tv|info|biz)$", IgnoreCase);
    private static readonly Regex Interior = new(@"^[A-Za-z0-9]{2,}\.[A-Za-z0-9._]*[A-Za-z0-9]{2,}$", Default);

    public string Name => "OnlineHandle";

    // A plain word after a label is a handle only when the label says so outright ("handle of kuttaosama",
    // "username Chloe"); after "Facebook page" or "Instagram account is" the next word is just prose.
    private static readonly Regex ExplicitLabel = new(@"(?i)(?:handle|user\s*name|screen\s+name|display\s+name|named?\s*$|\bof\s*$|:\s*$)", Default);

    public IEnumerable<TextSpan> Detect(string text, DetectionContext context)
    {
        var seen = new HashSet<(int, int)>();
        foreach (Match m in Labelled.Matches(text))
            if (Plausible(m) && Add(seen, m.Groups["h"])) yield return Span(m.Groups["h"], 0.9, "User name following a platform or handle label");
        foreach (Match m in ExportAccount.Matches(text))
            if (Add(seen, m.Groups["h"])) yield return Span(m.Groups["h"], 0.95, "Account name in a platform export");
        foreach (Match m in PlatformAccount.Matches(text))
            if (Plausible(m) && Add(seen, m.Groups["h"])) yield return Span(m.Groups["h"], 0.9, "Account name following a platform label");
        foreach (Match m in ExportLine.Matches(text))
        {
            var g = m.Groups["h"];
            if (!LooksLikeHandle(g.Value) && !context.KnownHandles.Contains(g.Value)) continue;
            if (Add(seen, g)) yield return Span(g, 0.85, "Sender of a message in a platform export");
        }
        foreach (Match m in Shaped.Matches(text))
        {
            var g = m.Groups["h"];
            var v = g.Value.TrimEnd('.');
            if (context.KnownHandles.Contains(v))
            {
                if (Add(seen, g)) yield return new TextSpan(g.Index, v.Length, PiiCategory.OnlineHandle, 0.85, "Matches a user name found elsewhere in the document");
                continue;
            }
            if (!LooksLikeHandle(v)) continue;
            if (Add(seen, g)) yield return new TextSpan(g.Index, v.Length, PiiCategory.OnlineHandle, 0.7, "Looks like a user name (letters with digits, underscores or dots)");
        }
    }

    /// <summary>The token has the shape of a handle and not of a date, ID, unit, version, file name or domain.</summary>
    public static bool LooksLikeHandle(string v)
    {
        v = v.TrimEnd('.');
        if (v.Length < 5 || v.Length > 40) return false;
        if (!v.Any(char.IsLetter)) return false;
        var hasDigit = v.Any(char.IsDigit);
        var hasUnderscore = v.Contains('_');
        var interiorDot = Interior.IsMatch(v);
        if (!hasDigit && !hasUnderscore && !interiorDot) return false;
        // No lower-case letter at all is an ID, plate, serial or statute number ("M22524112", "JVK8800", "218A.1421").
        if (!v.Any(char.IsLower)) return false;
        if (!hasDigit)
        {
            // Without digits the underscore must be inside the word: "Dear_" and "Title_" are form-template blanks.
            if (hasUnderscore && !interiorDot && v.Trim('_').IndexOf('_') < 0) return false;
            // And a dotted name is a handle only when the part after the dot is lower case ("jamie.logsdon", not "Mr.Xxxxxx").
            if (!hasUnderscore && interiorDot && v[(v.IndexOf('.') + 1)..].Any(char.IsUpper)) return false;
        }
        if (Ordinal.IsMatch(v) || Unit.IsMatch(v) || Version.IsMatch(v)) return false;
        if (Extension.IsMatch(v) || Domain.IsMatch(v)) return false;
        return true;
    }

    private static bool Plausible(Match m)
    {
        var h = m.Groups["h"];
        if (LooksLikeHandle(h.Value)) return true;
        var lead = m.Value[..(h.Index - m.Index)].TrimEnd();
        return ExplicitLabel.IsMatch(lead);
    }

    private static bool Add(HashSet<(int, int)> seen, Group g) => seen.Add((g.Index, g.Length));

    private static TextSpan Span(Group g, double conf, string reason)
    {
        var v = g.Value.TrimEnd('.');
        return new TextSpan(g.Index, v.Length, PiiCategory.OnlineHandle, conf, reason);
    }
}

/// <summary>
/// Web links. A URL from a platform export or a news article embeds account IDs, media IDs or the
/// subject's name, so the whole link is offered for redaction, including the continuation lines a
/// long link is wrapped onto in a printed report.
/// </summary>
public sealed class WebAddressDetector : IPiiDetector
{
    private static readonly Regex Url = new(@"(?:https?://|www\.)[^\s<>""]+", IgnoreCase);
    // A wrapped continuation: the next line is a single token with URL punctuation and no spaces.
    private static readonly Regex Continuation = new(@"\G\r?\n[ \t]*(?=[^\s<>""]{6,}(?:\r?\n|$))(?=[^\s]*[&=?%/_.-])([^\s<>""]+)", Default);
    private static readonly Regex Mailto = new(@"<mailto:[^\s>]+>", IgnoreCase);

    public string Name => "WebAddress";

    public IEnumerable<TextSpan> Detect(string text, DetectionContext context)
    {
        foreach (Match m in Url.Matches(text))
        {
            var start = m.Index;
            var end = m.Index + m.Length;
            // Extend over wrapped lines while the previous line ends mid-token or the next line is clearly URL-ish.
            while (true)
            {
                var c = Continuation.Match(text, end);
                if (!c.Success) break;
                var prevEndsMid = "-&?=%/_".Contains(text[end - 1]);
                var nextIsUrlish = c.Groups[1].Value.Count(ch => "&=?%".Contains(ch)) >= 1 || prevEndsMid;
                if (!nextIsUrlish) break;
                end = c.Index + c.Length;
            }
            while (end > start && ".,;:)".Contains(text[end - 1])) end--;
            yield return new TextSpan(start, end - start, PiiCategory.WebAddress, 0.8, "Web link");
        }
        foreach (Match m in Mailto.Matches(text))
            yield return new TextSpan(m.Index, m.Length, PiiCategory.WebAddress, 0.8, "Mail link");
    }
}
