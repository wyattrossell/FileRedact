using System.Text.RegularExpressions;
using FileRedact.Core.Model;
using static FileRedact.Core.Detection.RegexHelpers;
using static FileRedact.Core.Detection.Detectors.IdHelpers;

namespace FileRedact.Core.Detection.Detectors;

/// <summary>Social Security numbers, formatted or (with a label nearby) unformatted.</summary>
public sealed class SsnDetector : IPiiDetector
{
    private static readonly Regex Formatted = new(@"(?<!\d)(?!000|666|9\d\d)\d{3}[- ](?!00)\d{2}[- ](?!0000)\d{4}(?!\d)", Default);
    private static readonly Regex Plain = new(@"(?<!\d)(?!000|666|9\d\d)\d{3}(?!00)\d{2}(?!0000)\d{4}(?!\d)", Default);
    private static readonly Regex Context = new(@"\b(?:SSN|SS\s?#|SS\s?No|SSAN|Social\s+Security|Soc(?:ial)?\.?\s*Sec(?:urity)?)(?![A-Za-z])", IgnoreCase);

    public string Name => "SSN";

    public IEnumerable<TextSpan> Detect(string text, DetectionContext context)
    {
        foreach (Match m in Formatted.Matches(text))
        {
            var ctx = HasContextBefore(text, m.Index, Context);
            yield return new TextSpan(m.Index, m.Length, PiiCategory.SocialSecurityNumber, ctx ? 0.98 : 0.9,
                ctx ? "SSN format with label" : "SSN format (###-##-####)");
        }
        foreach (Match m in Plain.Matches(text))
        {
            if (HasContextBefore(text, m.Index, Context, 50))
                yield return new TextSpan(m.Index, m.Length, PiiCategory.SocialSecurityNumber, 0.9, "9 digits following an SSN label");
        }
    }
}

/// <summary>Dates. Flagged as date of birth when a DOB label precedes them, otherwise as a low-confidence generic date.</summary>
public sealed class DateDetector : IPiiDetector
{
    private const string Months = @"(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|June?|July?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)";
    private static readonly Regex[] Patterns =
    {
        // The year may wrap onto the next line in a narrative: "(DOB 12-22-\n2001)".
        new(@"(?<![\d/])(?:0?[1-9]|1[0-2])[/\-.](?:0?[1-9]|[12]\d|3[01])[/\-.][ \t]*(?:\r?\n[ \t]*)?(?:19|20)\d{2}(?![\d/])", Default),
        new(@"(?<![\d/])(?:0?[1-9]|1[0-2])[/\-.](?:0?[1-9]|[12]\d|3[01])[/\-.]\d{2}(?![\d/])", Default),
        new(@"(?<![\d/])(?:19|20)\d{2}[/\-.](?:0?[1-9]|1[0-2])[/\-.](?:0?[1-9]|[12]\d|3[01])(?![\d/])", Default),
        new(@"\b" + Months + @"\.?\s+\d{1,2}(?:st|nd|rd|th)?,?\s+(?:19|20)\d{2}\b", IgnoreCase),
        new(@"\b\d{1,2}(?:st|nd|rd|th)?\s+" + Months + @"\.?,?\s+(?:19|20)\d{2}\b", IgnoreCase),
    };
    private static readonly Regex DobContext = new(@"\b(?:DOB|D\.O\.B\.?|date\s+of\s+birth|birth\s*date|birthdate|born|birthday|B/D|age/DOB|DOB/Age)\b", IgnoreCase);

    public string Name => "Date";

    public IEnumerable<TextSpan> Detect(string text, DetectionContext context)
    {
        foreach (var re in Patterns)
        {
            foreach (Match m in re.Matches(text))
            {
                if (HasContextBefore(text, m.Index, DobContext, 45))
                    yield return new TextSpan(m.Index, m.Length, PiiCategory.DateOfBirth, 0.95, "Date following a date-of-birth label");
                else
                    yield return new TextSpan(m.Index, m.Length, PiiCategory.Date, 0.4, "Date without a DOB label (review)");
            }
        }
    }
}

