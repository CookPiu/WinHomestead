using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WinHomestead.Core.Abstractions;
using WinHomestead.Core.Infrastructure;
using WinHomestead.Core.Models;

namespace WinHomestead.Core.Engine;

/// <summary>
/// 逐项执行：用户点哪一项就执行哪一项。整个应用会话共用一个 journal 与一份结果文件；
/// 还原点在第一次写操作前创建一次；Explorer 重启与系统重启只记标志，由界面统一提示。
/// </summary>
public sealed class SessionRunner
{
    private readonly ExecutionServices _services;
    private readonly ISystemRestore _restore;
    private readonly StateStore _store;
    private readonly List<TaskResult> _results = new();
    private readonly object _gate = new();
    private FileJournal? _journal;
    private bool _restorePointTried;

    public SessionRunner(ExecutionServices services, ISystemRestore restore, StateStore store)
    {
        _services = services; _restore = restore; _store = store;
        SessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        StartedAt = DateTime.Now;
    }

    public string SessionId { get; }
    public DateTime StartedAt { get; }
    public IReadOnlyList<TaskResult> Results { get { lock (_gate) return _results.ToList(); } }
    public bool ExplorerRestartPending { get; private set; }
    public bool RebootPending { get; private set; }
    public bool Busy { get; private set; }

    /// <summary>把当前列表与快照落盘，供重启后复核（--resume）读取。</summary>
    public void SavePlan(Plan plan, EnvironmentSnapshot snapshot)
    {
        _store.Save($"plan-{SessionId}.json", plan with { Id = SessionId });
        _store.Save($"snapshot-{SessionId}.json", snapshot);
        _store.SaveState(new AppState(SessionId, false));
    }

    /// <summary>
    /// 给子页用的带 journal 上下文：启动项这类"一次改一条、没有 Detect/Verify 循环"的操作不走 TaskRunner，
    /// 但写进去的原值要和主列表进同一份 journal，执行记录和回滚才看得到它们。
    /// </summary>
    public TaskContext CreateJournaledContext(string taskId, EnvironmentSnapshot snapshot, Answers answers)
    {
        lock (_gate) _journal ??= new FileJournal(_store.PathFor($"journal-{SessionId}.jsonl"));
        return _services.CreateContext(taskId, snapshot, answers, _journal, CancellationToken.None);
    }

    /// <summary>只在会话第一次执行前尝试一次；失败仅告警。</summary>
    public void EnsureRestorePoint(IProgress<string>? status)
    {
        if (_restorePointTried) return;
        _restorePointTried = true;
        status?.Report("正在创建系统还原点…");
        try
        {
            if (!_restore.CreateRestorePoint("WinHomestead " + SessionId))
                _services.Logger.Warn("系统拒绝创建还原点（可能未开启系统保护），继续执行");
        }
        catch (Exception ex) { _services.Logger.Warn("创建还原点异常: " + ex.Message); }
    }

    /// <summary>
    /// 执行一项。它依赖的、在列表中仍为“可执行”的父项会先按拓扑顺序执行；已满足的父项不重复执行。
    /// 返回本次实际执行的全部结果（含父项）。
    /// </summary>
    public IReadOnlyList<TaskResult> Run(string taskId, Plan plan, IReadOnlyList<ITask> catalog, EnvironmentSnapshot snapshot,
        IProgress<RunnerProgress>? progress, IProgress<string>? status, CancellationToken ct)
    {
        lock (_gate)
        {
            if (Busy) throw new InvalidOperationException("已有任务在执行");
            Busy = true;
        }
        try
        {
            EnsureRestorePoint(status);
            var ids = ChainFor(taskId, plan);
            var subPlan = plan with
            {
                Id = SessionId,
                Items = plan.Items.Select(i => i with { Checked = ids.Contains(i.TaskId) }).ToList(),
            };
            _journal ??= new FileJournal(_store.PathFor($"journal-{SessionId}.jsonl"));
            _store.SaveState(new AppState(SessionId, true));
            status?.Report("正在执行…");
            var result = new TaskRunner(_services).Run(subPlan, catalog, snapshot, _journal, progress, ct, restartExplorer: false);

            var byId = catalog.ToDictionary(t => t.Metadata.Id, StringComparer.OrdinalIgnoreCase);
            lock (_gate)
            {
                foreach (var r in result.Results)
                {
                    _results.RemoveAll(x => string.Equals(x.TaskId, r.TaskId, StringComparison.OrdinalIgnoreCase));
                    _results.Add(r);
                    if (r.Outcome is TaskOutcome.Done or TaskOutcome.NeedsReboot && byId.TryGetValue(r.TaskId, out var t)
                        && (t.Metadata.Risk & RiskFlags.NeedsExplorerRestart) != 0)
                        ExplorerRestartPending = true;
                    if (r.Outcome == TaskOutcome.NeedsReboot) RebootPending = true;
                }
                _store.Save($"result-{SessionId}.json", new ExecutionResult(SessionId, StartedAt, DateTime.Now, false, RebootPending, _results.ToList()));
            }
            _store.SaveState(new AppState(SessionId, false));
            status?.Report(string.Empty);
            return result.Results;
        }
        finally { lock (_gate) Busy = false; }
    }

    /// <summary>界面捕获到的执行异常也记进本次会话，避免执行记录漏掉这一项。</summary>
    public void RecordFailure(TaskResult result)
    {
        lock (_gate)
        {
            _results.RemoveAll(x => string.Equals(x.TaskId, result.TaskId, StringComparison.OrdinalIgnoreCase));
            _results.Add(result);
            _store.Save($"result-{SessionId}.json", new ExecutionResult(SessionId, StartedAt, DateTime.Now, false, RebootPending, _results.ToList()));
        }
    }

    public void RestartExplorer()
    {
        _services.Shell.RestartExplorer();
        ExplorerRestartPending = false;
    }

    /// <summary>目标项及其所有未满足（Planned）的祖先依赖。</summary>
    internal static HashSet<string> ChainFor(string taskId, Plan plan)
    {
        var map = plan.Items.ToDictionary(i => i.TaskId, StringComparer.OrdinalIgnoreCase);
        var acc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string id)
        {
            if (!map.TryGetValue(id, out var item) || item.State != PlanState.Planned || !acc.Add(id)) return;
            foreach (var d in item.DependsOn) Visit(d);
        }
        Visit(taskId);
        return acc;
    }
}
