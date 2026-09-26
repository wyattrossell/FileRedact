using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace FileRedact.App.Services;

public static class BitmapConverter
{
    /// <summary>Copies a Skia bitmap into a frozen WPF bitmap (safe to use from any thread).</summary>
    public static BitmapSource ToBitmapSource(SKBitmap bitmap)
    {
        SKBitmap src = bitmap;
        var owned = false;
        if (bitmap.ColorType != SKColorType.Bgra8888)
        {
            src = bitmap.Copy(SKColorType.Bgra8888) ?? throw new InvalidOperationException("Unable to convert bitmap");
            owned = true;
        }
        try
        {
            var format = src.AlphaType == SKAlphaType.Premul ? PixelFormats.Pbgra32 : PixelFormats.Bgra32;
            var bs = BitmapSource.Create(src.Width, src.Height, 96, 96, format, null, src.GetPixels(), src.ByteCount, src.RowBytes);
            bs.Freeze();
            return bs;
        }
        finally
        {
            if (owned) src.Dispose();
        }
    }
}