public sealed class PhoneDetector : IPiiDetector
{
    // A number wrapped at a line end ("502-\n340-0773", "502-664\n-3039") is still one number.
    private static readonly Regex Phone = new(@"(?<![\d-])(?:\+?1[\s.-]?)?(?:\(\d{3}\)\s?|\d{3}(?:[.\-][ \t]*(?:\r?\n[ \t]*)?|\s))\d{3}(?:[ \t]*(?:\r?\n[ \t]*)?[.\-][ \t]*|\s)\d{4}(?!\d)", Default);
    private static readonly Regex Plain10 = new(@"(?<!\d)[2-9]\d{9}(?!\d)", Default);
    private static readonly Regex Context = new(@"\b(?:phone|tel(?:ephone)?|cell|mobile|fax|ph|contact|call|home|work|pager|number|no\.?|#)(?![A-Za-z])", IgnoreCase);

    public string Name => "Phone";

    public IEnumerable<TextSpan> Detect(string text, DetectionContext context)
    {
        foreach (Match m in Phone.Matches(text))
            yield return new TextSpan(m.Index, m.Length, PiiCategory.PhoneNumber, 0.9, "Phone number format");
        foreach (Match m in Plain10.Matches(text))
        {
            if (HasContextBefore(text, m.Index, Context, 30))
                yield return new TextSpan(m.Index, m.Length, PiiCategory.PhoneNumber, 0.7, "10 digits following a phone label");
            // "5024924699" typed into a chat: no label, but the area code is one already seen in this document.
            else if (context.KnownAreaCodes.Contains(m.Value[..3]))
                yield return new TextSpan(m.Index, m.Length, PiiCategory.PhoneNumber, 0.7, "10 digits with an area code found elsewhere in the document");
        }
    }
}

public sealed class EmailDetector : IPiiDetector
{
    private static readonly Regex Email = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", Default);
    public string Name => "Email";
    public IEnumerable<TextSpan> Detect(string text, DetectionContext context)
    {
        foreach (Match m in Email.Matches(text))
            yield return new TextSpan(m.Index, m.Length, PiiCategory.EmailAddress, 0.97, "Email address");
    }
}

public sealed class AddressDetector : IPiiDetector
{
    internal const string States = @"(?:A[LKZR]|C[AOT]|D[EC]|FL|GA|HI|I[DLNA]|K[SY]|LA|M[EDAINSOT]|N[EVHJMYCD]|O[HKR]|PA|RI|S[CD]|T[NX]|UT|V[TA]|W[AVIY])";
    internal const string Suffix = @"(?:Street|St|Avenue|Ave|Road|Rd|Boulevard|Blvd|Lane|Ln|Drive|Dr|Court|Ct|Circle|Cir|Way|Place|Pl|Terrace|Ter|Highway|Hwy|Parkway|Pkwy|Trail|Trl|Loop|Run|Pike|Route|Rte|Square|Sq|Alley|Aly|Crossing|Xing|Path|Point|Pt|Ridge|Rdg|Row|Bend|Cove|Cv|Creek|Crk|Hill|Hls|Park|Plaza|Plz|Turnpike|Tpke|Expressway|Expy|Freeway|Fwy|Landing|Lndg|Manor|Mnr|Meadows|Mdws|Station|Sta|Valley|Vly|View|Vw|Village|Vlg|Walk|Estates|Ests|Heights|Hts|Gardens|Gdns|Grove|Grv|Harbor|Hbr|Island|Junction|Jct|Lake|Lk|Mount|Mt|Orchard|Orch|Pass|Ranch|Rnch|Shore|Shr|Spring|Spg|Springs|Spgs|Summit|Smt|Trace|Trce|Vista|Vis)";
    // Words that are never part of a street name; without this "61 and Summit Drive" in a narrative is an address.
    private const string NotStreetWord = @"(?!(?:and|or|of|the|at|to|in|on|for|from|by|with|is|was|are|were|a|an)[ \t])";
    private const string Unit = @"(?:[ \t]*,?[ \t]*(?:Apt|Apartment|Unit|Suite|Ste|Bldg|Building|Floor|Fl|Rm|Room|Lot|Space|Spc|#)\.?(?![A-Za-z])[ \t]*[A-Za-z0-9\-]+)?";
    private const string CityStateZip = @"(?:[ \t]*,?[ \t]*(?:\r?\n)?[ \t]*[A-Za-z][A-Za-z.'\-]+(?:[ \t]+[A-Za-z][A-Za-z.'\-]+){0,3}[ \t]*,?[ \t]+" + States + @"\.?[ \t]+\d{5}(?:-\d{4})?)?";

