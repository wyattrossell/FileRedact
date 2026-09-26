using FileRedact.Core.Model;
using FileRedact.Core.Ocr;
using FileRedact.Core.Pdf;

namespace FileRedact.Core.Documents;

/// <summary>Loads any supported file into a <see cref="RedactDocument"/>: convert to PDF, extract text, OCR pages without text.</summary>
public static class DocumentLoader
{
    public sealed class LoadReport
    {
        public string ConversionNote { get; set; } = "";
        public int OcrPages { get; set; }
        public List<string> Warnings { get; } = new();
    }

    public static async Task<(RedactDocument Document, LoadReport Report)> LoadAsync(string path, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var report = new LoadReport();
        progress?.Report("Converting to PDF…");
        var (pdfPath, note) = await DocumentConverter.ToPdfAsync(path, ct);
        report.ConversionNote = note;

        progress?.Report("Reading text…");
        var extracted = await Task.Run(() => PdfTextExtractor.Extract(pdfPath), ct);

        // PdfPig and PDFium can disagree on page size for odd files; trust the renderer's page size.
        IList<System.Drawing.SizeF>? sizes = null;
        try { sizes = PdfRenderer.GetPageSizes(pdfPath); } catch { }

        var pages = new List<PageModel>();
        for (var i = 0; i < extracted.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var ep = extracted[i];
            var width = ep.Width;
            var height = ep.Height;
            if (sizes != null && i < sizes.Count && sizes[i].Width > 0 && sizes[i].Height > 0)
            {
                width = sizes[i].Width;
                height = sizes[i].Height;
            }

            var words = ep.Words;
            var text = ep.Text;
            var fromOcr = false;

            if (NeedsOcr(ep))
            {
                progress?.Report($"Recognising text on page {ep.Number} (OCR)…");
                try
                {
                    var dpi = ChooseOcrDpi(width, height);
                    using var bmp = PdfRenderer.Render(pdfPath, i, dpi);
                    var ocr = await WindowsOcr.RecognizeAsync(bmp, dpi);
                    if (ocr != null)
                    {
                        words = ocr.Words;
                        text = ocr.Text;
                        fromOcr = true;
                        report.OcrPages++;
                    }
                    else
                    {
                        report.Warnings.Add($"Page {ep.Number} has no text layer and OCR is not available on this system.");
                    }
                }
                catch (Exception ex)
                {
                    report.Warnings.Add($"OCR failed on page {ep.Number}: {ex.Message}");
                }
            }

            pages.Add(new PageModel
            {
                Number = ep.Number,
                Width = width,
                Height = height,
                Words = words,
                Text = text,
                FromOcr = fromOcr,
            });
        }

        var doc = new RedactDocument { SourcePath = path, WorkingPdfPath = pdfPath, Pages = pages };
        return (doc, report);
    }

    private static bool NeedsOcr(PdfTextExtractor.ExtractedPage p)
    {
        if (p.Words.Count < 3) return true;
        // Text layers produced by poor OCR or symbol fonts: mostly non-alphanumeric garbage.
        var alnum = p.Text.Count(char.IsLetterOrDigit);
        return alnum < p.Text.Length * 0.3;
    }

    private static int ChooseOcrDpi(double widthPt, double heightPt)
    {
        var maxSide = Math.Max(widthPt, heightPt);
        var dpi = (int)Math.Floor(2500 * 72.0 / maxSide);
        return Math.Clamp(dpi, 96, 300);
    }
}
