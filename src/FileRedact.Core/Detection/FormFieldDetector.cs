using System.Text.RegularExpressions;
using FileRedact.Core.Detection.Detectors;
using FileRedact.Core.Model;
using static FileRedact.Core.Detection.RegexHelpers;

namespace FileRedact.Core.Detection;

/// <summary>
/// Position-based detector for boxed forms (citations, booking sheets, applications). Such forms print a small
/// label in a corner of each cell with the value beneath it, and the reading-order text often separates the two;
/// a date of birth written in three boxes ("12 | 20 | 2007") never even appears as one date string, so the text
/// detectors cannot see it. This pass finds label phrases by their word positions and takes the words sitting
/// directly below them, inside the cell, when they look like the expected kind of value.
/// </summary>
public static class FormFieldDetector
{
    /// <summary>Returns how many of the value tokens (left to right) make up the value, or 0 when they do not look like one.</summary>
    private delegate int Accept(IReadOnlyList<string> tokens);

    private sealed record Field(string[] Labels, PiiCategory Category, double Confidence, string Reason, Accept Accept);

    private static readonly Regex FullDate = new(@"^(?:\d{1,2}[/\-.]\d{1,2}[/\-.](?:\d{4}|\d{2})|\d{4}[/\-.]\d{1,2}[/\-.]\d{1,2})$", Default);
    private static readonly Regex SsnFormatted = new(@"^\d{3}-?\d{2}-?\d{4}$", Default);
    private static readonly Regex PhoneChars = new(@"^[\d()\-.+]+$", Default);
    private static readonly Regex IdToken = new(@"^[A-Z0-9\-]{5,20}$", IgnoreCase);
    private static readonly Regex StreetSuffix = new("^" + AddressDetector.Suffix + @"\.?,?$", IgnoreCase);

    // Label phrases are compared with everything but letters removed and upper-cased: "S. S. NUMBER" -> "SSNUMBER".
    private static readonly Field[] Fields =
    {
        new(new[] { "DATEOFBIRTH", "DOB", "BIRTHDATE", "DATEOFBIRTHMMDDYYYY", "DOBMMDDYYYY" },
            PiiCategory.DateOfBirth, 0.95, "Date beneath a date-of-birth form label", DateValue),
        new(new[] { "SSN", "SSNUMBER", "SSNO", "SOCIALSECURITYNUMBER", "SOCIALSECURITYNO", "SOCIALSECURITY", "SOCSECNO" },
            PiiCategory.SocialSecurityNumber, 0.95, "Number beneath a Social Security number form label", SsnValue),
        new(new[] { "IDNUMBER", "IDNO", "DLNUMBER", "DLNO", "DL", "OLN", "OLNUMBER", "DRIVERSLICENSENUMBER", "DRIVERSLICENSENO", "DRIVERLICENSENUMBER", "LICENSENUMBER", "LICENSENO", "OPERATORSLICENSENUMBER", "STATEIDNUMBER" },
            PiiCategory.DriverLicense, 0.9, "Identifier beneath a license / ID number form label", IdValue),
        new(new[] { "HOMEPHONE", "PHONE", "PHONENUMBER", "PHONENO", "TELEPHONE", "TELEPHONENUMBER", "CELLPHONE", "CELL", "MOBILE", "WORKPHONE", "EMERGENCYPHONE", "CONTACTNUMBER", "CONTACTPHONE" },
            PiiCategory.PhoneNumber, 0.9, "Number beneath a phone form label", PhoneValue),
        new(new[] { "ADDRESS", "HOMEADDRESS", "RESIDENCEADDRESS", "STREETADDRESS", "ADDRESSNUMBERNAMESUFFIX", "ADDRESSNUMBERSTREETSUFFIX", "EXACTLOCATIONOFVIOLATION", "EXACTLOCATIONOFARREST", "LOCATIONOFVIOLATION", "LOCATIONOFARREST", "LOCATIONOFOFFENSE", "LOCATIONOFINCIDENT", "INCIDENTLOCATION", "OFFENSELOCATION", "LOCATIONOFOCCURRENCE" },
            PiiCategory.Address, 0.75, "Location beneath an address / location form label", LocationValue),
    };

    private static readonly Dictionary<string, Field> ByLabel = Fields
        .SelectMany(f => f.Labels.Select(l => (l, f)))
        .ToDictionary(x => x.l, x => x.f, StringComparer.Ordinal);

    private const int MaxLabelWords = 5;

    public static IEnumerable<TextSpan> Detect(PageModel page)
    {
        var words = page.Words;
        if (words.Count == 0) yield break;
        var keys = words.Select(w => new string(w.Text.Where(char.IsLetter).ToArray()).ToUpperInvariant()).ToList();

        foreach (var line in words.Select((w, i) => (w, i)).GroupBy(x => x.w.Line))
        {
            var ordered = line.OrderBy(x => x.w.Rect.Left).Select(x => x.i).ToList();
            var pos = 0;
            while (pos < ordered.Count)
            {
                var matched = false;
                // Longest phrase first, so "ADDRESS (NUMBER, NAME, SUFFIX)" is one label rather than "ADDRESS" plus noise.
                for (var len = Math.Min(MaxLabelWords, ordered.Count - pos); len >= 1 && !matched; len--)
                {
                    var run = ordered.GetRange(pos, len);
                    var key = string.Concat(run.Select(i => keys[i]));
                    if (key.Length == 0 || !ByLabel.TryGetValue(key, out var field)) continue;
                    foreach (var span in ValueSpans(page, run.Select(i => words[i]).ToList(), field))
                        yield return span;
                    pos += len;
                    matched = true;
                }
                if (!matched) pos++;
            }
        }
    }

