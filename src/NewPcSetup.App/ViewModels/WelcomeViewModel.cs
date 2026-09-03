using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewPcSetup.Core.Models;

namespace NewPcSetup.App.ViewModels;

public sealed class InfoRow
{
    public InfoRow(string label, string value) { Label = label; Value = value; }
    public string Label { get; }
    public string Value { get; }
}

public sealed partial class WelcomeViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _next;

    public WelcomeViewModel(AppServices services, Action next)
    {
        _services = services; _next = next;
        if (services.Session.Snapshot != null) Populate(services.Session.Snapshot);
        else _ = DetectAsync();
    }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "正在探测这台电脑…";
    [ObservableProperty] private bool _canContinue;
    public ObservableCollection<InfoRow> Rows { get; } = new();
    public ObservableCollection<string> Warnings { get; } = new();

    private async Task DetectAsync()
    {
        IsBusy = true;
        try
        {
            var snapshot = await Task.Run(() => _services.Collector.Collect());
            _services.Session.Snapshot = snapshot;
            _services.Store.Save("snapshot-latest.json", snapshot);
            Populate(snapshot);
        }
        catch (Exception ex)
        {
            _services.Logger.Error("探测失败", ex);
            Status = "探测失败：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    private void Populate(EnvironmentSnapshot s)
    {
        Rows.Clear();
        Rows.Add(new InfoRow("系统", $"{s.OsCaption}（内部版本 {s.Build}）"));
        Rows.Add(new InfoRow("机型", $"{s.Manufacturer} {s.Model} · {(s.IsLaptop ? "笔记本" : "台式机")} · {s.RamGb} GB 内存"));
        Rows.Add(new InfoRow("磁盘", string.Join("；", s.Disks.Select(d => $"{d.Model} {d.SizeBytes / 1073741824d:F0} GB"))));
        Rows.Add(new InfoRow("分区", string.Join("；", s.Volumes.Select(v => $"{v.DriveLetter} {v.Label} {v.SizeGb:F0} GB（剩 {v.FreeGb:F0} GB）"))));
        Rows.Add(new InfoRow("数据盘", s.DataDrive ?? "未检测到（单盘无数据分区）"));
        Rows.Add(new InfoRow("开发工具", string.Join("、", s.Tools.Where(t => t.Installed).Select(t => t.Name)) is { Length: > 0 } t ? t : "未检测到"));
        Rows.Add(new InfoRow("OneDrive", s.OneDrive.SignedIn ? (s.OneDrive.DesktopProtected ? "已登录，桌面由 OneDrive 备份接管" : "已登录") : "未登录"));
        Rows.Add(new InfoRow("安装日期", s.InstallDate.ToString("yyyy-MM-dd") + (s.IsFreshInstall ? "（全新机）" : "")));

        Warnings.Clear();
        if (!s.IsWindows11) Warnings.Add("当前不是 Windows 11，部分设置项可能不适用。");
        if (s.IsMdmEnrolled || s.IsDomainJoined) Warnings.Add("检测到此电脑受组织管理（MDM/域），涉及系统级策略的改动将只提示不执行。");
        if (s.DataDrive == null) Warnings.Add("未检测到数据盘，路径迁移类任务将不会出现在方案中。分盘建议功能在后续版本提供。");
        if (s.ProxyEnabled) Warnings.Add("系统代理已开启，打开官网链接时将沿用该代理。");
        var sys = s.Volumes.FirstOrDefault(v => v.IsSystem);
        if (sys != null && sys.FreeGb < 40) Warnings.Add($"系统盘剩余空间仅 {sys.FreeGb:F0} GB，建议执行路径迁移并查看“C 盘”页。");
        var state = _services.Store.LoadState();
        if (state.PendingResume) Warnings.Add($"上次执行（方案 {state.LastPlanId}）未正常结束。本次会重新探测，未完成的项会再次出现在方案中；改动记录在 {_services.Store.BaseDir}。");

        Status = "探测完成";
        CanContinue = true;
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Next() => _next();

    partial void OnCanContinueChanged(bool value) => NextCommand.NotifyCanExecuteChanged();
}
