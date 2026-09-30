using FileRedact.Core.Detection;
using FileRedact.Core.Detection.Detectors;
using FileRedact.Core.Model;
using Xunit;

namespace FileRedact.Tests;

/// <summary>User names, web links and the document-wide passes that grew out of an 83-page homicide file.</summary>
public class OnlineDetectorTests
{
    private static List<(string Text, PiiCategory Category, double Confidence)> Run(IPiiDetector d, string text, DetectionContext? ctx = null)
        => d.Detect(text, ctx ?? new DetectionContext()).Select(s => (text.Substring(s.Start, s.Length), s.Category, s.Confidence)).ToList();

    [Theory]
    [InlineData("cole.2943 (Instagram: 52256127893)", "cole.2943")]
    [InlineData("bigmoney502__ (Instagram: 59282730515)", "bigmoney502__")]
    [InlineData("the Instagram handle of kuttaosama, this account", "kuttaosama")]
    [InlineData("a screen name or username of go_fvck_yourself23, this Instagram", "go_fvck_yourself23")]
    [InlineData("the Instagram account bigmoney502__ it was identified", "bigmoney502__")]
    [InlineData("jasonlannan20- Fri Dec 13 05:05:51 UTC 2024- Yea right", "jasonlannan20")]
    [InlineData("dylan_barness- Yall heard Jaydin got pinched", "dylan_barness")]
    [InlineData("Search warrant on Katiss_spammm- no return", "Katiss_spammm")]
    [InlineData("Search warrant on 444.Kalii- data in case folder", "444.Kalii")]
    [InlineData("that conversation is as follows kaitlynm1751 wrote", "kaitlynm1751")]
    public void User_names_are_detected(string text, string expected)
    {
        Assert.Contains(Run(new OnlineHandleDetector(), text), x => x.Text == expected && x.Category == PiiCategory.OnlineHandle);
    }

    [Theory]
    [InlineData("a screen shot of a Facebook page talking about the pursuit")]
    [InlineData("listed on Lannan’s Instagram account is saved as a contact")]
    [InlineData("KRS CODE: 218A.1421(3) DEGREE: F")]
    [InlineData("LICENSE/ID NUMBER: M22524112")]
    [InlineData("report number LMPD24135091 and case HPD24-000774")]
    [InlineData("linked_media/unified_message_484253144649899.jpg")]
    [InlineData("on 2024-10-31 at 05:41:09 UTC the 100th time, 500mg, v1.3.1")]
    [InlineData("see www.example.com or wlky.com")]
    [InlineData("describe how you handle permits and open records day to day")]
    [InlineData("Dear_ Title_ Mr.Xxxxxx Cabinet_")]
    [InlineData("Crisis%20Numbers%20City%20stress")]
    public void Ordinary_tokens_are_not_user_names(string text)
    {
        Assert.Empty(Run(new OnlineHandleDetector(), text));
    }