    // The number and street stay on one line ([ \t] rather than \s): on boxed forms the reading-order text puts
    // unrelated cells on consecutive lines, and "1:30 PM\n\n2026\n\nBULLITT\n\nCOURT" must not become an address.
    // A time such as "1:30" is not a house number either.
    // Deliberately not RegexOptions.Compiled: the .NET compiled engine throws IndexOutOfRangeException on this
    // pattern for inputs such as "134 S. Ct. 2012 and Mullenix", which made whole documents fail to open.
    // The interpreter handles the same input correctly.
    // A narrative may wrap the address before the street type ("797 Hillview\nBoulevard", "13627 Faye Amick\nRd");
    // that is allowed only when the street name is made of words, so form cells ("20 2007\nPLACE") stay apart.
    private static readonly Regex Street = new(
        @"(?<![\w/])(?<!\d:)\d{1,6}[A-Za-z]?[ \t]+(?:(?:N|S|E|W|NE|NW|SE|SW|North|South|East|West)\.?[ \t]+)?(?:(?:" + NotStreetWord + @"[A-Za-z0-9'.\-]+[ \t]+){1,4}?|(?:" + NotStreetWord + @"[A-Za-z][A-Za-z'\-]*[ \t]+){0,3}" + NotStreetWord + @"[A-Za-z][A-Za-z'\-]*[ \t]*\r?\n[ \t]*)" + Suffix + @"\b\.?(?:[ \t]+(?:N|S|E|W|NE|NW|SE|SW))?\b" + Unit + CityStateZip,
        (IgnoreCase & ~RegexOptions.Compiled) | RegexOptions.ExplicitCapture);
    private static readonly Regex PoBox = new(@"\bP\.?\s?O\.?\s?Box\s+\d+" + CityStateZip, IgnoreCase | RegexOptions.ExplicitCapture);
    // "the residence at 111 Norwood", "lives at 4517 Greymont": a number and a capitalised street name with no
    // street-type word, trusted only when a location phrase introduces it.
    private static readonly Regex LocatedAt = new(
        @"\b(?:at|located\s+at|residence\s+(?:at|of)|address\s+(?:at|of|is|for)|lives?\s+(?:at|on)|residing\s+at|resides\s+at|in\s+front\s+of|to)\s+(?<addr>\d{1,6}[A-Za-z]?\s+(?:(?:N|S|E|W|NE|NW|SE|SW|North|South|East|West)\.?\s+)?[A-Z][A-Za-z'\-]{2,}(?:\s+[A-Z][A-Za-z'\-]{2,}){0,2})(?![A-Za-z0-9])",
        Default | RegexOptions.ExplicitCapture);
    // Chat spelling with the street type glued on: "831 hillviewblvd".
    private static readonly Regex Glued = new(
        @"(?<![\w/])(?<!\d:)\d{1,6}[ \t]+[A-Za-z]{4,}(?:blvd|boulevard|street|avenue|road|drive|lane|court|ave|hwy|pkwy|pike|trail|circle)\b",
        IgnoreCase);
    private static readonly Regex CityLine = new(@"\b[A-Z][A-Za-z.'\-]+(?:\s+[A-Z][A-Za-z.'\-]+){0,3}\s*,\s*" + States + @"\.?\s+\d{5}(?:-\d{4})?\b", Default | RegexOptions.ExplicitCapture);
    private static readonly Regex Context = new(@"\b(?:address|addr|residence|resides|residing|lives\s+at|home|located\s+at|location|street)\b", IgnoreCase);

    public string Name => "Address";

