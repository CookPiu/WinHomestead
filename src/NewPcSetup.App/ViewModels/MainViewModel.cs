using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewPcSetup.App.ViewModels;

public sealed partial class StepItem : ObservableObject
{
    public StepItem(string label) { Label = label; }
    public string Label { get; }
    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private bool _isDone;
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private object? _wizardPage;
    private int _stepIndex;

    public MainViewModel(AppServices services)
    {
        _services = services;
        Steps = new ObservableCollection<StepItem>
        {
            new("1 电脑信息"), new("2 问卷"), new("3 方案预览"), new("4 执行"), new("5 报告"),
        };
        GoTo(0);
    }

    public ObservableCollection<StepItem> Steps { get; }

    [ObservableProperty] private object? _currentPage;

    private void GoTo(int index)
    {
        _stepIndex = index;
        for (var i = 0; i < Steps.Count; i++) { Steps[i].IsCurrent = i == index; Steps[i].IsDone = i < index; }
        _wizardPage = index switch
        {
            0 => new WelcomeViewModel(_services, () => GoTo(1)),
            1 => new QuestionnaireViewModel(_services, () => GoTo(2), () => GoTo(0)),
            2 => new PlanViewModel(_services, () => GoTo(3), () => GoTo(1)),
            3 => new ExecuteViewModel(_services, () => GoTo(4)),
            _ => new ReportViewModel(_services, ShowSoftware),
        };
        CurrentPage = _wizardPage;
    }

    [RelayCommand]
    private void ShowSoftware() => CurrentPage = new SoftwareViewModel(_services, ReturnToWizard);

    [RelayCommand]
    private void ShowStorage() => CurrentPage = new StorageViewModel(_services, ReturnToWizard);

    private void ReturnToWizard() => CurrentPage = _wizardPage;
}
