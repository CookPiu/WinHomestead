using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewPcSetup.Native;

namespace NewPcSetup.App.ViewModels;

/// <summary>《01》4.4 的检查项：只读展示 + 跳转设置，工具不代改。</summary>
public sealed partial class CheckRowViewModel : ObservableObject
{
    public CheckRowViewModel(SystemCheck check)
    {
        Title = check.Title;
        Status = check.Status;
        Detail = check.Detail;
        Advice = check.Advice;
        IsWarning = check.Severity == Severity.Warning;
        SettingsUri = check.SettingsUri;
        SettingsLabel = check.SettingsLabel ?? string.Empty;
        HasSettings = SettingsUri != null;
    }

    public string Title { get; }
    public string Status { get; }
    public string Detail { get; }
    public string Advice { get; }
    public bool IsWarning { get; }
    public string? SettingsUri { get; }
    public string SettingsLabel { get; }
    public bool HasSettings { get; }

    [RelayCommand]
    private void OpenSettings()
    {
        if (SettingsUri == null) return;
        try { Process.Start(new ProcessStartInfo(SettingsUri) { UseShellExecute = true }); } catch { }
    }
}

/// <summary>检查页：BitLocker、杀软、Windows 更新、OEM 预装、默认应用、区域与时区。全部只读，不写任何设置。</summary>
public sealed partial class CheckupViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _back;

    public CheckupViewModel(AppServices services, Action back)
    {
        _services = services; _back = back;
        _ = LoadAsync();
    }

    public ObservableCollection<CheckRowViewModel> Rows { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "正在检查…";

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true; Status = "正在检查…";
        try
        {
            var manufacturer = _services.Session.Snapshot?.Manufacturer ?? string.Empty;
            var checks = await Task.Run(() => _services.Checks.Run(manufacturer));
            Rows.Clear();
            foreach (var c in checks) Rows.Add(new CheckRowViewModel(c));
            Status = "这些项工具只做检查与跳转，不代为改动。";
        }
        catch (Exception ex)
        {
            _services.Logger.Error("检查项失败", ex);
            Status = "检查失败：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void Back() => _back();
}
