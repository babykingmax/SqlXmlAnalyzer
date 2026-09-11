using System;
using System.Windows.Input;
using SqlXmlAnalyzer.Core.Mvvm;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.ViewModels;

public sealed class AnalysisOperationViewModel : ObservableObject
{
    private AnalysisProgress _progress = new(0, 0, AnalysisOperationState.Idle, "", []);
    public AnalysisProgress Progress => _progress;
    public string Status => _progress.DisplayText;
    public string Detail => _progress.Detail;
    public bool IsBusy => _progress.IsBusy;
    public ICommand CancelCommand { get; }
    public AnalysisOperationViewModel(Action? cancel = null) => CancelCommand = new RelayCommand(_ => cancel?.Invoke(), _ => IsBusy);
    public void Update(AnalysisProgress progress)
    {
        if (progress.Revision <= _progress.Revision) return;
        _progress = progress;
        OnPropertyChanged(nameof(Progress)); OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(Detail)); OnPropertyChanged(nameof(IsBusy));
        CommandManager.InvalidateRequerySuggested();
    }
}
