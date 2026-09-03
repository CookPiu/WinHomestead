using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewPcSetup.Core.Models;

namespace NewPcSetup.App.ViewModels;

public sealed class ResultRow
{
    public ResultRow(TaskResult r)
    {
        Name = r.DisplayName;
        Outcome = r.Outcome switch
        {
            TaskOutcome.Done => "完成",
            TaskOutcome.NeedsReboot => "完成（需重启）",
            TaskOutcome.Skipped => "跳过",
            TaskOutcome.RolledBack => "失败，已回滚",
            TaskOutcome.Failed => "失败",
            TaskOutcome.Aborted => "已中止",
            _ => r.Outcome.ToString(),
        };
        IsOk = r.Outcome is TaskOutcome.Done or TaskOutcome.NeedsReboot or TaskOutcome.Skipped;
        Message = r.Message ?? string.Empty;
    }
    public string Name { get; }
    public string Outcome { get; }
    public bool IsOk { get; }
    public string Message { get; }
}

public sealed partial class ExecuteViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _next;
    private readonly CancellationTokenSource _cts = new();

    public ExecuteViewModel(AppServices services, Action next)
    {
        _services = services; _next = next;
        _ = RunAsync();
    }

    [ObservableProperty] private string _status = "准备执行…";
    [ObservableProperty] private string _currentTask = string.Empty;
    [ObservableProperty] private int _completed;
    [ObservableProperty] private int _total = 1;
    [ObservableProperty] private bool _isRunning = true;
    [ObservableProperty] private bool _isFinished;
    public ObservableCollection<ResultRow> Results { get; } = new();

    private async Task RunAsync()
    {
        var session = _services.Session;
        var progress = new Progress<RunnerProgress>(p =>
        {
            Total = Math.Max(1, p.Total);
            Completed = p.Completed;
            CurrentTask = p.Completed >= p.Total ? string.Empty : p.CurrentDisplayName;
            if (p.LastResult != null) Results.Add(new ResultRow(p.LastResult));
        });
        var status = new Progress<string>(s => Status = s);
        try
        {
            var result = await Task.Run(() => _services.Coordinator.Execute(session.Plan!, _services.Catalog, session.Snapshot!, progress, status, _cts.Token));
            session.Result = result;
            Status = result.Aborted ? "已中止" : "执行完成";
        }
        catch (Exception ex)
        {
            _services.Logger.Error("执行异常", ex);
            Status = "执行异常：" + ex.Message;
        }
        finally
        {
            IsRunning = false;
            IsFinished = true;
            StopCommand.NotifyCanExecuteChanged();
            ContinueCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Stop() { _cts.Cancel(); Status = "将在当前任务完成后停止…"; }

    [RelayCommand(CanExecute = nameof(IsFinished))]
    private void Continue() => _next();
}
