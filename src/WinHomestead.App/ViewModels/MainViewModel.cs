using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WinHomestead.App.ViewModels;

/// <summary>单窗口：主列表页常驻，执行记录 / 软件推荐 / 启动项 / 检查项 作为可返回的子页。</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly HomeViewModel _home;

    public MainViewModel(AppServices services, StartupMode mode = StartupMode.Normal)
    {
        _services = services;
        _home = new HomeViewModel(services);
        _home.RequestShowReport += ShowReport;
        _home.Refreshed += RefreshReportLabel;
        CurrentPage = _home;
        RefreshReportLabel();
        if (mode == StartupMode.Reverify) ShowReport();
    }

    [ObservableProperty] private object? _currentPage;
    /// <summary>“执行记录”按钮文字；有手动项时带数量，没进过记录页的用户也能看到有事要做。</summary>
    [ObservableProperty] private string _reportLabel = "执行记录";
    public bool IsHome => ReferenceEquals(CurrentPage, _home);

    partial void OnCurrentPageChanged(object? value) => OnPropertyChanged(nameof(IsHome));

    private void RefreshReportLabel()
    {
        var n = ManualSteps.Collect(_services, ManualSteps.CurrentResult(_services)).Count;
        ReportLabel = n == 0 ? "执行记录" : $"执行记录（{n} 项手动）";
    }

    [RelayCommand]
    private void ShowSoftware() => CurrentPage = new SoftwareViewModel(_services, Home);

    [RelayCommand]
    private void ShowCheckup() => CurrentPage = new CheckupViewModel(_services, Home);

    [RelayCommand]
    private void ShowStartup() => CurrentPage = new StartupViewModel(_services, Home);

    [RelayCommand]
    private void ShowReport() => CurrentPage = new ReportViewModel(_services, Home);

    [RelayCommand]
    private void Home()
    {
        CurrentPage = _home;
        RefreshReportLabel();
    }
}