    public IEnumerable<TextSpan> Detect(string text, DetectionContext context)
    {
        foreach (Match m in Street.Matches(text))
        {
            var (s, l) = Trim(text, m.Index, m.Length);
            var ctx = HasContextBefore(text, m.Index, Context, 40);
            yield return new TextSpan(s, l, PiiCategory.Address, ctx ? 0.95 : 0.85, "Street address");
        }
        foreach (Match m in PoBox.Matches(text))
        {
            var (s, l) = Trim(text, m.Index, m.Length);
            yield return new TextSpan(s, l, PiiCategory.Address, 0.9, "PO Box");
        }
        foreach (Match m in LocatedAt.Matches(text))
        {
            var g = m.Groups["addr"];
            // "at 0304 hours", "at 2:30 am": a time or count, not a house number.
            var word = g.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).First();
            if (Regex.IsMatch(word, @"^(?:hours|hrs|am|pm|minutes|seconds|feet|ft|miles|percent|dollars|degrees|deg|min|seconds|shots|rounds|people|days|years|months)$", RegexOptions.IgnoreCase)) continue;
            var (s, l) = Trim(text, g.Index, g.Length);
            yield return new TextSpan(s, l, PiiCategory.Address, 0.7, "Number and street name after a location phrase");
        }
        foreach (Match m in Glued.Matches(text))
        {
            var (s, l) = Trim(text, m.Index, m.Length);
            yield return new TextSpan(s, l, PiiCategory.Address, 0.75, "Street address (street type written as one word)");
        }
        foreach (Match m in CityLine.Matches(text))
        {
            var (s, l) = Trim(text, m.Index, m.Length);
            yield return new TextSpan(s, l, PiiCategory.Address, 0.75, "City, state and ZIP");
        }
    }
}

/// <summary>Driver's license / state ID numbers. Formats vary by state, so this relies on a nearby label.</summary>
public sealed class DriverLicenseDetector : IPiiDetector
{
    private static readonly Regex Labelled = new(
        @"\b(?:DL|D\.L\.|DLN|OLN|DL\s*No|driver'?s?\s+licen[sc]e|operator'?s?\s+licen[sc]e|licen[sc]e\s+(?:no|number|num|#)|state\s+id(?:entification)?(?:\s+(?:no|number|card|#))?|ID\s+(?:no|number|card|#))\.?\s*(?:number|no\.?|num\.?|#)?\s*[:#\-]?\s*(?<id>[A-Z0-9][A-Z0-9 \-]{3,18}[A-Z0-9])(?![A-Za-z0-9])",
        IgnoreCase);

    public string Name => "DriverLicense";

    public IEnumerable<TextSpan> Detect(string text, DetectionContext context)
    {
        foreach (Match m in Labelled.Matches(text))
        {
            var g = m.Groups["id"];
            var (start, length) = TrimAlphaTokens(text, g.Index, g.Length);
            if (length == 0) continue;
            var v = text.AsSpan(start, length);
            if (DigitCount(v) < 2) continue;
            // An address or state name that happens to follow "ID" is not a licence number.
            if (LetterCount(v) > 6) continue;
            yield return new TextSpan(start, length, PiiCategory.DriverLicense, 0.9, "Identifier following a driver's license / ID label");
        }
    }
}

internal static class IdHelpers
{
    /// <summary>
    /// Keeps the first run of space-separated tokens that contain a digit ("12345678 STATE TX" -> "12345678",
    /// "B95674861 DOB 04" -> "B95674861"): the identifier ends where the next label begins.
    /// </summary>
    public static (int Start, int Length) TrimAlphaTokens(string text, int start, int length)
    {
        var value = text.Substring(start, length);
        var parts = value.Split(' ');
        var first = 0;
        while (first < parts.Length && DigitCount(parts[first]) == 0) first++;
        var last = first;
        while (last + 1 < parts.Length && DigitCount(parts[last + 1]) > 0) last++;
        if (first >= parts.Length) return (start, 0);
        var offset = parts.Take(first).Sum(p => p.Length + 1);
        var kept = string.Join(' ', parts.Skip(first).Take(last - first + 1));
        return (start + offset, kept.Length);
    }
}

public sealed class PassportDetector : IPiiDetector
{
    private static readonly Regex Labelled = new(@"\bpassport\s*(?:no\.?|number|num\.?|#)?\s*[:#\-]?\s*(?<id>[A-Z0-9]{6,9})\b", IgnoreCase);
    public string Name => "Passport";
    public IEnumerable<TextSpan> Detect(string text, DetectionContext context)
    {
        foreach (Match m in Labelled.Matches(text))
        {
            var g = m.Groups["id"];
            if (DigitCount(g.Value) < 4) continue;
            yield return new TextSpan(g.Index, g.Length, PiiCategory.PassportNumber, 0.9, "Identifier following a passport label");
        }
    }
}

