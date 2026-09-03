using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Infrastructure;
using NewPcSetup.Core.Models;

namespace NewPcSetup.Core.Engine;

/// <summary>执行前后的收尾：还原点、journal 文件、状态持久化。</summary>
public sealed class ExecutionCoordinator
{
    private readonly ExecutionServices _services;
    private readonly ISystemRestore _restore;
    private readonly StateStore _store;

    public ExecutionCoordinator(ExecutionServices services, ISystemRestore restore, StateStore store)
    {
        _services = services; _restore = restore; _store = store;
    }

    public ExecutionResult Execute(Plan plan, IReadOnlyList<ITask> catalog, EnvironmentSnapshot snapshot,
        IProgress<RunnerProgress>? progress, IProgress<string>? status, CancellationToken ct)
    {
        var log = _services.Logger;
        status?.Report("正在创建系统还原点…");
        try
        {
            if (!_restore.CreateRestorePoint("NewPcSetup " + plan.Id))
                log.Warn("系统拒绝创建还原点（可能未开启系统保护），继续执行");
        }
        catch (Exception ex) { log.Warn("创建还原点异常: " + ex.Message); }

        _store.Save($"plan-{plan.Id}.json", plan);
        _store.Save($"snapshot-{plan.Id}.json", snapshot);
        _store.SaveState(new AppState(plan.Id, true));

        var journal = new FileJournal(_store.PathFor($"journal-{plan.Id}.jsonl"));
        status?.Report("正在执行…");
        var result = RunSaving(plan, catalog, snapshot, journal, Array.Empty<TaskResult>(), progress, ct);

        _store.SaveState(new AppState(plan.Id, false));
        status?.Report("完成");
        return result;
    }

    /// <summary>每完成一个任务就把部分结果落盘（FinishedAt 为 null），崩溃后可据此续跑。</summary>
    private ExecutionResult RunSaving(Plan plan, IReadOnlyList<ITask> catalog, EnvironmentSnapshot snapshot, IJournal journal,
        IReadOnlyList<TaskResult> previous, IProgress<RunnerProgress>? progress, CancellationToken ct)
    {
        var started = DateTime.Now;
        var acc = previous.ToList();
        var saving = new Progress<RunnerProgress>(p =>
        {
            if (p.LastResult != null)
            {
                acc.Add(p.LastResult);
                try { _store.Save($"result-{plan.Id}.json", new ExecutionResult(plan.Id, started, null, false, false, acc.ToList())); }
                catch (Exception ex) { _services.Logger.Warn("保存部分结果失败: " + ex.Message); }
            }
            progress?.Report(p);
        });
        var result = new TaskRunner(_services).Run(plan, catalog, snapshot, journal, saving, ct);
        var merged = result with { Results = previous.Concat(result.Results).ToList(), RebootRequired = result.RebootRequired || previous.Any(r => r.Outcome == TaskOutcome.NeedsReboot) };
        _store.Save($"result-{plan.Id}.json", merged);
        return merged;
    }

    /// <summary>读取上次会话；PendingResume 为 true 且结果未完成时表示需要续跑。</summary>
    public ResumeSession? LoadLast()
    {
        var state = _store.LoadState();
        if (state.LastPlanId == null) return null;
        var plan = _store.Load<Plan>($"plan-{state.LastPlanId}.json");
        var snapshot = _store.Load<EnvironmentSnapshot>($"snapshot-{state.LastPlanId}.json");
        if (plan == null || snapshot == null) return null;
        var result = _store.Load<ExecutionResult>($"result-{state.LastPlanId}.json");
        var unfinished = state.PendingResume && (result == null || result.FinishedAt == null);
        return new ResumeSession(plan, snapshot, result, unfinished);
    }

    /// <summary>崩溃续跑：跳过已有结果的任务，继续执行其余勾选项。</summary>
    public ExecutionResult Continue(ResumeSession session, IReadOnlyList<ITask> catalog, IProgress<RunnerProgress>? progress, IProgress<string>? status, CancellationToken ct)
    {
        var done = new HashSet<string>(session.Result?.Results.Select(r => r.TaskId) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var remaining = session.Plan with
        {
            Items = session.Plan.Items.Select(i => done.Contains(i.TaskId) ? i with { Checked = false } : i).ToList(),
        };
        _store.SaveState(new AppState(session.Plan.Id, true));
        var journal = new FileJournal(_store.PathFor($"journal-{session.Plan.Id}.jsonl"));
        status?.Report("继续上次未完成的执行…");
        var result = RunSaving(remaining, catalog, session.Snapshot, journal, session.Result?.Results ?? Array.Empty<TaskResult>(), progress, ct);
        _store.SaveState(new AppState(session.Plan.Id, false));
        status?.Report("完成");
        return result;
    }

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
                return task.Verify(ctx) ? r with { Outcome = TaskOutcome.Done, Message = "重启后已确认生效" } : r with { Outcome = TaskOutcome.Failed, Message = "重启后校验未通过" };
            }
            catch (Exception ex) { return r with { Outcome = TaskOutcome.Failed, Message = "重启后校验异常: " + ex.Message }; }
        }).ToList();
        var result = session.Result with { Results = results, RebootRequired = false };
        _store.Save($"result-{session.Plan.Id}.json", result);
        return result;
    }
}

public sealed record ResumeSession(Plan Plan, EnvironmentSnapshot Snapshot, ExecutionResult? Result, bool Unfinished);
