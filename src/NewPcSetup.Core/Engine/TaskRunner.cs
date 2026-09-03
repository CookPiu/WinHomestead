using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Models;

namespace NewPcSetup.Core.Engine;

public sealed class TaskRunner
{
    private readonly ExecutionServices _services;

    public TaskRunner(ExecutionServices services) { _services = services; }

    public ExecutionResult Run(Plan plan, IReadOnlyList<ITask> catalog, EnvironmentSnapshot snapshot, IJournal journal,
        IProgress<RunnerProgress>? progress, CancellationToken ct)
    {
        var log = _services.Logger;
        var started = DateTime.Now;
        var byId = catalog.ToDictionary(t => t.Metadata.Id, StringComparer.OrdinalIgnoreCase);
        var selected = plan.Items
            .Where(i => i.Checked && i.State == PlanState.Planned && byId.ContainsKey(i.TaskId))
            .Select(i => byId[i.TaskId])
            .ToList();
        var ordered = TopologicalSort(selected);

        var results = new List<TaskResult>();
        bool aborted = false, explorer = false, reboot = false;
        var done = 0;

        foreach (var task in ordered)
        {
            var m = task.Metadata;
            if (ct.IsCancellationRequested) { aborted = true; break; }
            progress?.Report(new RunnerProgress(done, ordered.Count, m.Id, m.DisplayName, null));

            var sw = Stopwatch.StartNew();
            var ctx = _services.CreateContext(m.Id, snapshot, plan.Answers, journal, ct);
            TaskResult result;
            try
            {
                log.Info($"[{m.Id}] Apply");
                task.Apply(ctx);
                if (task.Verify(ctx))
                {
                    var outcome = (m.Risk & RiskFlags.NeedsReboot) != 0 ? TaskOutcome.NeedsReboot : TaskOutcome.Done;
                    if (outcome == TaskOutcome.NeedsReboot) reboot = true;
                    if ((m.Risk & RiskFlags.NeedsExplorerRestart) != 0) explorer = true;
                    result = new TaskResult(m.Id, m.DisplayName, outcome, null, sw.Elapsed.TotalMilliseconds, ctx.ManualSteps.ToList());
                    log.Info($"[{m.Id}] {outcome}");
                }
                else
                {
                    result = RollbackAfter(task, ctx, journal, "校验未通过", sw);
                }
            }
            catch (OperationCanceledException)
            {
                aborted = true;
                result = RollbackAfter(task, ctx, journal, "用户中止", sw, TaskOutcome.Aborted);
            }
            catch (Exception ex)
            {
                log.Error($"[{m.Id}] Apply 失败", ex);
                result = RollbackAfter(task, ctx, journal, ex.Message, sw);
            }

            results.Add(result);
            done++;
            progress?.Report(new RunnerProgress(done, ordered.Count, m.Id, m.DisplayName, result));
            if (aborted) break;
        }

        if (explorer)
        {
            try { log.Info("重启 Explorer"); _services.Shell.RestartExplorer(); }
            catch (Exception ex) { log.Warn("重启 Explorer 失败: " + ex.Message); }
        }

        return new ExecutionResult(plan.Id, started, DateTime.Now, aborted, reboot, results);
    }

    private TaskResult RollbackAfter(ITask task, TaskContext ctx, IJournal journal, string reason, Stopwatch sw, TaskOutcome? forced = null)
    {
        var m = task.Metadata;
        var entries = journal.EntriesFor(m.Id);
        try
        {
            task.Rollback(ctx, entries);
            _services.Logger.Warn($"[{m.Id}] 已回滚: {reason}");
            return new TaskResult(m.Id, m.DisplayName, forced ?? TaskOutcome.RolledBack, reason, sw.Elapsed.TotalMilliseconds, ctx.ManualSteps.ToList());
        }
        catch (Exception rex)
        {
            _services.Logger.Error($"[{m.Id}] 回滚失败", rex);
            return new TaskResult(m.Id, m.DisplayName, TaskOutcome.Failed, $"{reason}；回滚失败: {rex.Message}", sw.Elapsed.TotalMilliseconds, ctx.ManualSteps.ToList());
        }
    }

    /// <summary>按 Order 稳定排序后做拓扑排序；只考虑本次选中的任务之间的依赖。</summary>
    internal static List<ITask> TopologicalSort(List<ITask> selected)
    {
        var ids = new HashSet<string>(selected.Select(t => t.Metadata.Id), StringComparer.OrdinalIgnoreCase);
        var byId = selected.ToDictionary(t => t.Metadata.Id, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = new List<ITask>();

        void Visit(ITask t)
        {
            var id = t.Metadata.Id;
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidOperationException("任务依赖成环: " + id);
            foreach (var dep in t.Metadata.DependsOn)
                if (ids.Contains(dep)) Visit(byId[dep]);
            visiting.Remove(id);
            visited.Add(id);
            output.Add(t);
        }

        foreach (var t in selected.OrderBy(t => t.Metadata.Order)) Visit(t);
        return output;
    }
}
