using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewPcSetup.Core.Infrastructure;
using NewPcSetup.Core.Models;

namespace NewPcSetup.App.ViewModels;

public sealed partial class ReportViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _showSoftware;

    public ReportViewModel(AppServices services, Action showSoftware)
    {
        _services = services; _showSoftware = showSoftware;
        var r = services.Session.Result;
        if (r == null) { Summary = "没有执行记录。"; return; }

        var ok = r.Count(TaskOutcome.Done) + r.Count(TaskOutcome.NeedsReboot);
        var failed = r.Count(TaskOutcome.Failed) + r.Count(TaskOutcome.RolledBack);
        Summary = $"成功 {ok} 项 · 失败 {failed} 项" + (r.Aborted ? " · 已中止" : string.Empty);
        RebootRequired = r.RebootRequired;
        SignOutSuggested = services.Session.Plan?.Items.Any(i => i.Checked && i.State == PlanState.Planned && (i.Risk & RiskFlags.NeedsSignOut) != 0) ?? false;
        foreach (var x in r.Results) Results.Add(new ResultRow(x));
        foreach (var step in r.Results.SelectMany(x => x.ManualSteps)) ManualSteps.Add(step);

        var s = services.Session.Snapshot;
        if (s != null)
        {
            if (s.OneDrive.DesktopProtected) ManualSteps.Add("桌面由 OneDrive 备份接管：如需迁移桌面，先在 OneDrive 设置 → 同步和备份 → 管理备份 中停止桌面备份，再重新运行本工具。");
            if (s.HasTool("docker")) ManualSteps.Add("Docker Desktop：在 Settings → Resources → Advanced 中把 Disk image location 改到数据盘 VMs\\docker。");
            if (s.HasTool("maven")) ManualSteps.Add("Maven：编辑 %USERPROFILE%\\.m2\\settings.xml，把 <localRepository> 指向数据盘 DevCache\\m2-repository。");
            if (s.HasTool("conda")) ManualSteps.Add("Conda：在 %USERPROFILE%\\.condarc 中设置 envs_dirs 与 pkgs_dirs 到数据盘 DevCache\\conda。");
            var phone = s.LargeItems.FirstOrDefault(i => i.Category == "phonelink" && i.SizeGb >= 1);
            if (phone != null) ManualSteps.Add($"手机连接缓存占用 {phone.SizeGb:F1} GB（{phone.Path}）：可在“手机连接”应用设置中清理或断开设备。");
        }
        HasManualSteps = ManualSteps.Count > 0;
    }

    public string Summary { get; }
    public bool RebootRequired { get; }
    public bool SignOutSuggested { get; }
    public bool HasManualSteps { get; }
    public ObservableCollection<ResultRow> Results { get; } = new();
    public ObservableCollection<string> ManualSteps { get; } = new();

    [RelayCommand]
    private void OpenLogs() => Process.Start(new ProcessStartInfo("explorer.exe", _services.Store.BaseDir) { UseShellExecute = true });

    [ObservableProperty] private string _exportStatus = string.Empty;

    /// <summary>导出到数据盘 Data\开荒报告-&lt;时间&gt;.html；无数据盘时放到 %ProgramData%\NewPcSetup。</summary>
    [RelayCommand]
    private void ExportHtml()
    {
        var r = _services.Session.Result;
        if (r == null) return;
        try
        {
            var html = ReportHtml.Render(_services.Session.Snapshot, _services.Session.Plan, r, ManualSteps.ToList());
            var drive = _services.Session.Answers?.DataDrive ?? _services.Session.Snapshot?.DataDrive;
            var dir = drive != null && Directory.Exists(drive + "\\Data") ? drive + "\\Data" : _services.Store.BaseDir;
            var path = Path.Combine(dir, $"开荒报告-{DateTime.Now:yyyyMMdd-HHmm}.html");
            File.WriteAllText(path, html, Encoding.UTF8);
            ExportStatus = "已导出：" + path;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _services.Logger.Error("导出报告失败", ex);
            ExportStatus = "导出失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void ShowSoftware() => _showSoftware();

    [RelayCommand]
    private void Finish() => Application.Current.Shutdown();
}
