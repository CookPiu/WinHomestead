using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WinHomestead.Core.Abstractions;
using WinHomestead.Core.Infrastructure;
using WinHomestead.Core.Models;

namespace WinHomestead.Core.Engine;

/// <summary>跨会话的收尾：读取上次记录、重启后复核“需重启”项。逐项执行本身见 SessionRunner。</summary>
public sealed class ExecutionCoordinator
{
    private readonly ExecutionServices _services;
    private readonly StateStore _store;

    public ExecutionCoordinator(ExecutionServices services, StateStore store)
    {
        _services = services; _store = store;
    }

    /// <summary>读取上次会话；PendingResume 为 true 表示上次有任务执行到一半时进程退出。</summary>
    public ResumeSession? LoadLast()
    {
        var state = _store.LoadState();
        if (state.LastPlanId == null) return null;
        var plan = _store.Load<Plan>($"plan-{state.LastPlanId}.json");
        var snapshot = _store.Load<EnvironmentSnapshot>($"snapshot-{state.LastPlanId}.json");
        if (plan == null || snapshot == null) return null;
        var result = _store.Load<ExecutionResult>($"result-{state.LastPlanId}.json");
        return new ResumeSession(plan, snapshot, result, state.PendingResume);
    }

    public void ClearPending(string planId) => _store.SaveState(new AppState(planId, false));

    /// <summary>重启后复核：对标记“需重启”的任务重新 Verify，通过的改为完成。</summary>
    public ExecutionResult Reverify(ResumeSession session, IReadOnlyList<ITask> catalog)
    {
        if (session.Result == null) throw new InvalidOperationException("没有可复核的结果");
        var byId = catalog.ToDictionary(t => t.Metadata.Id, StringComparer.OrdinalIgnoreCase);
        var scratch = new InMemoryJournal();
        var results = session.Result.Results.Select(r =>
        {
            if (r.Outcome != TaskOutcome.NeedsReboot || !byId.TryGetValue(r.TaskId, out var task)) return r;
            try
            {
                var ctx = _services.CreateContext(r.TaskId, session.Snapshot, session.Plan.Answers, scratch, CancellationToken.None);
                return task.Verify(ctx)
                    ? r with { Outcome = TaskOutcome.Done, Message = L.S("重启后已确认生效", "confirmed after reboot") }
                    : r with { Outcome = TaskOutcome.Failed, Message = L.S("重启后校验未通过", "verification failed after reboot") };
            }
            catch (Exception ex) { return r with { Outcome = TaskOutcome.Failed,
                Message = L.S("重启后校验异常: ", "verification threw after reboot: ") + ex.Message }; }
        }).ToList();
        var result = session.Result with { Results = results, RebootRequired = false };
        _store.Save($"result-{session.Plan.Id}.json", result);
        return result;
    }
}

/// <summary>Unfinished：上次进程在执行某项时退出（改动已按 journal 回滚与否未知，需提示用户检查记录）。</summary>
public sealed record ResumeSession(Plan Plan, EnvironmentSnapshot Snapshot, ExecutionResult? Result, bool Unfinished);
