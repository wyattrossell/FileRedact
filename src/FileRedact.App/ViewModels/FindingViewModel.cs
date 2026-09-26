using CommunityToolkit.Mvvm.ComponentModel;
using FileRedact.Core.Model;

namespace FileRedact.App.ViewModels;

public partial class FindingViewModel : ObservableObject
{
    public Finding Model { get; }

    public FindingViewModel(Finding model)
    {
        Model = model;
        _accepted = model.Accepted;
    }

    [ObservableProperty]
    private bool _accepted;

    partial void OnAcceptedChanged(bool value)
    {
        Model.Accepted = value;
        AcceptedChanged?.Invoke(this);
    }

    public event Action<FindingViewModel>? AcceptedChanged;

    public PiiCategory Category => Model.Category;
    public string CategoryName => Model.Category.DisplayName();
    public string Text => Model.Text;
    public int Page => Model.Page;
    public string Reason => Model.Reason;
    public double Confidence => Model.Confidence;
    public string ConfidenceText => Model.IsManual ? "manual" : $"{Model.Confidence:P0}";
    public string ToolTip => $"{CategoryName} - page {Page}\n{Reason}\nConfidence: {ConfidenceText}";
    public bool IsManual => Model.IsManual;
}
