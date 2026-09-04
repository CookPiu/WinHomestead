using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewPcSetup.App.ViewModels;

/// <summary>单窗口：主列表页常驻，执行记录 / 软件推荐 / C 盘 / 检查项 作为可返回的子页。</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly HomeViewModel _home;

    public MainViewModel(AppServices services, StartupMode mode = StartupMode.Normal)
    {
        _services = services;
        _home = new HomeViewModel(services);
        _home.RequestShowReport += ShowReport;
        CurrentPage = _home;
        if (mode == StartupMode.Reverify) ShowReport();
    }

    [ObservableProperty] private object? _currentPage;
    public bool IsHome => ReferenceEquals(CurrentPage, _home);

    partial void OnCurrentPageChanged(object? value) => OnPropertyChanged(nameof(IsHome));

    [RelayCommand]
    private void ShowSoftware() => CurrentPage = new SoftwareViewModel(_services, Home);

    [RelayCommand]
    private void ShowStorage() => CurrentPage = new StorageViewModel(_services, Home);

    [RelayCommand]
    private void ShowCheckup() => CurrentPage = new CheckupViewModel(_services, Home);

    [RelayCommand]
    private void ShowReport() => CurrentPage = new ReportViewModel(_services, Home);

    [RelayCommand]
    private void Home() => CurrentPage = _home;
}
