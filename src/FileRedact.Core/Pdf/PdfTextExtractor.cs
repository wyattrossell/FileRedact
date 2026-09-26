using System.Text;
using FileRedact.Core.Model;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace FileRedact.Core.Pdf;

/// <summary>
/// Extracts words with positions from a PDF's text layer using PdfPig, arranges them in reading order
/// and produces a <see cref="PageModel"/> per page. Coordinates are converted to a top-left origin and
/// adjusted for the page's /Rotate so they line up with the rendered bitmap.
/// </summary>
public static class PdfTextExtractor
{
    public sealed record ExtractedPage(int Number, double Width, double Height, List<WordBox> Words, string Text);

    public static List<ExtractedPage> Extract(string pdfPath)
    {
        var pages = new List<ExtractedPage>();
        using var document = PdfDocument.Open(pdfPath, new ParsingOptions { UseLenientParsing = true, ClipPaths = false });
        foreach (var page in document.GetPages())
            pages.Add(ExtractPage(page));
        return pages;
    }

    private static ExtractedPage ExtractPage(Page page)
    {
        var crop = page.CropBox.Bounds;
        var cropW = crop.Width;
        var cropH = crop.Height;
        var rotation = ((page.Rotation.Value % 360) + 360) % 360;
        var (dispW, dispH) = rotation is 90 or 270 ? (cropH, cropW) : (cropW, cropH);

        IEnumerable<Word> words;
        try
        {
            words = page.GetWords(NearestNeighbourWordExtractor.Instance);
        }
        catch
        {
            words = page.GetWords();
        }
        var wordList = words.Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();

        var sb = new StringBuilder();
        var boxes = new List<WordBox>();
        if (wordList.Count > 0)
        {
            IReadOnlyList<UglyToad.PdfPig.DocumentLayoutAnalysis.TextBlock> blocks;
            try
            {
                blocks = DocstrumBoundingBoxes.Instance.GetBlocks(wordList);
                blocks = UnsupervisedReadingOrderDetector.Instance.Get(blocks).ToList();
            }
            catch
            {
                blocks = DefaultPageSegmenter.Instance.GetBlocks(wordList);
            }

            var line = 0;
            var index = 0;
            foreach (var block in blocks)
            {
                if (sb.Length > 0) sb.Append("\n\n");
                var firstLine = true;
                foreach (var textLine in block.TextLines)
                {
                    if (!firstLine) sb.Append('\n');
                    firstLine = false;
                    var firstWord = true;
                    foreach (var w in textLine.Words)
                    {
                        var text = Clean(w.Text);
                        if (text.Length == 0) continue;
                        if (!firstWord) sb.Append(' ');
                        firstWord = false;
                        var start = sb.Length;
                        sb.Append(text);
                        var bb = w.BoundingBox;
                        // PdfPig: bottom-left origin. Convert to top-left relative to the crop box.
                        var unrotated = new RectPt(bb.Left - crop.Left, crop.Top - bb.Top, bb.Width, bb.Height);
                        boxes.Add(new WordBox
                        {
                            Index = index++,
                            Text = text,
                            Rect = Rotate(unrotated, rotation, cropW, cropH),
                            TextStart = start,
                            Line = line,
                        });
                    }
                    line++;
                }
            }
        }

        return new ExtractedPage(page.Number, dispW, dispH, boxes, sb.ToString());
    }

    /// <summary>Removes control characters and normalises odd whitespace inside a word.</summary>
    private static string Clean(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c)) continue;
            if (c == '�') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Maps a top-left-origin rectangle on the unrotated page onto the rotated (displayed) page.</summary>
    public static RectPt Rotate(RectPt r, int rotation, double w, double h) => rotation switch
    {
        90 => new RectPt(h - r.Y - r.Height, r.X, r.Height, r.Width),
        180 => new RectPt(w - r.X - r.Width, h - r.Y - r.Height, r.Width, r.Height),
        270 => new RectPt(r.Y, w - r.X - r.Width, r.Height, r.Width),
        _ => r,
    };
}
