using FileRedact.Core.Detection;
using FileRedact.Core.Model;
using Xunit;

namespace FileRedact.Tests;

/// <summary>
/// Boxed-form values sit beneath their label and are scattered in the reading-order text; these tests lay out
/// words by position (points, top-left origin) the way a citation form's cells come out of the extractor.
/// </summary>
public class FormFieldDetectorTests
{
    private sealed record W(string Text, double X, double Y, double H = 4.4);

    /// <summary>
    /// Each word becomes its own line of page text, so nothing can be found by text patterns alone. Words at the
    /// same Y share a visual line number, as the extractor assigns them.
    /// </summary>
    private static PageModel Page(params W[] words)
    {
        var boxes = new List<WordBox>();
        var text = new System.Text.StringBuilder();
        var lines = words.Select(w => w.Y).Distinct().OrderBy(y => y).ToList();
        for (var i = 0; i < words.Length; i++)
        {
            if (i > 0) text.Append('\n');
            var w = words[i];
            boxes.Add(new WordBox
            {
                Index = i,
                Text = w.Text,
                Rect = new RectPt(w.X, w.Y, w.Text.Length * w.H * 0.6, w.H),
                TextStart = text.Length,
                Line = lines.IndexOf(w.Y),
            });
            text.Append(w.Text);
        }
        return new PageModel { Number = 1, Width = 612, Height = 792, Words = boxes, Text = text.ToString() };
    }

    private static List<(string Text, PiiCategory Category)> Run(PageModel page)
        => FormFieldDetector.Detect(page).Select(s => (page.Text.Substring(s.Start, s.Length), s.Category)).ToList();

    [Fact]
    public void Date_of_birth_in_three_boxes_beneath_the_label_is_found()
    {
        // "DATE OF BIRTH" with "SEX" as the next label to the right; the checkbox text "MALE" sits inside the SEX cell.
        // As on the real form, the layout analysis put "12" far from "20 2007" in the page text.
        var page = Page(
            new W("12", 53, 216, 5.8),
            new W("DATE", 60, 200), new W("OF", 78, 200), new W("BIRTH", 88, 200), new W("SEX", 158, 200),
            new W("20", 71, 216, 5.8), new W("2007", 95, 216, 5.8), new W("MALE", 136, 214));
        var r = Run(page);
        // "20" and "2007" are adjacent in the page text and become one span; "12" is a span of its own.
        Assert.Equal(new[] { "12", "20\n2007" }, r.Where(x => x.Category == PiiCategory.DateOfBirth).Select(x => x.Text).OrderBy(x => x));
        Assert.DoesNotContain(r, x => x.Text.Contains("MALE"));
    }

    [Fact]
    public void Location_beneath_a_location_label_stops_at_the_next_cell()
    {
        // The neighbouring "KENTUCKY RESIDENT STATUS" label is centred in its own (wide) cell, so the value of that
        // cell starts well to the left of its label; the wide gap after "WAY" marks the end of the address.
        var page = Page(
            new W("ADDRESS", 48, 119), new W("(NUMBER,", 79, 119), new W("NAME,", 110, 119), new W("SUFFIX)", 131, 119),
            new W("KENTUCKY", 449, 119), new W("RESIDENT", 483, 119), new W("STATUS", 515, 119),
            new W("176", 48, 129, 5), new W("WISELAND", 62, 129, 5), new W("WAY", 101, 129, 5),
            new W("F:", 412, 129), new W("FULL-TIME", 419, 129));
        var r = Run(page);
        Assert.Contains(r, x => x.Category == PiiCategory.Address);
        Assert.All(r, x => Assert.DoesNotContain("FULL-TIME", x.Text));
    }

    [Theory]
    [InlineData("HOME PHONE", "UNKNOWN")]
    [InlineData("DATE OF BIRTH", "MALE")]
    [InlineData("CITY", "PIONEER VILLAGE")]
    public void Values_that_do_not_fit_the_label_are_ignored(string label, string value)
    {
        var words = new List<W>();
        var x = 50.0;
        foreach (var t in label.Split(' ')) { words.Add(new W(t, x, 100)); x += t.Length * 3 + 4; }
        x = 50.0;
        foreach (var t in value.Split(' ')) { words.Add(new W(t, x, 110)); x += t.Length * 3 + 4; }
        Assert.Empty(Run(Page(words.ToArray())));
    }

    [Fact]
    public void Engine_turns_form_field_values_into_findings()
    {
        var page = Page(new W("S.", 300, 200), new W("S.", 308, 200), new W("NUMBER", 316, 200), new W("123-45-6789", 302, 212, 5.8));
        var doc = new RedactDocument { SourcePath = "form.pdf", WorkingPdfPath = "form.pdf", Pages = new[] { page } };
        var findings = new DetectionEngine().Detect(doc, new DetectionOptions());
        Assert.Contains(findings, f => f.Text == "123-45-6789" && f.Category == PiiCategory.SocialSecurityNumber && f.Accepted);
    }
}
