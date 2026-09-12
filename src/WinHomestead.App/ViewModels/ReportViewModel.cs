using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinHomestead.Core.Abstractions;
using WinHomestead.Core.Infrastructure;
using WinHomestead.Core.Models;

namespace WinHomestead.App.ViewModels;

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
        _result = global::WinHomestead.App.ManualSteps.CurrentResult(services);
        Title = ReferenceEquals(_result, services.Session.PreviousResult)
            ? L.S("上次执行的记录（重启后已复核）", "Previous session (re-verified after reboot)")
            : L.S("本次执行记录", "This session");

        var ok = _result.Count(TaskOutcome.Done) + _result.Count(TaskOutcome.NeedsReboot);
        var failed = _result.Count(TaskOutcome.Failed) + _result.Count(TaskOutcome.RolledBack);
        Summary = _result.Results.Count == 0
            ? L.S("还没有执行过任何项。", "Nothing has been run yet.")
            : L.S($"成功 {ok} 项 · 失败 {failed} 项", $"{ok} succeeded · {failed} failed");
        RebootRequired = _result.RebootRequired;
        foreach (var x in _result.Results) Results.Add(new ResultRow(x));
        foreach (var step in global::WinHomestead.App.ManualSteps.Collect(services, _result)) ManualSteps.Add(step);
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

    /// <summary>导出到数据盘 Data\开荒报告-&lt;时间&gt;.html；无数据盘时放到 %ProgramData%\WinHomestead。</summary>
    [RelayCommand]
    private void ExportHtml()
    {
        try
        {
            var html = ReportHtml.Render(_services.Session.Snapshot, _services.Session.Plan, _result, ManualSteps.ToList());
            var drive = _services.Session.Answers?.DataDrive ?? _services.Session.Snapshot?.DataDrive;
            var dir = drive != null && Directory.Exists(drive + "\\Data") ? drive + "\\Data" : _services.Store.BaseDir;
            var path = Path.Combine(dir, L.S($"开荒报告-{DateTime.Now:yyyyMMdd-HHmm}.html", $"WinHomestead-report-{DateTime.Now:yyyyMMdd-HHmm}.html"));
            File.WriteAllText(path, html, Encoding.UTF8);
            ExportStatus = L.S("已导出：", "Exported to ") + path;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _services.Logger.Error("导出报告失败", ex);
            ExportStatus = L.S("导出失败：", "Export failed: ") + ex.Message;
        }
    }

    [RelayCommand]
    private void RebootNow() => RebootHelper.RebootWithResume(_services);

    [RelayCommand]
    private void Back() => _back();
}
