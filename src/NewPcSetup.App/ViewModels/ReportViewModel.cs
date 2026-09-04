using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Infrastructure;
using NewPcSetup.Core.Models;

namespace NewPcSetup.App.ViewModels;

public sealed class ResultRow
{
    public ResultRow(TaskResult r)
    {
        Name = r.DisplayName;
        Outcome = ReportHtml.OutcomeLabel(r.Outcome);
        IsOk = r.Outcome is TaskOutcome.Done or TaskOutcome.NeedsReboot or TaskOutcome.Skipped;
        Message = r.Message ?? string.Empty;
    }
    public string Name { get; }
    public string Outcome { get; }
    public bool IsOk { get; }
    public string Message { get; }
}

/// <summary>执行记录：本次会话逐项执行的结果（重启后拉起时先显示上次的复核结果）+ 手动项 + 导出。</summary>
public sealed partial class ReportViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _back;
    private readonly ExecutionResult _result;

    public ReportViewModel(AppServices services, Action back)
    {
        _services = services; _back = back;
        var runner = services.Runner;
        var previous = services.Session.PreviousResult;
        _result = runner.Results.Count == 0 && previous != null
            ? previous
            : new ExecutionResult(runner.SessionId, runner.StartedAt, DateTime.Now, false, runner.RebootPending, runner.Results);
        Title = ReferenceEquals(_result, previous) ? "上次执行的记录（重启后已复核）" : "本次执行记录";

        var ok = _result.Count(TaskOutcome.Done) + _result.Count(TaskOutcome.NeedsReboot);
        var failed = _result.Count(TaskOutcome.Failed) + _result.Count(TaskOutcome.RolledBack);
        Summary = _result.Results.Count == 0 ? "还没有执行过任何项。" : $"成功 {ok} 项 · 失败 {failed} 项";
        RebootRequired = _result.RebootRequired;
        foreach (var x in _result.Results) Results.Add(new ResultRow(x));
        foreach (var step in _result.Results.SelectMany(x => x.ManualSteps)) ManualSteps.Add(step);

        var s = services.Session.Snapshot;
        if (s != null)
        {
            if (s.OneDrive.DesktopProtected) ManualSteps.Add("桌面由 OneDrive 备份接管：如需迁移桌面，先在 OneDrive 设置 → 同步和备份 → 管理备份 中停止桌面备份，再刷新列表。");
            if (s.HasTool("docker")) ManualSteps.Add("Docker Desktop：在 Settings → Resources → Advanced 中把 Disk image location 改到数据盘 VMs\\docker。");
            // 只对已经装了的工具提；开荒工具不代为迁移使用中的工具，只把步骤列出来
            if (s.HasTool("maven")) ManualSteps.Add(@"Maven 已安装：本机仓库位置只能改 %USERPROFILE%\.m2\settings.xml 的 <localRepository>，工具不代改。想搬到数据盘就把它指向 DevCache\m2-repository，已下载的 jar 会重新拉。");
            if (s.HasTool("android")) ManualSteps.Add("Android SDK 已安装：SDK 与 AVD 镜像动辄几十 GB，改 ANDROID_HOME 不会把它们搬过去。要迁移就在 Android Studio 的 SDK Manager 里改位置并手动移动目录。");
            var phone = s.LargeItems.FirstOrDefault(i => i.Category == "phonelink" && i.SizeGb >= 1);
            if (phone != null) ManualSteps.Add($"手机连接缓存占用 {phone.SizeGb:F1} GB（{phone.Path}）：可在“手机连接”应用设置中清理或断开设备。");
        }
        HasManualSteps = ManualSteps.Count > 0;
    }

    public string Title { get; }
    public string Summary { get; }
    public bool RebootRequired { get; }
    public bool HasManualSteps { get; }
    public ObservableCollection<ResultRow> Results { get; } = new();
    public ObservableCollection<string> ManualSteps { get; } = new();

    [ObservableProperty] private string _exportStatus = string.Empty;

    [RelayCommand]
    private void OpenLogs() => Process.Start(new ProcessStartInfo("explorer.exe", _services.Store.BaseDir) { UseShellExecute = true });

    /// <summary>导出到数据盘 Data\开荒报告-&lt;时间&gt;.html；无数据盘时放到 %ProgramData%\NewPcSetup。</summary>
    [RelayCommand]
    private void ExportHtml()
    {
        try
        {
            var html = ReportHtml.Render(_services.Session.Snapshot, _services.Session.Plan, _result, ManualSteps.ToList());
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
    private void RebootNow() => RebootHelper.RebootWithResume(_services);

    [RelayCommand]
    private void Back() => _back();
}
