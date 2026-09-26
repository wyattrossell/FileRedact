using System.Text.RegularExpressions;

namespace FileRedact.Core.Detection;

internal static class RegexHelpers
{
    public const RegexOptions Default = RegexOptions.Compiled | RegexOptions.CultureInvariant;
    public const RegexOptions IgnoreCase = Default | RegexOptions.IgnoreCase;

    /// <summary>
    /// Returns true when <paramref name="context"/> matches in the window of text immediately before
    /// <paramref name="position"/>. Used to raise confidence when a label such as "SSN:" precedes a value.
    /// </summary>
    public static bool HasContextBefore(string text, int position, Regex context, int window = 40)
    {
        var start = Math.Max(0, position - window);
        var slice = text.AsSpan(start, position - start);
        return context.IsMatch(slice);
    }

    public static int DigitCount(ReadOnlySpan<char> s)
    {
        var n = 0;
        foreach (var c in s) if (char.IsDigit(c)) n++;
        return n;
    }

    public static int LetterCount(ReadOnlySpan<char> s)
    {
        var n = 0;
        foreach (var c in s) if (char.IsLetter(c)) n++;
        return n;
    }

    public static bool IsAllSameDigit(ReadOnlySpan<char> digits)
    {
        if (digits.Length == 0) return false;
        var first = digits[0];
        foreach (var c in digits) if (c != first) return false;
        return true;
    }

    public static bool LuhnValid(ReadOnlySpan<char> digits)
    {
        var sum = 0;
        var alt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (d < 0 || d > 9) return false;
            if (alt)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            alt = !alt;
        }
        return digits.Length > 0 && sum % 10 == 0;
    }

    /// <summary>Trims whitespace and trailing punctuation from a span, returning adjusted start/length.</summary>
    public static (int Start, int Length) Trim(string text, int start, int length)
    {
        var end = start + length;
        while (start < end && (char.IsWhiteSpace(text[start]) || text[start] is ':' or '#' or '-')) start++;
        while (end > start && (char.IsWhiteSpace(text[end - 1]) || text[end - 1] is ',' or '.' or ';' or ':')) end--;
        return (start, end - start);
    }
}
