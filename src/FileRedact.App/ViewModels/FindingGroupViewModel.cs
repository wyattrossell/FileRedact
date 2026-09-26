using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using FileRedact.Core.Model;

namespace FileRedact.App.ViewModels;

/// <summary>
/// All findings with the same text ("Wyatt" found 50 times on 20 pages) shown as one row. Ticking the row
/// selects or clears every occurrence; expanding it lists the occurrences individually.
/// </summary>
public partial class FindingGroupViewModel : ObservableObject
{
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);
    private bool _suppress;

    public FindingGroupViewModel(string key, IEnumerable<FindingViewModel> items)
    {
        Key = key;
        Items = new ObservableCollection<FindingViewModel>(items.OrderBy(f => f.Page).ThenBy(f => f.Model.Rects[0].Y).ThenBy(f => f.Model.Rects[0].X));
        foreach (var f in Items) f.AcceptedChanged += _ => Recompute();
        var first = Items[0];
        Text = first.Text;
        Category = Items.GroupBy(f => f.Category).OrderByDescending(g => g.Count()).First().Key;
        Recompute();
    }

    /// <summary>Case- and whitespace-insensitive grouping key; manual area selections are never merged.</summary>
    public static string KeyFor(FindingViewModel f)
        => f.Category == PiiCategory.Manual && f.Model.Rects.Count > 0 && f.Text.StartsWith("Area ", StringComparison.Ordinal)
            ? "manual:" + f.Model.Id
            : Spaces.Replace(f.Text.Trim(), " ").ToLowerInvariant();

    public string Key { get; }
    public string Text { get; }
    public PiiCategory Category { get; }
    public string CategoryName => Category.DisplayName();
    public ObservableCollection<FindingViewModel> Items { get; }
    public int Count => Items.Count;
    public FindingViewModel First => Items[0];

    /// <summary>"p.3" for a single occurrence, "×12 on 4 pages" for many.</summary>
    public string Summary
    {
        get
        {
            var pages = Items.Select(f => f.Page).Distinct().Order().ToList();
            if (Items.Count == 1) return $"p.{pages[0]}";
            var pagesText = pages.Count == 1 ? $"p.{pages[0]}" : pages.Count <= 4 ? "p." + string.Join(", ", pages) : $"{pages.Count} pages";
            return $"×{Items.Count} · {pagesText}";
        }
    }

    public string ToolTip => Items.Count == 1
        ? First.ToolTip
        : $"{CategoryName}\n{Items.Count} occurrences on {Items.Select(f => f.Page).Distinct().Count()} page(s)\nTick to black out all of them; expand to pick individual ones.";

    /// <summary>True = every occurrence selected, false = none, null = some.</summary>
    [ObservableProperty]
    private bool? _isChecked;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private int _acceptedCount;

    partial void OnIsCheckedChanged(bool? value)
    {
        if (_suppress || value == null) return;
        _suppress = true;
        foreach (var f in Items) f.Accepted = value.Value;
        _suppress = false;
        Recompute();
    }

    private void Recompute()
    {
        if (_suppress) return;
        var accepted = Items.Count(f => f.Accepted);
        AcceptedCount = accepted;
        _suppress = true;
        IsChecked = accepted == 0 ? false : accepted == Items.Count ? true : null;
        _suppress = false;
        OnPropertyChanged(nameof(StateText));
    }

    public string StateText => Items.Count == 1 ? "" : AcceptedCount == Items.Count ? "all" : AcceptedCount == 0 ? "none" : $"{AcceptedCount} of {Items.Count}";
}
