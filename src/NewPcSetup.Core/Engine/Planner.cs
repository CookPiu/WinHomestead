using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Infrastructure;
using NewPcSetup.Core.Models;

namespace NewPcSetup.Core.Engine;

public sealed class Planner
{
    private readonly ExecutionServices _services;

    public Planner(ExecutionServices services) { _services = services; }

    public Plan Build(IReadOnlyList<ITask> catalog, EnvironmentSnapshot snapshot, Answers answers)
    {
        var items = new List<PlanItem>();
        var scratch = new InMemoryJournal();
        foreach (var task in catalog.OrderBy(t => t.Metadata.Order))
        {
            var m = task.Metadata;
            bool applicable;
            try { applicable = task.IsApplicable(snapshot, answers); }
            catch (Exception ex) { _services.Logger.Warn($"{m.Id} IsApplicable 异常: {ex.Message}"); applicable = false; }
            if (!applicable) continue;

            var ctx = _services.CreateContext(m.Id, snapshot, answers, scratch, CancellationToken.None);
            DetectResult d;
            try { d = task.Detect(ctx); }
            catch (Exception ex)
            {
                _services.Logger.Warn($"{m.Id} Detect 异常: {ex.Message}");
                d = DetectResult.NotApplicableBecause("detect_failed: " + ex.Message);
            }

            var state = d.Reason != null && d.Reason.StartsWith(DetectResult.NotApplicable, StringComparison.Ordinal)
                ? PlanState.NotApplicable
                : d.Satisfied ? PlanState.Skipped : PlanState.Planned;
            var isChecked = state == PlanState.Planned && SafeDefaultChecked(task, snapshot, answers);
            items.Add(new PlanItem(m.Id, m.Module, m.DisplayName, m.Description, state, isChecked,
                d.CurrentValue, d.TargetValue, m.Risk, d.Reason, m.DependsOn));
        }

        var plan = new Plan(DateTime.Now.ToString("yyyyMMdd-HHmmss"), DateTime.Now, answers, items);
        return PlanEditor.Normalize(plan);
    }

    private bool SafeDefaultChecked(ITask task, EnvironmentSnapshot s, Answers a)
    {
        try { return task.DefaultChecked(s, a); }
        catch (Exception ex) { _services.Logger.Warn($"{task.Metadata.Id} DefaultChecked 异常: {ex.Message}"); return false; }
    }
}

public static class PlanEditor
{
    /// <summary>用户勾选/取消：取消时级联取消所有依赖它的项；勾选时级联勾选它依赖的 Planned 项。</summary>
    public static Plan SetChecked(Plan plan, string taskId, bool value)
    {
        var map = plan.Items.ToDictionary(i => i.TaskId, StringComparer.OrdinalIgnoreCase);
        if (!map.TryGetValue(taskId, out var item) || item.State != PlanState.Planned) return plan;

        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (value) CheckWithParents(map, taskId, changed);
        else UncheckWithChildren(map, taskId, changed);

        var items = plan.Items.Select(i => changed.Contains(i.TaskId) ? i with { Checked = value } : i).ToList();
        return plan with { Items = items };
    }

    /// <summary>保证初始状态一致：父项未勾选（且为 Planned）时子项不能勾选。</summary>
    public static Plan Normalize(Plan plan)
    {
        var map = plan.Items.ToDictionary(i => i.TaskId, StringComparer.OrdinalIgnoreCase);
        var items = new List<PlanItem>();
        foreach (var i in plan.Items)
        {
            var ok = i.Checked;
            if (ok)
                foreach (var dep in i.DependsOn)
                    if (map.TryGetValue(dep, out var p) && p.State == PlanState.Planned && !p.Checked) { ok = false; break; }
            items.Add(ok == i.Checked ? i : i with { Checked = ok });
        }
        return plan with { Items = items };
    }

    private static void CheckWithParents(Dictionary<string, PlanItem> map, string id, HashSet<string> acc)
    {
        if (!map.TryGetValue(id, out var item) || item.State != PlanState.Planned || !acc.Add(id)) return;
        foreach (var dep in item.DependsOn) CheckWithParents(map, dep, acc);
    }

    private static void UncheckWithChildren(Dictionary<string, PlanItem> map, string id, HashSet<string> acc)
    {
        if (!acc.Add(id)) return;
        foreach (var i in map.Values)
            if (i.DependsOn.Any(d => string.Equals(d, id, StringComparison.OrdinalIgnoreCase)))
                UncheckWithChildren(map, i.TaskId, acc);
    }
}
