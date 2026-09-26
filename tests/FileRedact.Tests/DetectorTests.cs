using FileRedact.Core.Detection;
using FileRedact.Core.Detection.Detectors;
using FileRedact.Core.Model;
using Xunit;

namespace FileRedact.Tests;

public class DetectorTests
{
    private static readonly DetectionContext Ctx = new();

    private static List<(string Text, PiiCategory Category, double Confidence)> Run(IPiiDetector d, string text)
        => d.Detect(text, Ctx).Select(s => (text.Substring(s.Start, s.Length), s.Category, s.Confidence)).ToList();

    [Theory]
    [InlineData("SSN: 123-45-6789", "123-45-6789")]
    [InlineData("Social Security Number 123 45 6789 on file", "123 45 6789")]
    [InlineData("SS# 123456789", "123456789")]
    public void Ssn_is_detected(string text, string expected)
    {
        var r = Run(new SsnDetector(), text);
        Assert.Contains(r, x => x.Text == expected && x.Category == PiiCategory.SocialSecurityNumber);
    }

    [Fact]
    public void Ssn_plain_digits_without_label_are_ignored()
    {
        Assert.Empty(Run(new SsnDetector(), "Case number 123456789 was filed"));
    }

    [Theory]
    [InlineData("DOB: 04/12/1985", "04/12/1985", PiiCategory.DateOfBirth)]
    [InlineData("Date of Birth - March 3, 1990", "March 3, 1990", PiiCategory.DateOfBirth)]
    [InlineData("Incident occurred on 07/04/2023", "07/04/2023", PiiCategory.Date)]
    public void Dates_are_classified(string text, string expected, PiiCategory cat)
    {
        var r = Run(new DateDetector(), text);
        Assert.Contains(r, x => x.Text == expected && x.Category == cat);
    }

    [Theory]
    [InlineData("Call (555) 123-4567 today", "(555) 123-4567")]
    [InlineData("Phone: 555.123.4567", "555.123.4567")]
    [InlineData("cell 5551234567", "5551234567")]
    public void Phones_are_detected(string text, string expected)
    {
        Assert.Contains(Run(new PhoneDetector(), text), x => x.Text == expected);
    }

    [Fact]
    public void Email_is_detected()
    {
        Assert.Contains(Run(new EmailDetector(), "reach me at john.doe+work@example.org."), x => x.Text == "john.doe+work@example.org");
    }

    [Theory]
    [InlineData("He resides at 1234 N Main Street Apt 5B, Springfield, IL 62704 with", "1234 N Main Street Apt 5B, Springfield, IL 62704")]
    [InlineData("Address: 98 Oak Ave.", "98 Oak Ave")]
    [InlineData("Mail to P.O. Box 4521, Austin, TX 78701", "P.O. Box 4521, Austin, TX 78701")]
    [InlineData("City: Denver, CO 80202", "Denver, CO 80202")]
    public void Addresses_are_detected(string text, string expected)
    {
        var r = Run(new AddressDetector(), text);
        Assert.Contains(r, x => x.Text == expected);
    }

    [Theory]
    [InlineData("DL# S123-4567-8901 (IL)", "S123-4567-8901")]
    [InlineData("Driver's License Number: A1234567", "A1234567")]
    [InlineData("OLN 12345678 STATE TX", "12345678")]
    public void Driver_license_is_detected(string text, string expected)
    {
        Assert.Contains(Run(new DriverLicenseDetector(), text), x => x.Text == expected && x.Category == PiiCategory.DriverLicense);
    }

    [Fact]
    public void Credit_card_requires_luhn()
    {
        var d = new FinancialDetector();
        Assert.Contains(Run(d, "card 4111 1111 1111 1111 exp"), x => x.Text == "4111 1111 1111 1111");
        Assert.DoesNotContain(Run(d, "ref 4111 1111 1111 1112 exp"), x => x.Text == "4111 1111 1111 1112");
    }

    [Fact]
    public void Vin_and_plate_are_detected()
    {
        var d = new VehicleDetector();
        Assert.Contains(Run(d, "VIN 1HGCM82633A004352"), x => x.Text == "1HGCM82633A004352");
        Assert.Contains(Run(d, "License Plate: ABC-1234"), x => x.Text == "ABC-1234");
    }

    [Fact]
    public void Fbi_and_sid_numbers_are_detected()
    {
        var d = new CriminalJusticeIdDetector();
        Assert.Contains(Run(d, "FBI No. 123456AB7"), x => x.Text == "123456AB7");
        Assert.Contains(Run(d, "SID: IL12345678"), x => x.Text == "IL12345678");
    }

    [Theory]
    [InlineData("Defendant: John Q. Adams was", "John Q. Adams")]
    [InlineData("Name: SMITH, JOHN A", "SMITH, JOHN A")]
    [InlineData("Officer Maria Gonzalez responded", "Maria Gonzalez")]
    [InlineData("statement of Mr. O'Brien regarding", "O'Brien")]
    [InlineData("Victim Robert Johnson stated", "Robert Johnson")]
    [InlineData("was interviewed. Jennifer Walsh confirmed", "Jennifer Walsh")]
    public void Names_are_detected(string text, string expected)
    {
        var r = Run(new NameDetector(), text);
        Assert.Contains(r, x => x.Text == expected && x.Category == PiiCategory.Name);
    }

    [Fact]
    public void Common_capitalised_phrases_are_not_names()
    {
        var r = Run(new NameDetector(), "The Springfield Police Department responded on Monday March 3 to Main Street.");
        Assert.Empty(r);
    }

    [Fact]
    public void Name_label_trims_trailing_organisation_words()
    {
        var r = Run(new NameDetector(), "Reported by: Jane Doe Police Department");
        Assert.Contains(r, x => x.Text == "Jane Doe");
    }

    [Fact]
    public void Place_of_birth_and_maiden_name_follow_labels()
    {
        Assert.Contains(Run(new PlaceOfBirthDetector(), "Place of Birth: Peoria, IL   Sex: M"), x => x.Text == "Peoria, IL");
        Assert.Contains(Run(new MaidenNameDetector(), "Mother's maiden name: Kowalski"), x => x.Text == "Kowalski");
    }

    [Fact]
    public void Custom_terms_match_case_insensitively_and_whole_word()
    {
        var ctx = new DetectionContext { CustomTerms = new[] { "Acme Corp", "XZ-9" } };
        var spans = new CustomTermDetector().Detect("Contact ACME corp about xz-9 and XZ-99.", ctx).ToList();
        Assert.Equal(2, spans.Count);
    }

    [Fact]
    public void Overlap_resolution_keeps_highest_confidence_then_longest()
    {
        var spans = new[]
        {
            new TextSpan(0, 10, PiiCategory.Address, 0.85, "a"),
            new TextSpan(2, 4, PiiCategory.Date, 0.4, "b"),
            new TextSpan(20, 5, PiiCategory.Name, 0.8, "c"),
            new TextSpan(20, 8, PiiCategory.Name, 0.8, "d"),
        };
        var kept = DetectionEngine.ResolveOverlaps(spans);
        Assert.Equal(2, kept.Count);
        Assert.Equal("a", kept[0].Reason);
        Assert.Equal("d", kept[1].Reason);
    }
}
