using CommunityToolkit.Mvvm.ComponentModel;
using FileRedact.Core.Model;

namespace FileRedact.App.ViewModels;

/// <summary>Summary row for one PII category: count and a checkbox that accepts/rejects all its findings.</summary>
public partial class CategoryGroupViewModel : ObservableObject
{
    private readonly Action<PiiCategory, bool> _setAll;
    private bool _suppress;

    public CategoryGroupViewModel(PiiCategory category, Action<PiiCategory, bool> setAll)
    {
        Category = category;
        _setAll = setAll;
    }

    public PiiCategory Category { get; }
    public string DisplayName => Category.DisplayName();

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private int _acceptedCount;

    [ObservableProperty]
    private bool? _isChecked;

    public string Label => $"{DisplayName}  ({AcceptedCount}/{Count})";

    partial void OnIsCheckedChanged(bool? value)
    {
        if (_suppress || value == null) return;
        _setAll(Category, value.Value);
    }

    public void Update(IEnumerable<FindingViewModel> findings)
    {
        var list = findings.Where(f => f.Category == Category).ToList();
        Count = list.Count;
        AcceptedCount = list.Count(f => f.Accepted);
        _suppress = true;
        IsChecked = AcceptedCount == 0 ? false : AcceptedCount == Count ? true : null;
        _suppress = false;
        OnPropertyChanged(nameof(Label));
    }
}
