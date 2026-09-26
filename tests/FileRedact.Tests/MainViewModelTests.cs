using FileRedact.App.ViewModels;
using FileRedact.Core.Documents;
using FileRedact.Core.Model;
using UglyToad.PdfPig;
using Xunit;

namespace FileRedact.Tests;

/// <summary>
/// Exercises the review workflow exactly as the window drives it: load, review, toggle, add manual areas and
/// custom terms, then export. Runs on an STA thread because the view model is a WPF component.
/// </summary>
public class MainViewModelTests
{
    private static readonly string[] Lines =
    {
        "CITATION",
        "Driver: Alvarez, Carmen R   DOB: 02/14/1990   DL# A9876543",
        "Address: 55 Pine Court, Peoria, IL 61602   Phone: 309-555-0100",
        "Officer Daniel Chen observed the vehicle at Main Street and Oak Avenue.",
        "Alvarez was advised of the citation. Nothing further.",
    };

    private static string BuildSample()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fileredact-vm-{Guid.NewGuid():N}.pdf");
        SimplePdfBuilder.FromParagraphs(Lines, path);
        return path;
    }

    private static Task RunSta(Func<Task> body)
    {
        var tcs = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(dispatcher));
                var frame = new System.Windows.Threading.DispatcherFrame();
                var task = body();
                task.ContinueWith(_ => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                task.GetAwaiter().GetResult();
                dispatcher.InvokeShutdown();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    [Fact]
    public Task Load_enables_save_and_print_and_prepares_findings() => RunSta(async () =>
    {
        var sample = BuildSample();
        var vm = new MainViewModel();
        Assert.False(vm.SaveRedactedCommand.CanExecute(null));
        Assert.False(vm.PrintCommand.CanExecute(null));

        await vm.LoadFileAsync(sample);

        Assert.True(vm.HasDocument);
        Assert.False(vm.IsBusy);
        Assert.True(vm.SaveRedactedCommand.CanExecute(null), "Save must be available once a document is loaded");
        Assert.True(vm.PrintCommand.CanExecute(null), "Print must be available once a document is loaded");
        Assert.Single(vm.Pages);
        Assert.NotEmpty(vm.Pages[0].Highlights);
        Assert.Contains(vm.Findings, f => f.Text.Contains("Alvarez, Carmen R") && f.Accepted);
        Assert.Contains(vm.Findings, f => f.Text == "02/14/1990" && f.Category == PiiCategory.DateOfBirth);
        Assert.Contains(vm.Findings, f => f.Text == "A9876543" && f.Category == PiiCategory.DriverLicense);
        Assert.Contains(vm.Findings, f => f.Text.StartsWith("55 Pine Court") && f.Category == PiiCategory.Address);
        Assert.Contains(vm.Findings, f => f.Text == "309-555-0100");
        Assert.Contains(vm.Findings, f => f.Text == "Daniel Chen");
        Assert.Contains(vm.Findings, f => f.Text == "Alvarez" && f.Reason.StartsWith("Matches part"));
        Assert.Contains(vm.Categories, c => c.Category == PiiCategory.Name && c.Count >= 2);
        Assert.Contains("Save redacted PDF", vm.Status);
        File.Delete(sample);
    });

    [Fact]
    public Task Toggle_select_all_and_clear_all_update_summary_and_highlights() => RunSta(async () =>
    {
        var sample = BuildSample();
        var vm = new MainViewModel();
        await vm.LoadFileAsync(sample);

        vm.RejectAllCommand.Execute(null);
        Assert.All(vm.Findings, f => Assert.False(f.Accepted));
        Assert.Contains("0 selected", vm.Summary);

        vm.AcceptAllCommand.Execute(null);
        Assert.All(vm.Findings, f => Assert.True(f.Accepted));
        Assert.Contains($"{vm.Findings.Count} selected", vm.Summary);

        var first = vm.Findings[0];
        vm.ToggleFinding(first);
        Assert.False(first.Accepted);
        Assert.False(first.Model.Accepted);
        Assert.Contains($"{vm.Findings.Count - 1} selected", vm.Summary);

        var cat = vm.Categories.First(c => c.Category == first.Category);
        Assert.True(cat.AcceptedCount == cat.Count - 1);
        File.Delete(sample);
    });

    [Fact]
    public Task Manual_area_custom_terms_and_export_produce_a_redacted_pdf() => RunSta(async () =>
    {
        var sample = BuildSample();
        var vm = new MainViewModel();
        await vm.LoadFileAsync(sample);
        var page = vm.Pages[0];

        // Drag-select the word "CITATION" on the first line.
        var citation = page.Model.Words.First(w => w.Text == "CITATION");
        var before = vm.Findings.Count;
        vm.AddManualFinding(page, citation.Rect.Inflate(2, 2));
        Assert.Equal(before + 1, vm.Findings.Count);
        var manual = vm.Findings.Last();
        Assert.Equal(PiiCategory.Manual, manual.Category);
        Assert.Equal("CITATION", manual.Text);
        Assert.True(manual.Accepted);

        // "Always redact" term picks up both mentions of the street name.
        vm.CustomTermInput = "Main Street";
        vm.AddCustomTermCommand.Execute(null);
        Assert.Contains("Main Street", vm.CustomTerms);
        Assert.Contains(vm.Findings, f => f.Category == PiiCategory.CustomTerm && f.Text == "Main Street" && f.Accepted);
        vm.RemoveCustomTermCommand.Execute("Main Street");
        Assert.DoesNotContain("Main Street", vm.CustomTerms);

        // Right-click "redact all occurrences" on the officer's name: it becomes an always-redact term and every
        // occurrence is selected (an existing finding with the identical span is re-used rather than duplicated).
        var chen = vm.Findings.First(f => f.Text == "Daniel Chen");
        chen.Accepted = false;
        vm.RedactAllOccurrencesCommand.Execute(chen);
        Assert.Contains("Daniel Chen", vm.CustomTerms);
        Assert.All(vm.Findings.Where(f => f.Text == "Daniel Chen"), f => Assert.True(f.Accepted));
        Assert.Single(vm.Findings, f => f.Text == "Daniel Chen");
        vm.RemoveCustomTermCommand.Execute("Daniel Chen");

        // Leave the incident location in, everything else out.
        var output = Path.ChangeExtension(sample, ".redacted.pdf");
        Assert.True(await vm.ExportAsync(output));
        Assert.True(File.Exists(output));
        Assert.Equal(output, vm.LastOutputPath);
        Assert.False(vm.IsBusy);
        Assert.True(vm.SaveRedactedCommand.CanExecute(null), "Save must remain available after exporting");
        Assert.True(vm.PrintCommand.CanExecute(null), "Print must remain available after exporting");

        using (var pdf = PdfDocument.Open(output))
        {
            var text = string.Join(" ", pdf.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
            Assert.DoesNotContain("CITATION", text);
            Assert.DoesNotContain("Carmen", text);
            Assert.DoesNotContain("A9876543", text);
            Assert.DoesNotContain("Alvarez", text);
            Assert.Contains("observed", text);
            Assert.Contains("Nothing", text);
        }

        File.Delete(sample);
        File.Delete(output);
    });
}