public sealed class FinancialDetector : IPiiDetector
{
    private static readonly Regex Card = new(@"(?<!\d)(?:\d[ \-]?){12,18}\d(?!\d)", Default);
    private static readonly Regex Labelled = new(@"\b(?:account|acct|routing|ABA|IBAN|card|debit|credit\s+card|checking|savings)\s*(?:no\.?|number|num\.?|#)?\s*[:#\-]?\s*(?<id>[A-Z]{0,2}\d[\d \-]{5,30}\d)(?!\d)", IgnoreCase);

    public string Name => "Financial";

    public IEnumerable<TextSpan> Detect(string text, DetectionContext context)
    {
        foreach (Match m in Card.Matches(text))
        {
            var digits = new string(m.Value.Where(char.IsDigit).ToArray());
            if (digits.Length is < 13 or > 19) continue;
            if (IsAllSameDigit(digits)) continue;
            if (!LuhnValid(digits)) continue;
            yield return new TextSpan(m.Index, m.Length, PiiCategory.FinancialAccount, 0.9, "Payment card number (Luhn valid)");
        }
        foreach (Match m in Labelled.Matches(text))
        {
            var g = m.Groups["id"];
            if (DigitCount(g.Value) < 6) continue;
            yield return new TextSpan(g.Index, g.Length, PiiCategory.FinancialAccount, 0.85, "Number following an account label");
        }
    }
}

public sealed class VehicleDetector : IPiiDetector
{
    private static readonly Regex Vin = new(@"\b[A-HJ-NPR-Z0-9]{17}\b", Default);
    private static readonly Regex VinContext = new(@"\bVIN\b|vehicle\s+id", IgnoreCase);
    // At most one line break between the label and the value, so a value can sit on the next line of a form.
    private const string Gap = @"[ \t]*(?:\r?\n[ \t]*)?";
    // A form may print sub-labels, the state and the year between the label and the plate itself:
    // "REGISTRATION: STATE, YEAR, NUMBER" / "KY 2027 Y5C400". The label must be a whole word ("LIC" is not "LICENSE").
    private static readonly Regex Plate = new(
        @"\b(?:licen[sc]e\s+plate|plate|tag|LP|LIC|registration)(?![A-Za-z])" + Gap +
        @"(?:(?:(?:no\.?|number|num\.?|#|state|st\.?|year|yr\.?|(?:19|20)\d{2}|" + AddressDetector.States + @")(?![A-Za-z0-9])|[:,/\-])" + Gap + @")*" +
        @"(?<id>[A-Z0-9]{2,4}[\- ]?[A-Z0-9]{2,4})(?![A-Za-z0-9])",
        IgnoreCase);

    public string Name => "Vehicle";

    public IEnumerable<TextSpan> Detect(string text, DetectionContext context)
    {
        foreach (Match m in Vin.Matches(text))
        {
            if (DigitCount(m.Value) < 3 || LetterCount(m.Value) < 3) continue;
            var ctx = HasContextBefore(text, m.Index, VinContext, 30);
            yield return new TextSpan(m.Index, m.Length, PiiCategory.VehicleIdentifier, ctx ? 0.97 : 0.8, "Vehicle identification number");
        }
        foreach (Match m in Plate.Matches(text))
        {
            var g = m.Groups["id"];
            // Plates carry digits; a word after the label ("REGISTRATION PLATES", "license plate lights") does not.
            if (DigitCount(g.Value) == 0) continue;
            yield return new TextSpan(g.Index, g.Length, PiiCategory.VehicleIdentifier, 0.85, "License plate following a plate label");
        }
    }
}

/// <summary>FBI numbers (UCN), state identification numbers, booking / inmate / offender numbers.</summary>
public sealed class CriminalJusticeIdDetector : IPiiDetector
{
    private static readonly Regex Labelled = new(
        @"\b(?:FBI|UCN|SID|SBI|OCA|TCN|DOC|DCN|NCIC|inmate|booking|offender|arrest|jacket|MNI|CCH|state\s+identification)\s*(?:no\.?|number|num\.?|#|id)?\s*[:#\-]?\s*(?<id>[A-Z0-9][A-Z0-9\-]{4,15})(?![A-Za-z0-9])",
        IgnoreCase);

