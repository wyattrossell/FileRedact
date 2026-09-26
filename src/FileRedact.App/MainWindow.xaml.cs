using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using FileRedact.App.ViewModels;
using FileRedact.Core.Documents;

namespace FileRedact.App;

public partial class MainWindow : Window
{
    private const double ClickTolerance = 4;

    private readonly DispatcherTimer _visibilityTimer;
    private Canvas? _dragCanvas;
    private Point _dragStart;
    private Rectangle? _dragRect;
    private HighlightViewModel? _pressedHighlight;

    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        // Fit smaller screens (the XAML defaults suit a 1080p or larger display).
        var work = SystemParameters.WorkArea;
        Width = Math.Min(Width, work.Width * 0.95);
        Height = Math.Min(Height, work.Height * 0.95);
        ViewModel = new MainViewModel();
        DataContext = ViewModel;
        ViewModel.ScrollToFindingRequested += ScrollToFinding;
        ViewModel.Pages.CollectionChanged += Pages_CollectionChanged;
        ViewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.Zoom)) ScheduleVisibilityUpdate(); };

        _visibilityTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _visibilityTimer.Tick += (_, _) => { _visibilityTimer.Stop(); UpdateVisiblePages(); };
    }

    // ------------------------------------------------------------ lazy page rendering

    private void Pages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleVisibilityUpdate();

    private void ScheduleVisibilityUpdate()
    {
        _visibilityTimer.Stop();
        _visibilityTimer.Start();
    }

    private void PagesScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) => ScheduleVisibilityUpdate();

    /// <summary>Renders pages near the viewport and frees bitmaps of pages far away, so large documents stay responsive.</summary>
    private void UpdateVisiblePages()
    {
        if (ViewModel.Pages.Count == 0) return;
        var viewportH = PagesScroll.ViewportHeight;
        if (viewportH <= 0) return;

        foreach (var page in ViewModel.Pages)
        {
            if (PagesHost.ItemContainerGenerator.ContainerFromItem(page) is not FrameworkElement container || !container.IsLoaded) continue;
            Rect bounds;
            try
            {
                var topLeft = container.TransformToVisual(PagesScroll).Transform(new Point(0, 0));
                bounds = new Rect(topLeft, new Size(container.ActualWidth * ViewModel.Zoom, container.ActualHeight * ViewModel.Zoom));
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            var distance = bounds.Bottom < 0 ? -bounds.Bottom : bounds.Top > viewportH ? bounds.Top - viewportH : 0;
            if (distance < viewportH * 1.5)
                _ = page.EnsureImageAsync();
            else if (distance > viewportH * 5 && page.IsImageLoaded)
                page.ReleaseImage();
        }
    }

    private void PagesScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (e.Delta > 0) ViewModel.ZoomInCommand.Execute(null);
        else ViewModel.ZoomOutCommand.Execute(null);
        e.Handled = true;
    }

    private void ScrollToFinding(FindingViewModel finding)
    {
        var page = ViewModel.Pages.FirstOrDefault(p => p.Number == finding.Page);
        if (page == null) return;
        if (PagesHost.ItemContainerGenerator.ContainerFromItem(page) is not FrameworkElement container) return;
        try
        {
            var topLeft = container.TransformToVisual(PagesScroll).Transform(new Point(0, 0));
            var rect = finding.Model.Rects[0];
            var yInPage = (rect.Y * page.Scale + 22) * ViewModel.Zoom; // +header height
            var target = PagesScroll.VerticalOffset + topLeft.Y + yInPage - PagesScroll.ViewportHeight / 3;
            PagesScroll.ScrollToVerticalOffset(Math.Max(0, target));
        }
        catch (InvalidOperationException) { }
    }

    // ------------------------------------------------------------ page mouse interaction

    private static PageViewModel? PageOf(object sender) => (sender as Canvas)?.Tag as PageViewModel;

    private static HighlightViewModel? HighlightAt(MouseEventArgs e)
        => (e.OriginalSource as FrameworkElement)?.DataContext as HighlightViewModel;

    private void Page_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Canvas canvas || PageOf(sender) == null) return;
        _dragCanvas = canvas;
        _dragStart = e.GetPosition(canvas);
        _pressedHighlight = HighlightAt(e);
        _dragRect = null;
        canvas.CaptureMouse();
        e.Handled = true;
    }

    private void Page_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCanvas == null || sender != _dragCanvas || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(_dragCanvas);
        var dx = Math.Abs(pos.X - _dragStart.X);
        var dy = Math.Abs(pos.Y - _dragStart.Y);
        if (_dragRect == null)
        {
            if (dx < ClickTolerance && dy < ClickTolerance) return;
            _dragRect = new Rectangle
            {
                Stroke = Brushes.Black,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Fill = new SolidColorBrush(Color.FromArgb(0x40, 0, 0, 0)),
                IsHitTestVisible = false,
            };
            _dragCanvas.Children.Add(_dragRect);
        }
        var r = Normalise(_dragStart, pos, _dragCanvas);
        Canvas.SetLeft(_dragRect, r.X);
        Canvas.SetTop(_dragRect, r.Y);
        _dragRect.Width = r.Width;
        _dragRect.Height = r.Height;
    }

    private void Page_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragCanvas == null || sender != _dragCanvas) return;
        var canvas = _dragCanvas;
        var page = PageOf(canvas)!;
        var pos = e.GetPosition(canvas);
        canvas.ReleaseMouseCapture();
        _dragCanvas = null;

        if (_dragRect != null)
        {
            canvas.Children.Remove(_dragRect);
            var r = Normalise(_dragStart, pos, canvas);
            _dragRect = null;
            if (r.Width >= ClickTolerance && r.Height >= ClickTolerance)
                ViewModel.AddManualFinding(page, page.ToPagePoints(r.X, r.Y, r.Width, r.Height));
        }
        else if (_pressedHighlight != null)
        {
            ViewModel.ToggleFinding(_pressedHighlight.Finding);
        }
        _pressedHighlight = null;
        e.Handled = true;
    }

    private void Page_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var h = HighlightAt(e);
        if (h == null) return;
        var f = h.Finding;
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = f.Accepted ? "Do not redact this item" : "Redact this item", Command = new RelayAction(() => ViewModel.ToggleFinding(f)) });
        menu.Items.Add(new MenuItem { Header = $"Redact all occurrences of \"{Shorten(f.Text)}\"", Command = ViewModel.RedactAllOccurrencesCommand, CommandParameter = f });
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Remove this finding", Command = ViewModel.RemoveFindingCommand, CommandParameter = f });
        menu.Items.Add(new MenuItem { Header = "Show in list", Command = new RelayAction(() => { ViewModel.SelectedFinding = f; FindingsList.ScrollIntoView(f); }) });
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static string Shorten(string s) => s.Length > 40 ? s[..37] + "…" : s;

    private static Rect Normalise(Point a, Point b, Canvas canvas)
    {
        var x1 = Math.Clamp(Math.Min(a.X, b.X), 0, canvas.ActualWidth);
        var x2 = Math.Clamp(Math.Max(a.X, b.X), 0, canvas.ActualWidth);
        var y1 = Math.Clamp(Math.Min(a.Y, b.Y), 0, canvas.ActualHeight);
        var y2 = Math.Clamp(Math.Max(a.Y, b.Y), 0, canvas.ActualHeight);
        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    // ------------------------------------------------------------ misc UI

    private void CustomTerm_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.AddCustomTermCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Help_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show(this, ViewModel.HelpText, "FileRedact help", MessageBoxButton.OK, MessageBoxImage.Information);

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        var first = files.FirstOrDefault(DocumentConverter.IsSupported) ?? files[0];
        await ViewModel.LoadFileAsync(first);
        foreach (var extra in files.Skip(1).Where(f => f != first && DocumentConverter.IsSupported(f)))
            App.Current.OpenDocument(extra);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        var others = Application.Current.Windows.OfType<MainWindow>().Count(w => w != this && w.IsVisible);
        if (others == 0 && App.Current.KeepRunningInBackground)
        {
            // Keep waiting for print jobs from the notification area.
            e.Cancel = true;
            Hide();
            return;
        }
        if (others == 0)
        {
            App.Current.ExitApplication();
        }
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
