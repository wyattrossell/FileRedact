using FileRedact.App.ViewModels;
using FileRedact.Core.Documents;
using FileRedact.Core.Model;
using Xunit;

namespace FileRedact.Tests;

/// <summary>Grouping of identical values and bulk selection, as used by the findings panel and drag-selection.</summary>
public class FindingGroupTests
{
    private static readonly string[] Lines =
    {
        "Subject: Wyatt Rossell was interviewed. Rossell stated that Rossell's car was taken.",
        "Witness: Dana Pruitt confirmed the account given by Rossell.",
        "Rossell signed the statement. Nothing further.",
    };

    private static string BuildSample()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fileredact-group-{Guid.NewGuid():N}.pdf");
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
            catch (Exception ex) { tcs.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    [Fact]
    public Task Identical_values_form_one_group_whose_checkbox_drives_every_occurrence() => RunSta(async () =>
    {
        var sample = BuildSample();
        var vm = new MainViewModel();
        await vm.LoadFileAsync(sample);

        var rossell = vm.Groups.Single(g => g.Text.Equals("Rossell", StringComparison.OrdinalIgnoreCase));
        Assert.True(rossell.Count >= 3, $"expected the surname to be found several times, got {rossell.Count}");
        Assert.True(rossell.Items.Select(f => f.Page).Distinct().Count() >= 1);
        Assert.Contains("×", rossell.Summary);
        Assert.Equal(true, rossell.IsChecked);
        Assert.Equal(PiiCategory.Name, rossell.Category);

        // One tick clears every occurrence, and the page highlights follow.
        rossell.IsChecked = false;
        Assert.All(rossell.Items, f => Assert.False(f.Accepted));
        Assert.All(vm.Pages[0].Highlights.Where(h => rossell.Items.Contains(h.Finding)), h => Assert.False(h.Finding.Accepted));

        // Changing a single occurrence makes the group indeterminate; ticking the group selects all again.
        rossell.Items[0].Accepted = true;
        Assert.Null(rossell.IsChecked);
        Assert.Equal($"1 of {rossell.Count}", rossell.StateText);
        rossell.IsChecked = true;
        Assert.All(rossell.Items, f => Assert.True(f.Accepted));

        // The possessive "Rossell's" is a different value and therefore its own row.
        Assert.Contains(vm.Groups, g => g.Text.Equals("Rossell's", StringComparison.OrdinalIgnoreCase));
        // Every finding belongs to exactly one group.
        Assert.Equal(vm.Findings.Count, vm.Groups.Sum(g => g.Count));
        File.Delete(sample);
    });

    [Fact]
    public Task Bulk_selection_and_removal_apply_to_all_given_findings() => RunSta(async () =>
    {
        var sample = BuildSample();
        var vm = new MainViewModel();
        await vm.LoadFileAsync(sample);
        var page = vm.Pages[0];

        // Everything on the first line, as a drag-selection over the page would gather it.
        var firstLine = page.Model.Words.Where(w => w.Line == 0).Select(w => w.Rect).Aggregate((a, b) => a.Union(b));
        var hit = page.Highlights.Where(h => new RectPt(h.X / page.Scale, h.Y / page.Scale, h.Width / page.Scale, h.Height / page.Scale).Intersects(firstLine))
                      .Select(h => h.Finding).Distinct().ToList();
        Assert.True(hit.Count >= 2, "expected several findings on the first line");

        vm.SetAccepted(hit, false);
        Assert.All(hit, f => Assert.False(f.Accepted));
        vm.SetAccepted(hit, true);
        Assert.All(hit, f => Assert.True(f.Accepted));

        var before = vm.Findings.Count;
        vm.RemoveFindings(hit);
        Assert.Equal(before - hit.Count, vm.Findings.Count);
        Assert.DoesNotContain(page.Highlights, h => hit.Contains(h.Finding));
        Assert.Equal(vm.Findings.Count, vm.Groups.Sum(g => g.Count));
        File.Delete(sample);
    });
}