    // Officer badge / ID numbers: "Badge #123", or "BADGE/I.D. NUMBER" with the value on the next line of a form.
    private static readonly Regex Badge = new(
        @"\bbadge(?:[ \t]*/[ \t]*|[ \t]+)?(?:I\.?[ \t]?D\.?)?[ \t]*(?:no\.?|number|num\.?|#)?[ \t]*[:#\-]?[ \t]*(?:\r?\n[ \t]*)?(?<id>\d{2,8})(?!\d)",
        IgnoreCase);

    public string Name => "CriminalJusticeId";

    public IEnumerable<TextSpan> Detect(string text, DetectionContext context)
    {
        foreach (Match m in Labelled.Matches(text))
        {
            var g = m.Groups["id"];
            if (DigitCount(g.Value) < 3) continue;
            yield return new TextSpan(g.Index, g.Length, PiiCategory.CriminalJusticeIdentifier, 0.85, "Identifier following a criminal-justice record label");
        }
        foreach (Match m in Badge.Matches(text))
        {
            var g = m.Groups["id"];
            yield return new TextSpan(g.Index, g.Length, PiiCategory.CriminalJusticeIdentifier, 0.8, "Badge number following a badge label");
        }
    }
}

public sealed class PlaceOfBirthDetector : IPiiDetector
{
    private static readonly Regex Labelled = new(@"\b(?i:place\s+of\s+birth|POB|birthplace|born\s+in|birth\s+place)\b\s*[:#\-]?\s*(?<v>[A-Z][A-Za-z.'\-]+(?:,?[ ][A-Z][A-Za-z.'\-]+(?!:)){0,3})", Default);
    public string Name => "PlaceOfBirth";
    public IEnumerable<TextSpan> Detect(string text, DetectionContext context)
    {
        foreach (Match m in Labelled.Matches(text))
        {
            var g = m.Groups["v"];
            var (s, l) = Trim(text, g.Index, g.Length);
            yield return new TextSpan(s, l, PiiCategory.PlaceOfBirth, 0.8, "Value following a place-of-birth label");
        }
    }
}

public sealed class MaidenNameDetector : IPiiDetector
{
    private static readonly Regex Labelled = new(@"\b(?:mother'?s\s+)?maiden\s+name\b\s*[:#\-]?\s*(?<v>[A-Z][A-Za-z'\-]+)", IgnoreCase);
    public string Name => "MaidenName";
    public IEnumerable<TextSpan> Detect(string text, DetectionContext context)
    {
        foreach (Match m in Labelled.Matches(text))
        {
            var g = m.Groups["v"];
            yield return new TextSpan(g.Index, g.Length, PiiCategory.MaidenName, 0.85, "Value following a maiden-name label");
        }
    }
}

public sealed class IpAddressDetector : IPiiDetector
{
    private static readonly Regex Ip4 = new(@"(?<![\d.])(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(?:\.(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}(?![\d.])", Default);
    public string Name => "IpAddress";
    public IEnumerable<TextSpan> Detect(string text, DetectionContext context)
    {
        foreach (Match m in Ip4.Matches(text))
            yield return new TextSpan(m.Index, m.Length, PiiCategory.IpAddress, 0.7, "IPv4 address");
    }
}

/// <summary>Literal terms supplied by the user (e.g. a specific name or case number) - always matched.</summary>
public sealed class CustomTermDetector : IPiiDetector
{
    public string Name => "CustomTerm";
    public IEnumerable<TextSpan> Detect(string text, DetectionContext context)
    {
        foreach (var term in context.CustomTerms)
        {
            if (string.IsNullOrWhiteSpace(term)) continue;
            var re = BuildTermRegex(term);
            foreach (Match m in re.Matches(text))
                yield return new TextSpan(m.Index, m.Length, PiiCategory.CustomTerm, 1.0, $"Custom term \"{term}\"");
        }
    }

    public static Regex BuildTermRegex(string term)
    {
        var t = term.Trim();
        // Allow any whitespace (including line breaks) between the words of the term.
        var pattern = string.Join(@"\s+", t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));
        if (char.IsLetterOrDigit(t[0])) pattern = @"(?<![A-Za-z0-9])" + pattern;
        if (char.IsLetterOrDigit(t[^1])) pattern += @"(?![A-Za-z0-9])";
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
