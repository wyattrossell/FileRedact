using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FileRedact.Core.Model;
using FileRedact.Core.Pdf;

namespace FileRedact.App.Services;

/// <summary>Prints the redacted rendering (never the original) through the standard Windows print dialog.</summary>
public static class PrintService
{
    public static bool Print(RedactDocument doc, IReadOnlyList<Finding> accepted, RedactionOptions options)
    {
        var dialog = new PrintDialog { UserPageRangeEnabled = true, MinPage = 1, MaxPage = (uint)doc.Pages.Count };
        if (dialog.ShowDialog() != true) return false;

        var first = 1;
        var last = doc.Pages.Count;
        if (dialog.PageRangeSelection == PageRangeSelection.UserPages)
        {
            first = Math.Max(1, dialog.PageRange.PageFrom);
            last = Math.Min(doc.Pages.Count, dialog.PageRange.PageTo);
        }

        var fixedDoc = new FixedDocument();
        var pageW = dialog.PrintableAreaWidth;
        var pageH = dialog.PrintableAreaHeight;
        fixedDoc.DocumentPaginator.PageSize = new Size(pageW, pageH);

        for (var n = first; n <= last; n++)
        {
            var page = doc.Pages[n - 1];
            using var bmp = RedactionWriter.RenderRedactedPage(doc, page, accepted, options);
            var source = BitmapConverter.ToBitmapSource(bmp);

            // Fit the page into the printable area, preserving aspect ratio.
            var scale = Math.Min(pageW / page.Width, pageH / page.Height);
            var w = page.Width * scale;
            var h = page.Height * scale;

            var image = new Image { Source = source, Width = w, Height = h, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var canvas = new Canvas { Width = pageW, Height = pageH };
            Canvas.SetLeft(image, (pageW - w) / 2);
            Canvas.SetTop(image, (pageH - h) / 2);
            canvas.Children.Add(image);

            var fixedPage = new FixedPage { Width = pageW, Height = pageH };
            fixedPage.Children.Add(canvas);
            var content = new PageContent();
            ((System.Windows.Markup.IAddChild)content).AddChild(fixedPage);
            fixedDoc.Pages.Add(content);
        }

        dialog.PrintDocument(fixedDoc.DocumentPaginator, $"FileRedact - {doc.DisplayName} (redacted)");
        return true;
    }
}
