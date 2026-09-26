using PDFtoImage;
using SkiaSharp;

namespace FileRedact.Core.Pdf;

/// <summary>Rasterises PDF pages with PDFium (via PDFtoImage).</summary>
public static class PdfRenderer
{
    public static int GetPageCount(string pdfPath)
    {
        using var fs = File.OpenRead(pdfPath);
        return Conversion.GetPageCount(fs, leaveOpen: false);
    }

    /// <summary>Page sizes in points, after the page rotation has been applied.</summary>
    public static IList<System.Drawing.SizeF> GetPageSizes(string pdfPath)
    {
        using var fs = File.OpenRead(pdfPath);
        return Conversion.GetPageSizes(fs, leaveOpen: false);
    }

    public static SKBitmap Render(string pdfPath, int pageIndex, int dpi, bool withAnnotations = true)
    {
        using var fs = File.OpenRead(pdfPath);
        var options = new RenderOptions(
            Dpi: dpi,
            WithAnnotations: withAnnotations,
            WithFormFill: true,
            AntiAliasing: PdfAntiAliasing.All,
            BackgroundColor: SKColors.White);
        return Conversion.ToImage(fs, pageIndex, leaveOpen: false, password: null, options: options);
    }
}
