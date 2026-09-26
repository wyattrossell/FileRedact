using System.Text;
using FileRedact.Core.Model;
using SkiaSharp;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace FileRedact.Core.Ocr;

/// <summary>
/// OCR for pages without a usable text layer (scanned documents, printed images), using the OCR engine
/// built into Windows 10/11. No network access is involved - the document never leaves the machine.
/// </summary>
public static class WindowsOcr
{
    public static bool IsAvailable
    {
        get
        {
            try { return OcrEngine.TryCreateFromUserProfileLanguages() != null || OcrEngine.AvailableRecognizerLanguages.Count > 0; }
            catch { return false; }
        }
    }

    public sealed record OcrPage(List<WordBox> Words, string Text);

    /// <summary>
    /// Recognises the bitmap (rendered at <paramref name="dpi"/>) and returns word boxes in page points.
    /// </summary>
    public static async Task<OcrPage?> RecognizeAsync(SKBitmap bitmap, int dpi)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                     ?? (OcrEngine.AvailableRecognizerLanguages.Count > 0 ? OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0]) : null);
        if (engine == null) return null;

        // The engine limits image dimensions; downscale if needed and correct the scale afterwards.
        var max = (int)OcrEngine.MaxImageDimension;
        var scale = 1.0;
        var src = bitmap;
        if (bitmap.Width > max || bitmap.Height > max)
        {
            scale = Math.Min((double)max / bitmap.Width, (double)max / bitmap.Height);
            var info = new SKImageInfo((int)(bitmap.Width * scale), (int)(bitmap.Height * scale), SKColorType.Bgra8888, SKAlphaType.Premul);
            src = bitmap.Resize(info, SKSamplingOptions.Default) ?? bitmap;
        }

        using var data = src.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream();
        data.SaveTo(ms);
        ms.Position = 0;
        using var ras = ms.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(ras);
        using var soft = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(soft);
        if (!ReferenceEquals(src, bitmap)) src.Dispose();

        var pxToPt = 72.0 / dpi / scale;
        var sb = new StringBuilder();
        var words = new List<WordBox>();
        var lineNo = 0;
        var index = 0;
        foreach (var line in result.Lines)
        {
            if (sb.Length > 0) sb.Append('\n');
            var first = true;
            foreach (var w in line.Words)
            {
                var text = w.Text.Trim();
                if (text.Length == 0) continue;
                if (!first) sb.Append(' ');
                first = false;
                var start = sb.Length;
                sb.Append(text);
                var r = w.BoundingRect;
                words.Add(new WordBox
                {
                    Index = index++,
                    Text = text,
                    Rect = new RectPt(r.X * pxToPt, r.Y * pxToPt, r.Width * pxToPt, r.Height * pxToPt),
                    TextStart = start,
                    Line = lineNo,
                });
            }
            lineNo++;
        }
        return new OcrPage(words, sb.ToString());
    }
}
