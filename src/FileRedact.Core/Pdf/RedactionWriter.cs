using FileRedact.Core.Documents;
using FileRedact.Core.Model;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SkiaSharp;

namespace FileRedact.Core.Pdf;

public sealed class RedactionOptions
{
    /// <summary>Resolution of the rasterised pages in the output.</summary>
    public int Dpi { get; init; } = 200;
    public int JpegQuality { get; init; } = 88;
    /// <summary>Extra margin (points) around each redaction box to cover anti-aliasing fringes.</summary>
    public double Padding { get; init; } = 1.5;
    /// <summary>
    /// Re-add the non-redacted words as invisible text so the output stays searchable/copyable and
    /// AI models can read it without OCR. Redacted words are never written.
    /// </summary>
    public bool KeepTextLayer { get; init; } = true;
}

/// <summary>
/// Produces the redacted PDF. Every page is rasterised, the accepted regions are painted solid black on
/// the bitmap, and a brand-new PDF is assembled from those bitmaps. Because nothing from the original
/// file's content streams, fonts, metadata or attachments is copied, the redacted information cannot be
/// recovered from the output (unlike a black rectangle drawn over the original text).
/// </summary>
public static class RedactionWriter
{
    public sealed record PageResult(int PageNumber, SKBitmap Bitmap);

    public static void Write(RedactDocument doc, IEnumerable<Finding> accepted, string outputPath, RedactionOptions options, IProgress<(int Page, int Total)>? progress = null, CancellationToken ct = default)
    {
        PdfSharpSetup.EnsureFonts();
        var acceptedList = accepted.ToList();
        using var pdf = new PdfDocument();
        pdf.Info.Title = "";
        pdf.Info.Author = "";
        pdf.Info.Subject = "";
        pdf.Info.Keywords = "";
        pdf.Info.Creator = "FileRedact";

        var total = doc.Pages.Count;
        for (var i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            var page = doc.Pages[i];
            progress?.Report((i + 1, total));
            using var bmp = RenderRedactedPage(doc, page, acceptedList, options);

            using var jpeg = bmp.Encode(SKEncodedImageFormat.Jpeg, options.JpegQuality);
            using var ms = new MemoryStream();
            jpeg.SaveTo(ms);
            ms.Position = 0;

            var pdfPage = pdf.AddPage();
            pdfPage.Width = XUnit.FromPoint(page.Width);
            pdfPage.Height = XUnit.FromPoint(page.Height);
            using var gfx = XGraphics.FromPdfPage(pdfPage);
            using var img = XImage.FromStream(ms);
            gfx.DrawImage(img, 0, 0, page.Width, page.Height);

            if (options.KeepTextLayer)
                WriteInvisibleText(gfx, page, acceptedList.Where(f => f.Page == page.Number).ToList(), options);
        }

        var tmp = outputPath + ".tmp";
        pdf.Save(tmp);
        File.Move(tmp, outputPath, overwrite: true);
    }

    /// <summary>Renders one page with the accepted regions painted black. Used for output, printing and preview.</summary>
    public static SKBitmap RenderRedactedPage(RedactDocument doc, PageModel page, IReadOnlyList<Finding> accepted, RedactionOptions options)
    {
        var bmp = PdfRenderer.Render(doc.WorkingPdfPath, page.Number - 1, options.Dpi);
        var scaleX = bmp.Width / page.Width;
        var scaleY = bmp.Height / page.Height;
        using var canvas = new SKCanvas(bmp);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false, Style = SKPaintStyle.Fill };
        foreach (var f in accepted.Where(f => f.Page == page.Number))
        {
            foreach (var r0 in f.Rects)
            {
                var r = r0.Inflate(options.Padding, options.Padding);
                var rect = new SKRect(
                    (float)Math.Floor(r.Left * scaleX), (float)Math.Floor(r.Top * scaleY),
                    (float)Math.Ceiling(r.Right * scaleX), (float)Math.Ceiling(r.Bottom * scaleY));
                canvas.DrawRect(rect, paint);
            }
        }
        canvas.Flush();
        return bmp;
    }

    /// <summary>Returns the words on a page that are NOT covered (even partially) by an accepted redaction.</summary>
    public static IEnumerable<WordBox> SurvivingWords(PageModel page, IReadOnlyList<Finding> acceptedOnPage, double padding)
    {
        var boxes = acceptedOnPage.SelectMany(f => f.Rects).Select(r => r.Inflate(padding, padding)).ToList();
        foreach (var w in page.Words)
        {
            if (w.Text.Length == 0) continue;
            if (boxes.Any(b => b.Intersects(w.Rect))) continue;
            yield return w;
        }
    }

    private static void WriteInvisibleText(XGraphics gfx, PageModel page, IReadOnlyList<Finding> acceptedOnPage, RedactionOptions options)
    {
        var brush = new XSolidBrush(XColor.FromArgb(0, 0, 0, 0));
        foreach (var w in SurvivingWords(page, acceptedOnPage, options.Padding))
        {
            var r = w.Rect;
            if (r.Height <= 0.5 || r.Width <= 0.5) continue;
            var size = Math.Clamp(r.Height * 1.05, 2, 200);
            XFont font;
            try { font = new XFont("Arial", size, XFontStyleEx.Regular); }
            catch { return; }
            var measured = gfx.MeasureString(w.Text, font);
            if (measured.Width <= 0) continue;
            var sx = r.Width / measured.Width;
            gfx.Save();
            gfx.TranslateTransform(r.Left, r.Top);
            gfx.ScaleTransform(sx, 1);
            gfx.DrawString(w.Text, font, brush, new XPoint(0, 0), XStringFormats.TopLeft);
            gfx.Restore();
        }
    }
}
