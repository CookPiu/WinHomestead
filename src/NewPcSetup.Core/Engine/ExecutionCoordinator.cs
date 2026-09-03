using System;
using System.Collections.Generic;
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
        var result = new TaskRunner(_services).Run(plan, catalog, snapshot, journal, progress, ct);

        _store.Save($"result-{plan.Id}.json", result);
        _store.SaveState(new AppState(plan.Id, false));
        status?.Report("完成");
        return result;
    }
}