    [Fact]
    public void Known_user_names_are_found_without_context()
    {
        var ctx = new DetectionContext { KnownHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "kuttaosama" } };
        Assert.Contains(Run(new OnlineHandleDetector(), "Search warrant on kuttaosama - data in case folder", ctx), x => x.Text == "kuttaosama");
    }

    [Fact]
    public void Web_links_are_detected_including_wrapped_continuation_lines()
    {
        const string text = "URL\nhttps://interncache-ftw.fbcdn.net/v/t69.46293-2/465011599_484253147983232_n.?stp=dst-\njpg_p843x403&ccb=1-\n7&_nc_sid=ed1755&efg=eyJ1cmxnZW4iOiJwaHBfdXJs\nProduct Type\nREPLAYABLE";
        var r = Run(new WebAddressDetector(), text);
        var link = Assert.Single(r);
        Assert.StartsWith("https://interncache-ftw", link.Text);
        Assert.EndsWith("eyJ1cmxnZW4iOiJwaHBfdXJs", link.Text);
        Assert.DoesNotContain("Product", link.Text);
    }

    [Fact]
    public void Short_web_link_stops_at_the_end_of_its_line()
    {
        var r = Run(new WebAddressDetector(), "sent https://www.wlky.com/article/hillview-teen-murdered/63087741\nShare\nDate Created");
        Assert.Equal("https://www.wlky.com/article/hillview-teen-murdered/63087741", Assert.Single(r).Text);
    }

    [Theory]
    [InlineData("that phone number is 502-\n340-0773 that conversation", "502-\n340-0773")]
    [InlineData("a saved number of 502-664\n-3039, the contact", "502-664\n-3039")]
    public void Phone_number_wrapped_at_a_line_end_is_detected(string text, string expected)
    {
        Assert.Contains(Run(new PhoneDetector(), text), x => x.Text == expected);
    }

    [Fact]
    public void Bare_ten_digits_are_a_phone_number_when_the_area_code_is_known()
    {
        var ctx = new DetectionContext { KnownAreaCodes = new HashSet<string> { "502" } };
        Assert.Contains(Run(new PhoneDetector(), "Body\n5024924699\nAuthor", ctx), x => x.Text == "5024924699");
        Assert.Empty(Run(new PhoneDetector(), "Body\n5024924699\nAuthor"));
    }

    [Fact]
    public void Date_of_birth_wrapped_before_the_year_is_detected()
    {
        Assert.Contains(Run(new DateDetector(), "Kameron Faircloth (DOB 12-22-\n2001) with a possible"), x => x.Text == "12-22-\n2001" && x.Category == PiiCategory.DateOfBirth);
    }

    [Fact]
    public void License_number_ends_before_the_next_label()
    {
        var r = Run(new DriverLicenseDetector(), "Cheri Brian (OLN B95674861 DOB 04/12-1974) with");
        Assert.Contains(r, x => x.Text == "B95674861");
    }

    [Theory]
    [InlineData("the residence at 111 Norwood also had a camera", "111 Norwood")]
    [InlineData("Body\n831 hillviewblvd\nAuthor", "831 hillviewblvd")]
    [InlineData("responded to the area of 797 Hillview\nBoulevard in reference to", "797 Hillview\nBoulevard")]
    [InlineData("with an address of 13627 Faye Amick\nRd Marysville Indiana", "13627 Faye Amick\nRd")]
    public void Addresses_in_narrative_prose_are_detected(string text, string expected)
    {
        Assert.Contains(Run(new AddressDetector(), text), x => x.Text == expected);
    }

    [Theory]
    [InlineData("arrived on scene at 0304 hours and saw")]
    [InlineData("I'm here for 20-25 yeard")]
    public void Times_and_counts_are_not_addresses(string text)
    {
        Assert.Empty(Run(new AddressDetector(), text));
    }

    [Theory]
    [InlineData("Haylee Hartman was interviewed at her residence", "Haylee Hartman")]
    [InlineData("Mikey told her that Joe shot Cole", "Mikey")]
    [InlineData("techs E. Burbrink and A Forlines. They also", "E. Burbrink")]
    [InlineData("techs E. Burbrink and A Forlines. They also", "A Forlines")]
    [InlineData("Interview with Kenna Westerman was told", "Kenna Westerman")]
    [InlineData("This person has been identified as Kameron Faircloth (DOB", "Kameron Faircloth")]
    [InlineData("identified as\nDJ Jewel, they had", "DJ Jewel")]
    public void Names_without_titles_are_detected(string text, string expected)
    {
        Assert.Contains(Run(new NameDetector(), text), x => x.Text == expected && x.Confidence >= 0.6);
    }

    [Theory]
    [InlineData("A fragment of a projectile was located in a porch post")]
    [InlineData("the stolen vehicle was towed to the lot")]
    [InlineData("JUVENILE\nKYIBRS REPORT: OFFENSE SUPPLEMENT")]
    [InlineData("Search warrant for 270-849-5568 is a Verizon cell phone number")]
    [InlineData("B. Scope\nC. Use of Deadly Force\nD. De-escalation")]
    [InlineData("State Aid Funds received during the year")]
    public void Prose_and_headings_are_not_names(string text)
    {
        Assert.DoesNotContain(Run(new NameDetector(), text), x => x.Confidence >= 0.6);
    }

    [Fact]
    public void Lower_case_propagation_is_limited_to_given_names()
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Deadly", "Training", "Cole", "Suicide" };
        var r = Run(new NameDetector(), "deadly force training; cole and suicide", new DetectionContext { KnownNameTokens = known });
        Assert.Equal(new[] { "cole" }, r.Select(x => x.Text));
    }

    [Fact]
    public void Form_label_followed_by_another_heading_does_not_propagate()
    {
        var r = Run(new NameDetector(), "OFFENDER SUSPECTED\nOF USING: NOT APPLICABLE");
        Assert.All(r, x => Assert.True(x.Confidence < 0.8, x.Text));
    }

    [Theory]
    [InlineData("Oritz", "Ortiz", true)]
    [InlineData("Katlyn", "Kaitlyn", true)]
    [InlineData("Jasan", "Jason", true)]
    [InlineData("Dlyan", "Dylan", true)]
    [InlineData("Arrow", "Barrow", true)]
    [InlineData("Miller", "Mueller", false)]
    public void Within_one_edit(string a, string b, bool expected)
    {
        Assert.Equal(expected, NameDetector.WithinOneEdit(a, b));
    }

    [Fact]
    public void Misspellings_and_lower_case_mentions_of_known_names_are_found()
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ortiz", "Kaitlyn", "Cole", "Barrow", "Chase" };
        var ctx = new DetectionContext { KnownNameTokens = known };
        var r = Run(new NameDetector(), "Oritz told Katlyn that cole was there. Detective Barrow. Bow & Arrow. jus the chase", ctx);
        Assert.Contains(r, x => x.Text == "Oritz");
        Assert.Contains(r, x => x.Text == "Katlyn");
        Assert.Contains(r, x => x.Text == "cole");
        Assert.DoesNotContain(r, x => x.Text == "Arrow");   // a dropped letter on a short word is not a misspelling
        Assert.DoesNotContain(r, x => x.Text == "chase");   // a common word in lower case is a word
    }

    [Fact]
    public void Engine_propagates_handles_area_codes_and_hyphenated_name_halves()
    {
        var page1 = "SUSPECT SEQ. # NAME: MOLINA-HOLLAND, JAYDN\nPHONE: 502-664-3039\nthe Instagram handle of kuttaosama";
        var page2 = "the residence of Jaydn Molina-\nHolland. Later kuttaosama replied\nBody\n5026763100";
        var doc = new RedactDocument
        {
            SourcePath = "x.pdf", WorkingPdfPath = "x.pdf",
            Pages = new[] { Page(1, page1), Page(2, page2) },
        };
        var findings = new DetectionEngine().Detect(doc, new DetectionOptions());
        Assert.Contains(findings, f => f.Page == 2 && f.Text == "Molina-");
        Assert.Contains(findings, f => f.Page == 2 && f.Text == "Holland");
        Assert.Contains(findings, f => f.Page == 2 && f.Text == "kuttaosama" && f.Category == PiiCategory.OnlineHandle);
        Assert.Contains(findings, f => f.Page == 2 && f.Text == "5026763100" && f.Category == PiiCategory.PhoneNumber);
    }

    private static PageModel Page(int number, string text)
    {
        var words = new List<WordBox>();
        var line = 0;
        var pos = 0;
        foreach (var l in text.Split('\n'))
        {
            var x = 0.0;
            foreach (var w in l.Split(' '))
            {
                if (w.Length > 0)
                    words.Add(new WordBox { Index = words.Count, Text = w, Rect = new RectPt(x, line * 12, w.Length * 5, 10), TextStart = pos, Line = line });
                pos += w.Length + 1;
                x += w.Length * 5 + 4;
            }
            line++;
        }
        return new PageModel { Number = number, Width = 612, Height = 792, Words = words, Text = text };
    }
}
