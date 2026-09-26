using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FileRedact.App.Services;
using FileRedact.Core.Model;
using FileRedact.Core.Pdf;

namespace FileRedact.App;

/// <summary>
/// Shows the pages exactly as they will be printed (redactions burned in), then hands off to the normal
/// Windows printer dialog. Rendering happens in the background so the window opens immediately.
/// </summary>
public partial class PrintPreviewWindow : Window
{
    private const int PreviewDpi = 96;

    public sealed partial class PreviewPage : ObservableObject
    {
        public required string Header { get; init; }
        public required double Width { get; init; }
        public required double Height { get; init; }
        [ObservableProperty] private ImageSource? _image;
        public Visibility Placeholder => Image == null ? Visibility.Visible : Visibility.Collapsed;
        partial void OnImageChanged(ImageSource? value) => OnPropertyChanged(nameof(Placeholder));
    }

    private readonly RedactDocument _document;
    private readonly IReadOnlyList<Finding> _accepted;
    private readonly RedactionOptions _options;
    private readonly CancellationTokenSource _cts = new();
    private double _zoom = 1.0;

    public ObservableCollection<PreviewPage> Pages { get; } = new();
    public ICommand PrintCommand { get; }

    /// <summary>True when the user went on to print from the preview.</summary>
    public bool Printed { get; private set; }

    public PrintPreviewWindow(RedactDocument document, IReadOnlyList<Finding> accepted, RedactionOptions options)
    {
        InitializeComponent();
        _document = document;
        _accepted = accepted;
        _options = options;
        PrintCommand = new RelayAction(() => Print_Click(this, new RoutedEventArgs()));

        var scale = PreviewDpi / 72.0;
        foreach (var p in document.Pages)
            Pages.Add(new PreviewPage { Header = $"Page {p.Number}", Width = Math.Round(p.Width * scale), Height = Math.Round(p.Height * scale) });
        PagesHost.ItemsSource = Pages;
        PageInfo.Text = $"{Pages.Count} page(s)";
        Status.Text = $"{accepted.Count} item(s) will be blacked out.";

        Loaded += async (_, _) =>
        {
            FitWidth();
            await RenderAllAsync();
        };
    }

    private async Task RenderAllAsync()
    {
        Progress.Visibility = Visibility.Visible;
        try
        {
            var previewOptions = new RedactionOptions { Dpi = PreviewDpi, Padding = _options.Padding };
            for (var i = 0; i < Pages.Count; i++)
            {
                if (_cts.IsCancellationRequested) return;
                var page = _document.Pages[i];
                var doc = _document;
                var accepted = _accepted;
                var source = await Task.Run(() =>
                {
                    using var bmp = RedactionWriter.RenderRedactedPage(doc, page, accepted, previewOptions);
                    return BitmapConverter.ToBitmapSource(bmp);
                }, _cts.Token);
                Pages[i].Image = source;
                Progress.Value = (i + 1.0) / Pages.Count;
                Status.Text = $"Prepared page {i + 1} of {Pages.Count}. {_accepted.Count} item(s) blacked out.";
            }
            Status.Text = $"Ready to print: {Pages.Count} page(s), {_accepted.Count} item(s) blacked out.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Status.Text = "Preview failed: " + ex.Message;
        }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (PrintService.Print(_document, _accepted, _options))
            {
                Printed = true;
                Status.Text = "Sent to the printer.";
                Close();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "FileRedact - print failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ------------------------------------------------------------ zoom

    private void SetZoom(double z)
    {
        _zoom = Math.Clamp(z, 0.2, 4.0);
        ZoomTransform.ScaleX = _zoom;
        ZoomTransform.ScaleY = _zoom;
        ZoomLabel.Content = $"{_zoom:P0}";
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom * 1.25);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom / 1.25);
    private void ZoomReset_Click(object sender, RoutedEventArgs e) => SetZoom(1.0);
    private void FitWidth_Click(object sender, RoutedEventArgs e) => FitWidth();

    private void FitWidth()
    {
        if (Pages.Count == 0 || Scroll.ViewportWidth <= 0) return;
        var widest = Pages.Max(p => p.Width) + 48; // margins
        SetZoom((Scroll.ViewportWidth - 24) / widest);
    }

    private void Scroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        SetZoom(e.Delta > 0 ? _zoom * 1.1 : _zoom / 1.1);
        e.Handled = true;
    }

    private void Scroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Pages.Count == 0 || e.ExtentHeight <= 0) return;
        var pageHeight = e.ExtentHeight / Pages.Count;
        var current = Math.Clamp((int)((e.VerticalOffset + e.ViewportHeight / 2) / pageHeight) + 1, 1, Pages.Count);
        PageInfo.Text = $"Page {current} of {Pages.Count}";
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Cancel();
        base.OnClosed(e);
    }

    private sealed class RelayAction : ICommand
    {
        private readonly Action _action;
        public RelayAction(Action action) => _action = action;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _action();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
