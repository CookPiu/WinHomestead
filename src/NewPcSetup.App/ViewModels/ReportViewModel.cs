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
            AddIfNotApplicable("env.maven", "maven", "Maven：编辑 %USERPROFILE%\\.m2\\settings.xml，把 <localRepository> 指向数据盘 DevCache\\m2-repository。");
            AddIfNotApplicable("env.conda", "conda", "Conda：在 %USERPROFILE%\\.condarc 中设置 envs_dirs 与 pkgs_dirs 到数据盘 DevCache\\conda。");
            AddIfNotApplicable("env.android", "android", "Android：把 SDK 与 AVD 目录搬到数据盘后，设置 ANDROID_HOME、ANDROID_USER_HOME、ANDROID_AVD_HOME。");
            var phone = s.LargeItems.FirstOrDefault(i => i.Category == "phonelink" && i.SizeGb >= 1);
            if (phone != null) ManualSteps.Add($"手机连接缓存占用 {phone.SizeGb:F1} GB（{phone.Path}）：可在“手机连接”应用设置中清理或断开设备。");
        }
        HasManualSteps = ManualSteps.Count > 0;

        // 对应任务能自动执行时不重复提示；任务判定为“不适用”时把它给出的原因作为手动步骤
        void AddIfNotApplicable(string taskId, string toolId, string fallback)
        {
            if (s == null || !s.HasTool(toolId)) return;
            var item = _services.Session.Plan?.Items.FirstOrDefault(i => string.Equals(i.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
            if (item == null) { ManualSteps.Add(fallback); return; }
            if (item.State != PlanState.NotApplicable) return;
            var reason = item.Reason;
            var text = reason != null && reason.StartsWith(DetectResult.NotApplicable, StringComparison.Ordinal)
                ? reason.Substring(DetectResult.NotApplicable.Length).TrimStart(':', ' ')
                : null;
            ManualSteps.Add(text == null ? fallback : $"{item.DisplayName}：{text}。");
        }
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
