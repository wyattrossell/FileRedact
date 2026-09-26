using FileRedact.Core;
using FileRedact.Core.Detection;
using FileRedact.Core.Documents;
using FileRedact.Core.Model;
using FileRedact.Core.Pdf;
using SkiaSharp;
using UglyToad.PdfPig;
using Xunit;

namespace FileRedact.Tests;

/// <summary>End-to-end: build a PDF, load it, detect PII, write the redacted PDF and prove the PII is gone.</summary>
public class PipelineTests
{
    private static readonly string[] Lines =
    {
        "SPRINGFIELD POLICE DEPARTMENT - INCIDENT REPORT",
        "Case No. 2024-00123    Date: 03/14/2024",
        "Victim: JOHNSON, ROBERT A    DOB: 05/21/1979    SSN: 123-45-6789",
        "Address: 742 Evergreen Terrace, Springfield, IL 62704",
        "Phone: (555) 867-5309    Email: robert.johnson@example.com",
        "DL# J123-4567-8901 (IL)    Plate: ABC-1234",
        "Narrative: Officer Maria Gonzalez responded to the location and spoke with Johnson,",
        "who stated that an unknown suspect took his wallet. Johnson was cooperative.",
        "Nothing further at this time.",
    };

    private static string BuildSamplePdf()
    {
        var path = Path.Combine(Path.GetTempPath(), $"filereadact-sample-{Guid.NewGuid():N}.pdf");
        SimplePdfBuilder.FromParagraphs(Lines, path);
        return path;
    }

    [Fact]
    public async Task Load_detect_redact_removes_pii_and_keeps_other_text()
    {
        var sample = BuildSamplePdf();
        var (doc, report) = await DocumentLoader.LoadAsync(sample);
        Assert.Single(doc.Pages);
        Assert.False(doc.Pages[0].FromOcr);
        Assert.Contains("123-45-6789", doc.Pages[0].Text);

        var engine = new DetectionEngine();
        var findings = engine.Detect(doc, new DetectionOptions());
        doc.Findings.AddRange(findings);

        string[] mustFind = { "123-45-6789", "05/21/1979", "(555) 867-5309", "robert.johnson@example.com", "J123-4567-8901", "ABC-1234", "JOHNSON, ROBERT A", "Maria Gonzalez" };
        foreach (var expected in mustFind)
            Assert.Contains(findings, f => f.Text.Contains(expected) && f.Accepted);
        Assert.Contains(findings, f => f.Text.StartsWith("742 Evergreen Terrace") && f.Category == PiiCategory.Address);
        // Surname propagation: the standalone "Johnson" mentions are picked up from "JOHNSON, ROBERT A".
        Assert.True(findings.Count(f => f.Text == "Johnson") >= 2, "expected propagated surname findings");
        // The incident date is offered but not pre-selected.
        Assert.Contains(findings, f => f.Text == "03/14/2024" && f.Category == PiiCategory.Date && !f.Accepted);

        var output = Path.ChangeExtension(sample, ".redacted.pdf");
        RedactionWriter.Write(doc, doc.AcceptedFindings, output, new RedactionOptions { Dpi = 150 });
        Assert.True(File.Exists(output));

        // The redacted PDF has a text layer with the surviving words only.
        var text = ExtractAllText(output);
        foreach (var gone in mustFind)
            Assert.DoesNotContain(gone, text);
        Assert.DoesNotContain("Johnson", text);
        Assert.Contains("SPRINGFIELD", text);
        Assert.Contains("Narrative", text);
        Assert.Contains("wallet", text);

        // And the original text cannot be found in the raw bytes either (nothing is copied from the source).
        var raw = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(output));
        Assert.DoesNotContain("123-45-6789", raw);
        Assert.DoesNotContain("Gonzalez", raw);

        // The invisible text layer must not change the rendered appearance.
        var noText = Path.ChangeExtension(sample, ".notext.pdf");
        RedactionWriter.Write(doc, doc.AcceptedFindings, noText, new RedactionOptions { Dpi = 150, KeepTextLayer = false });
        using var a = PdfRenderer.Render(output, 0, 100);
        using var b = PdfRenderer.Render(noText, 0, 100);
        Assert.Equal(a.Width, b.Width);
        Assert.True(PixelDifferenceRatio(a, b) < 0.001, "invisible text layer altered the rendering");

        // Black boxes really are painted where the SSN was.
        var ssn = findings.First(f => f.Text.Contains("123-45-6789"));
        var r = ssn.Rects[0];
        var scale = a.Width / doc.Pages[0].Width;
        var px = a.GetPixel((int)((r.X + r.Width / 2) * scale), (int)((r.Y + r.Height / 2) * scale));
        Assert.True(px.Red < 30 && px.Green < 30 && px.Blue < 30, $"expected black at SSN location, got {px}");

        File.Delete(sample);
        File.Delete(output);
        File.Delete(noText);
    }

    [Fact]
    public void Rotation_mapping_is_consistent()
    {
        var r = new RectPt(10, 20, 30, 5);
        var rot90 = PdfTextExtractor.Rotate(r, 90, 100, 200);
        Assert.Equal(new RectPt(200 - 25, 10, 5, 30), rot90);
        var rot180 = PdfTextExtractor.Rotate(r, 180, 100, 200);
        Assert.Equal(new RectPt(60, 175, 30, 5), rot180);
        var rot270 = PdfTextExtractor.Rotate(r, 270, 100, 200);
        Assert.Equal(new RectPt(20, 60, 5, 30), rot270);
    }

    [Fact]
    public void Printer_install_script_targets_expected_paths()
    {
        var script = Core.Printer.PrinterInstaller.BuildInstallScript();
        Assert.Contains(AppPaths.PrinterPortFile, script);
        Assert.Contains("Microsoft Print To PDF", script);
        Assert.Contains("Add-Printer", script);
    }

    private static string ExtractAllText(string pdf)
    {
        using var d = PdfDocument.Open(pdf);
        return string.Join("\n", d.GetPages().Select(p => string.Join(" ", p.GetWords().Select(w => w.Text))));
    }

    private static double PixelDifferenceRatio(SKBitmap a, SKBitmap b)
    {
        var diff = 0;
        for (var y = 0; y < a.Height; y += 2)
            for (var x = 0; x < a.Width; x += 2)
            {
                var pa = a.GetPixel(x, y);
                var pb = b.GetPixel(x, y);
                if (Math.Abs(pa.Red - pb.Red) > 8 || Math.Abs(pa.Green - pb.Green) > 8 || Math.Abs(pa.Blue - pb.Blue) > 8) diff++;
            }
        return diff / (double)((a.Width / 2) * (a.Height / 2));
    }
}
