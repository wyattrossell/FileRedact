using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileRedact.App.Services;
using FileRedact.Core;
using FileRedact.Core.Detection;
using FileRedact.Core.Documents;
using FileRedact.Core.Model;
using FileRedact.Core.Pdf;
using FileRedact.Core.Printer;
using FileRedact.Core.Settings;
using Microsoft.Win32;

namespace FileRedact.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DetectionEngine _engine = new();
    private RedactDocument? _document;

    public MainViewModel()
    {
        Settings = UserSettings.Current;
        foreach (var t in Settings.CustomTerms) CustomTerms.Add(t);
        foreach (var c in PiiCategoryInfo.All)
            Categories.Add(new CategoryGroupViewModel(c, SetCategory));
        OutputDpi = Settings.OutputDpi;
        KeepTextLayer = Settings.KeepTextLayer;
        RefreshPrinterState();
    }

    public UserSettings Settings { get; }

    public ObservableCollection<PageViewModel> Pages { get; } = new();
    public ObservableCollection<FindingViewModel> Findings { get; } = new();
    public ObservableCollection<CategoryGroupViewModel> Categories { get; } = new();
    public ObservableCollection<string> CustomTerms { get; } = new();

    /// <summary>Raised when the view should scroll a finding into view.</summary>
    public event Action<FindingViewModel>? ScrollToFindingRequested;

    [ObservableProperty] private string _status = "Open a document, or print to the FileRedact printer.";
    // Save/Print are enabled only when a document is loaded and nothing is running; re-evaluate whenever IsBusy flips.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveRedactedCommand))]
    [NotifyCanExecuteChangedFor(nameof(PrintCommand))]
    private bool _isBusy;
    [ObservableProperty] private string? _documentName;
    [ObservableProperty] private string? _sourcePath;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ZoomPercent))] private double _zoom = 1.0;
    [ObservableProperty] private bool _previewRedactions;
    [ObservableProperty] private string _customTermInput = "";
    [ObservableProperty] private bool _printerInstalled;
    [ObservableProperty] private string _printerButtonText = "Install printer";
    [ObservableProperty] private int _outputDpi = 200;
    [ObservableProperty] private bool _keepTextLayer = true;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string? _lastOutputPath;
    [ObservableProperty] private FindingViewModel? _selectedFinding;

    public string ZoomPercent => $"{Zoom:P0}";
    public bool HasDocument => _document != null;
    public RedactDocument? Document => _document;
    public int[] DpiChoices { get; } = { 150, 200, 300 };

    partial void OnSelectedFindingChanged(FindingViewModel? value)
    {
        if (value != null) ScrollToFindingRequested?.Invoke(value);
    }

    partial void OnOutputDpiChanged(int value) { Settings.OutputDpi = value; SaveSettings(); }
    partial void OnKeepTextLayerChanged(bool value) { Settings.KeepTextLayer = value; SaveSettings(); }

    private void SaveSettings()
    {
        try { Settings.Save(); } catch { }
    }

    // ---------------------------------------------------------------- loading

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var dlg = new OpenFileDialog { Filter = DocumentConverter.FileDialogFilter, Title = "Open document to redact" };
        if (dlg.ShowDialog() != true) return;
        await LoadFileAsync(dlg.FileName);
    }

    public async Task LoadFileAsync(string path)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            Status = $"Loading {Path.GetFileName(path)}…";
            var progress = new Progress<string>(s => Status = s);
            var (doc, report) = await DocumentLoader.LoadAsync(path, progress);

            Status = "Scanning for personal information…";
            var findings = await Task.Run(() => _engine.Detect(doc, BuildDetectionOptions()));
            doc.Findings.AddRange(findings);

            SetDocument(doc);

            var notes = new List<string> { report.ConversionNote };
            if (report.OcrPages > 0) notes.Add($"OCR used on {report.OcrPages} page(s)");
            notes.AddRange(report.Warnings);
            var selected = findings.Count(f => f.Accepted);
            Status = findings.Count == 0
                ? $"Loaded {doc.Pages.Count} page(s). No personal information was detected - drag on the page to redact anything the scan missed, then Save or Print. ({string.Join(" · ", notes)})"
                : $"Loaded {doc.Pages.Count} page(s). {findings.Count} item(s) found, {selected} pre-selected. Review the highlights, then click Save redacted PDF or Print. ({string.Join(" · ", notes)})";
            LastOutputPath = null;
        }
        catch (Exception ex)
        {
            Status = "Failed to load: " + ex.Message;
            MessageBox.Show(ex.Message, "FileRedact - could not open document", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private DetectionOptions BuildDetectionOptions() => new()
    {
        CustomTerms = CustomTerms.ToList(),
        PropagateNames = Settings.PropagateNames,
        DisabledCategories = Settings.DisabledCategories.ToHashSet(),
    };

    private void SetDocument(RedactDocument doc)
    {
        _document = doc;
        DocumentName = doc.DisplayName;
        SourcePath = doc.SourcePath;
        Pages.Clear();
        Findings.Clear();
        foreach (var p in doc.Pages) Pages.Add(new PageViewModel(p, doc.WorkingPdfPath));
        foreach (var f in doc.Findings.OrderBy(f => f.Page).ThenBy(f => f.Rects[0].Y).ThenBy(f => f.Rects[0].X))
            AddFindingVm(f);
        RefreshHighlights();
        OnPropertyChanged(nameof(HasDocument));
        SaveRedactedCommand.NotifyCanExecuteChanged();
        PrintCommand.NotifyCanExecuteChanged();
    }

    private FindingViewModel AddFindingVm(Finding f)
    {
        var vm = new FindingViewModel(f);
        vm.AcceptedChanged += _ => UpdateSummary();
        Findings.Add(vm);
        return vm;
    }

    private void RefreshHighlights()
    {
        foreach (var p in Pages) p.RebuildHighlights(Findings);
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        foreach (var c in Categories) c.Update(Findings);
        var accepted = Findings.Count(f => f.Accepted);
        Summary = _document == null ? "" : $"{Findings.Count} item(s) found, {accepted} selected for redaction";
    }

    // ---------------------------------------------------------------- review actions

    private void SetCategory(PiiCategory category, bool accepted)
    {
        foreach (var f in Findings.Where(f => f.Category == category)) f.Accepted = accepted;
    }

    [RelayCommand] private void AcceptAll() { foreach (var f in Findings) f.Accepted = true; }
    [RelayCommand] private void RejectAll() { foreach (var f in Findings) f.Accepted = false; }

    public void ToggleFinding(FindingViewModel f) => f.Accepted = !f.Accepted;

    [RelayCommand]
    private void RemoveFinding(FindingViewModel? f)
    {
        if (f == null || _document == null) return;
        _document.Findings.Remove(f.Model);
        Findings.Remove(f);
        RefreshHighlights();
    }

    /// <summary>Redact every occurrence of the finding's text across the document and remember it as a custom term.</summary>
    [RelayCommand]
    private void RedactAllOccurrences(FindingViewModel? f)
    {
        if (f == null || _document == null) return;
        var term = f.Text.Trim();
        if (term.Length < 2) return;
        AddTermInternal(term, rescan: true);
    }

    [RelayCommand]
    private void AddCustomTerm()
    {
        var term = CustomTermInput.Trim();
        if (term.Length == 0) return;
        AddTermInternal(term, rescan: true);
        CustomTermInput = "";
    }

    private void AddTermInternal(string term, bool rescan)
    {
        if (!CustomTerms.Contains(term, StringComparer.OrdinalIgnoreCase))
        {
            CustomTerms.Add(term);
            Settings.CustomTerms = CustomTerms.ToList();
            SaveSettings();
        }
        if (!rescan || _document == null) return;

        var existing = Findings.Select(f => (f.Page, f.Model.TextStart, f.Model.TextLength)).ToHashSet();
        var added = 0;
        foreach (var nf in DetectionEngine.FindTerm(_document, term, PiiCategory.CustomTerm))
        {
            // Skip when an identical-span finding already exists; otherwise make sure it is accepted.
            var same = Findings.FirstOrDefault(f => f.Page == nf.Page && f.Model.TextStart == nf.TextStart && f.Model.TextLength == nf.TextLength);
            if (same != null) { same.Accepted = true; continue; }
            _document.Findings.Add(nf);
            AddFindingVm(nf);
            added++;
        }
        RefreshHighlights();
        Status = added > 0 ? $"Added {added} occurrence(s) of \"{term}\"." : $"\"{term}\" will be redacted in future documents.";
    }

    [RelayCommand]
    private void RemoveCustomTerm(string? term)
    {
        if (term == null) return;
        CustomTerms.Remove(term);
        Settings.CustomTerms = CustomTerms.ToList();
        SaveSettings();
    }

    /// <summary>Adds a manual redaction from a drag-selection on a page (in page points).</summary>
    public void AddManualFinding(PageViewModel page, RectPt rect)
    {
        if (_document == null) return;
        var words = page.Model.WordsIntersecting(rect).ToList();
        Finding finding;
        if (words.Count > 0)
        {
            finding = new Finding
            {
                Category = PiiCategory.Manual,
                Page = page.Number,
                Text = string.Join(" ", words.OrderBy(w => w.Line).ThenBy(w => w.Rect.X).Select(w => w.Text)),
                Rects = DetectionEngine.MergeByLine(words),
                IsManual = true,
                Accepted = true,
                Reason = "Selected by reviewer",
            };
        }
        else
        {
            finding = new Finding
            {
                Category = PiiCategory.Manual,
                Page = page.Number,
                Text = $"Area {rect.Width:0}×{rect.Height:0} pt",
                Rects = new[] { rect },
                IsManual = true,
                Accepted = true,
                Reason = "Area selected by reviewer",
            };
        }
        _document.Findings.Add(finding);
        AddFindingVm(finding);
        RefreshHighlights();
    }

    [RelayCommand] private void ZoomIn() => Zoom = Math.Min(4.0, Math.Round(Zoom * 1.25, 2));
    [RelayCommand] private void ZoomOut() => Zoom = Math.Max(0.25, Math.Round(Zoom / 1.25, 2));
    [RelayCommand] private void ZoomReset() => Zoom = 1.0;

    // ---------------------------------------------------------------- output

    private RedactionOptions BuildRedactionOptions() => new() { Dpi = OutputDpi, KeepTextLayer = KeepTextLayer };

    private bool CanExport() => _document != null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task SaveRedactedAsync()
    {
        if (_document == null) return;
        var accepted = Findings.Where(f => f.Accepted).Select(f => f.Model).ToList();
        if (accepted.Count == 0)
        {
            var r = MessageBox.Show("No items are selected for redaction. Save an unredacted copy anyway?", "FileRedact", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
        }

        var baseName = Path.GetFileNameWithoutExtension(_document.SourcePath);
        var dlg = new SaveFileDialog
        {
            Filter = "PDF document|*.pdf",
            FileName = baseName + "-REDACTED.pdf",
            Title = "Save redacted PDF",
            InitialDirectory = Settings.LastOutputFolder is { } d && Directory.Exists(d) ? d : Path.GetDirectoryName(_document.SourcePath),
        };
        if (dlg.ShowDialog() != true) return;

        if (!await ExportAsync(dlg.FileName)) return;
        var open = MessageBox.Show(
            $"Redacted PDF saved with {accepted.Count} item(s) blacked out:\n{dlg.FileName}\n\nOpen it now to check the result?",
            "FileRedact - saved", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (open == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
    }

    /// <summary>Writes the redacted PDF for the currently selected findings to <paramref name="outputPath"/>.</summary>
    public async Task<bool> ExportAsync(string outputPath)
    {
        if (_document == null) return false;
        var accepted = Findings.Where(f => f.Accepted).Select(f => f.Model).ToList();
        IsBusy = true;
        try
        {
            var options = BuildRedactionOptions();
            var progress = new Progress<(int Page, int Total)>(p => Status = $"Writing redacted page {p.Page} of {p.Total}…");
            var doc = _document;
            await Task.Run(() => RedactionWriter.Write(doc, accepted, outputPath, options, progress));
            Settings.LastOutputFolder = Path.GetDirectoryName(outputPath);
            SaveSettings();
            LastOutputPath = outputPath;
            Status = $"Saved redacted PDF: {outputPath} ({accepted.Count} item(s) removed)";
            return true;
        }
        catch (Exception ex)
        {
            Status = "Failed to save: " + ex.Message;
            MessageBox.Show(ex.Message, "FileRedact - could not save", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void Print()
    {
        if (_document == null) return;
        var accepted = Findings.Where(f => f.Accepted).Select(f => f.Model).ToList();
        try
        {
            // Show the pages as they will print (redactions burned in); the preview hands off to the printer dialog.
            var preview = new PrintPreviewWindow(_document, accepted, BuildRedactionOptions());
            if (Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => ReferenceEquals(w.ViewModel, this) && w.IsVisible) is { } owner)
                preview.Owner = owner;
            preview.ShowDialog();
            if (preview.Printed) Status = "Sent redacted document to the printer.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "FileRedact - print failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (LastOutputPath == null || !File.Exists(LastOutputPath)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{LastOutputPath}\"") { UseShellExecute = true });
    }

    // ---------------------------------------------------------------- printer

    public void RefreshPrinterState()
    {
        PrinterInstalled = PrinterInstaller.IsInstalled();
        PrinterButtonText = PrinterInstalled ? "Remove FileRedact printer" : "Install FileRedact printer";
    }

    [RelayCommand]
    private async Task TogglePrinterAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (PrinterInstalled)
            {
                var r = MessageBox.Show("Remove the FileRedact printer from this computer?", "FileRedact", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
                Status = "Removing printer (Windows will ask for permission)…";
                var code = await PrinterInstaller.UninstallAsync();
                Status = code == 0 ? "FileRedact printer removed." : code == null ? "Printer removal cancelled." : $"Printer removal failed (exit code {code}).";
            }
            else
            {
                Status = "Installing printer (Windows will ask for permission)…";
                var code = await PrinterInstaller.InstallAsync();
                if (code == 0)
                {
                    PrinterInstaller.RegisterWatcherAtLogin(Environment.ProcessPath ?? "FileRedact.exe");
                    Status = "FileRedact printer installed. Print any document to \"FileRedact\" to open it here.";
                }
                else
                {
                    Status = code == null ? "Printer installation cancelled." : $"Printer installation failed (exit code {code}). See the PowerShell output for details.";
                }
            }
        }
        finally
        {
            RefreshPrinterState();
            IsBusy = false;
            ((App)Application.Current).SyncWatcher();
        }
    }

    public string HelpText =>
        "How to use FileRedact\n\n" +
        "1. Open a PDF, Word document or image, or print any document to the \"FileRedact\" printer.\n" +
        "2. Review the highlighted items. Ticked items (yellow) will be blacked out; unticked items are suggestions.\n" +
        "   • Click a highlight to toggle it. Drag on the page to redact a custom area.\n" +
        "   • Right-click a highlight to redact every occurrence of that text.\n" +
        "3. Use \"Preview\" to see the final result, then \"Save redacted PDF\" or \"Print\".\n\n" +
        "The output is rebuilt from scratch: pages are rasterised with the black boxes burned in, and only the\n" +
        "remaining words are written back as searchable text. Nothing from the original file is copied.\n\n" +
        "Detection covers the CJIS Security Policy PII categories: names, dates of birth, SSNs, driver's license\n" +
        "and state ID numbers, passport numbers, addresses, phone numbers, email addresses, financial account\n" +
        "numbers, vehicle identifiers, FBI/SID/booking numbers, place of birth, mother's maiden name.\n" +
        "Automatic detection is an aid, not a guarantee - always review the document before sharing it.";
}
