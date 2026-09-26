using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FileRedact.App.Services;
using FileRedact.Core.Model;
using FileRedact.Core.Pdf;

namespace FileRedact.App.ViewModels;

/// <summary>A highlight rectangle on a page, in rendered-pixel coordinates. Several may belong to one finding.</summary>
public sealed class HighlightViewModel
{
    public required FindingViewModel Finding { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

public partial class PageViewModel : ObservableObject
{
    public const int DisplayDpi = 110;

    public PageModel Model { get; }
    private readonly string _pdfPath;
    private readonly SemaphoreSlim _renderGate = new(1, 1);

    public PageViewModel(PageModel model, string pdfPath)
    {
        Model = model;
        _pdfPath = pdfPath;
        Scale = DisplayDpi / 72.0;
        PixelWidth = Math.Round(model.Width * Scale);
        PixelHeight = Math.Round(model.Height * Scale);
    }

    public int Number => Model.Number;
    /// <summary>Rendered pixels per PDF point.</summary>
    public double Scale { get; }
    public double PixelWidth { get; }
    public double PixelHeight { get; }
    public string Header => Model.FromOcr ? $"Page {Number}  (text recovered with OCR)" : $"Page {Number}";

    [ObservableProperty]
    private ImageSource? _image;

    public ObservableCollection<HighlightViewModel> Highlights { get; } = new();

    public bool IsImageLoaded => Image != null;

    public async Task EnsureImageAsync(CancellationToken ct = default)
    {
        if (Image != null) return;
        await _renderGate.WaitAsync(ct);
        try
        {
            if (Image != null) return;
            var source = await Task.Run(() =>
            {
                using var bmp = PdfRenderer.Render(_pdfPath, Model.Number - 1, DisplayDpi);
                return BitmapConverter.ToBitmapSource(bmp);
            }, ct);
            Image = source;
        }
        finally
        {
            _renderGate.Release();
        }
    }

    public void ReleaseImage() => Image = null;

    public void RebuildHighlights(IEnumerable<FindingViewModel> findings)
    {
        Highlights.Clear();
        foreach (var f in findings.Where(f => f.Page == Number))
        {
            foreach (var r in f.Model.Rects)
            {
                var p = r.Inflate(1.0, 1.0);
                Highlights.Add(new HighlightViewModel
                {
                    Finding = f,
                    X = p.X * Scale,
                    Y = p.Y * Scale,
                    Width = p.Width * Scale,
                    Height = p.Height * Scale,
                });
            }
        }
    }

    /// <summary>Converts a rectangle in rendered pixels to page points.</summary>
    public RectPt ToPagePoints(double x, double y, double w, double h) => new(x / Scale, y / Scale, w / Scale, h / Scale);
}
