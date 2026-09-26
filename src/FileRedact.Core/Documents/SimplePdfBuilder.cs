using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace FileRedact.Core.Documents;

/// <summary>Minimal PDF generation used when Microsoft Word is not available, and for images.</summary>
public static class SimplePdfBuilder
{
    static SimplePdfBuilder()
    {
        PdfSharpSetup.EnsureFonts();
    }

    private const double PageW = 612, PageH = 792, Margin = 54;

    public static void FromDocx(string docxPath, string output)
    {
        var paragraphs = new List<string>();
        using (var doc = WordprocessingDocument.Open(docxPath, false))
        {
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body != null)
            {
                foreach (var el in body.ChildElements)
                {
                    switch (el)
                    {
                        case Paragraph p:
                            paragraphs.Add(p.InnerText);
                            break;
                        case Table t:
                            foreach (var row in t.Elements<TableRow>())
                                paragraphs.Add(string.Join("    ", row.Elements<TableCell>().Select(c => c.InnerText)));
                            break;
                    }
                }
            }
        }
        FromParagraphs(paragraphs, output);
    }

    public static void FromText(string text, string output)
        => FromParagraphs(text.Replace("\r\n", "\n").Split('\n'), output);

    public static void FromParagraphs(IEnumerable<string> paragraphs, string output)
    {
        using var pdf = new PdfDocument();
        var font = new XFont("Arial", 11, XFontStyleEx.Regular);
        var lineHeight = 15.0;
        PdfPage? page = null;
        XGraphics? gfx = null;
        var y = Margin;

        void NewPage()
        {
            gfx?.Dispose();
            page = pdf.AddPage();
            page.Width = XUnit.FromPoint(PageW);
            page.Height = XUnit.FromPoint(PageH);
            gfx = XGraphics.FromPdfPage(page);
            y = Margin;
        }
        NewPage();

        foreach (var para in paragraphs)
        {
            var lines = Wrap(gfx!, font, para, PageW - 2 * Margin);
            if (lines.Count == 0) lines.Add("");
            foreach (var line in lines)
            {
                if (y + lineHeight > PageH - Margin) NewPage();
                if (line.Length > 0)
                    gfx!.DrawString(line, font, XBrushes.Black, new XPoint(Margin, y), XStringFormats.TopLeft);
                y += lineHeight;
            }
        }
        gfx?.Dispose();
        pdf.Save(output);
    }

    public static void FromImage(string imagePath, string output)
    {
        using var pdf = new PdfDocument();
        using var img = XImage.FromFile(imagePath);
        var page = pdf.AddPage();
        var w = img.PointWidth;
        var h = img.PointHeight;
        page.Width = XUnit.FromPoint(w);
        page.Height = XUnit.FromPoint(h);
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawImage(img, 0, 0, w, h);
        pdf.Save(output);
    }

    private static List<string> Wrap(XGraphics gfx, XFont font, string text, double maxWidth)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var current = "";
        foreach (var w in words)
        {
            var candidate = current.Length == 0 ? w : current + " " + w;
            if (gfx.MeasureString(candidate, font).Width <= maxWidth || current.Length == 0)
                current = candidate;
            else
            {
                result.Add(current);
                current = w;
            }
        }
        if (current.Length > 0) result.Add(current);
        return result;
    }
}

/// <summary>PDFsharp needs to know where to find fonts; on Windows we use the installed system fonts.</summary>
public static class PdfSharpSetup
{
    private static bool _done;
    public static void EnsureFonts()
    {
        if (_done) return;
        _done = true;
        try
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }
        catch
        {
            // Older/newer PDFsharp builds may configure fonts differently; drawing will throw a clearer error if unavailable.
        }
    }
}