    private static IEnumerable<TextSpan> ValueSpans(PageModel page, List<WordBox> label, Field field)
    {
        var box = label.Select(w => w.Rect).Aggregate((a, b) => a.Union(b));
        var labelSet = new HashSet<WordBox>(label);

        // The cell reaches from the nearest word on the label's line to its left to the nearest one on its right.
        // Labels are often centred in their cell while values are left-aligned, so the label's own edges are no guide.
        double left = 0, right = page.Width;
        foreach (var w in page.Words)
        {
            if (labelSet.Contains(w) || w.Rect.CenterY < box.Top || w.Rect.CenterY > box.Bottom) continue;
            if (w.Rect.Right <= box.Left + 1) left = Math.Max(left, w.Rect.Right);
            else if (w.Rect.Left >= box.Right - 1) right = Math.Min(right, w.Rect.Left);
        }

        // Candidate values: the first row of words entirely below the label, inside the cell. (A rotated margin
        // heading can start a point below the label's centre line, so "below" means below the label's bottom.)
        var window = box.Bottom + Math.Max(3 * box.Height, 16);
        var below = page.Words
            .Where(w => !labelSet.Contains(w) && w.Rect.Top >= box.Bottom - 0.5 && w.Rect.Top <= window)
            .Where(w => w.Rect.Left + w.Rect.Width / 2 >= left && w.Rect.Left + w.Rect.Width / 2 <= right)
            .ToList();
        if (below.Count == 0) yield break;
        var rowTop = below.Min(w => w.Rect.Top);
        var ordered = below.Where(w => w.Rect.Top <= rowTop + 4).OrderBy(w => w.Rect.Left).ToList();

        // Neighbouring labels are often centred in their own cells, so the cell edge found above can be too
        // generous: the value starts at the first word that reaches under the label ("40218 | 4/2/2009" beneath
        // "ZIP CODE: | DATE OF BIRTH:") and ends at the first wide gap between words.
        var first = ordered.FindIndex(w => w.Rect.Right > box.Left - 4);
        if (first < 0) first = 0;
        var row = new List<WordBox> { ordered[first] };
        for (var i = first + 1; i < ordered.Count; i++)
        {
            if (ordered[i].Rect.Left - row[^1].Rect.Right > Math.Max(16, 3 * ordered[i].Rect.Height)) break;
            row.Add(ordered[i]);
        }

        // The value may not be the first word in the row: a neighbouring cell's value can sit inside the
        // computed cell edges ("40218 4/2/2009" beneath "ZIP CODE: | DATE OF BIRTH:").
        var texts = row.Select(w => w.Text).ToList();
        var offset = 0;
        var count = 0;
        for (; offset < row.Count; offset++)
        {
            count = field.Accept(texts.Skip(offset).ToList());
            if (count > 0) break;
        }
        if (count <= 0) yield break;

        // One span per run of words that are adjacent in the page text ("20\n2007" is one span, "12" elsewhere another).
        var chosen = row.Skip(offset).Take(count).OrderBy(w => w.TextStart).ToList();
        var start = chosen[0].TextStart;
        var end = chosen[0].TextEnd;
        for (var i = 1; i < chosen.Count; i++)
        {
            if (chosen[i].TextStart == end + 1) { end = chosen[i].TextEnd; continue; }
            yield return new TextSpan(start, end - start, field.Category, field.Confidence, field.Reason);
            start = chosen[i].TextStart;
            end = chosen[i].TextEnd;
        }
        yield return new TextSpan(start, end - start, field.Category, field.Confidence, field.Reason);
    }

    private static bool IsNum(string s, int min, int max) => s.Length >= min && s.Length <= max && s.All(char.IsDigit);

    private static int DateValue(IReadOnlyList<string> t)
    {
        if (t.Count >= 1 && FullDate.IsMatch(t[0])) return 1;
        // Month, day and year in three separate boxes.
        if (t.Count >= 3 && IsNum(t[0], 1, 2) && IsNum(t[1], 1, 2) && (IsNum(t[2], 4, 4) || IsNum(t[2], 2, 2)))
        {
            var month = int.Parse(t[0]);
            var day = int.Parse(t[1]);
            if (month is >= 1 and <= 12 && day is >= 1 and <= 31) return 3;
        }
        return 0;
    }

    private static int SsnValue(IReadOnlyList<string> t)
    {
        if (t.Count >= 1 && SsnFormatted.IsMatch(t[0])) return 1;
        if (t.Count >= 3 && IsNum(t[0], 3, 3) && IsNum(t[1], 2, 2) && IsNum(t[2], 4, 4)) return 3;
        return 0;
    }

    private static int PhoneValue(IReadOnlyList<string> t)
    {
        var digits = 0;
        for (var i = 0; i < t.Count && i < 3; i++)
        {
            if (!PhoneChars.IsMatch(t[i])) break;
            digits += DigitCount(t[i]);
            if (digits == 10 || (digits == 11 && t[0].StartsWith('1'))) return i + 1;
        }
        return 0;
    }

    private static int IdValue(IReadOnlyList<string> t)
        => t.Count >= 1 && IdToken.IsMatch(t[0]) && DigitCount(t[0]) >= 2 ? 1 : 0;

    private static int LocationValue(IReadOnlyList<string> t)
    {
        // A street address or a location such as "N PRESTON HWY": needs a house number or a street-type word
        // at the end ("City, ST Zip" on a letter template has neither).
        var n = Math.Min(t.Count, 6);
        if (n == 0) return 0;
        var plausible = DigitCount(t[0]) > 0 || StreetSuffix.IsMatch(t[n - 1]);
        return plausible ? n : 0;
    }
}
