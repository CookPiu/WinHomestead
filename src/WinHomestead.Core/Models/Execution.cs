using System;
using System.Collections.Generic;

namespace WinHomestead.Core.Models;

public enum JournalKind { Registry, EnvVar, KnownFolder, File, Power, Display }

/// <summary>
/// 一条可撤销的改动记录。Key 的格式随 Kind 而定：
/// Registry: root::subkey::valueName（Extra 为旧值类型名，缺失时为 "Missing"）；
/// EnvVar: scope::name；KnownFolder: 文件夹枚举名；
/// File: "dir_created"（NewValue 为路径）、"quick_access_pinned" / "quick_access_unpinned"（NewValue 为路径）；
/// Power: "hibernate"（OldValue/NewValue 为 "1"/"0"）、"ac_timeouts"（值为 "关屏分钟;睡眠分钟"）；
/// Display: "refresh_rate"（值为赫兹）。
/// </summary>
public sealed record JournalEntry(
    string TaskId,
    DateTime At,
    JournalKind Kind,
    string Key,
    string? OldValue,
    string? NewValue,
    string? Extra = null);

public enum TaskOutcome { Done, Skipped, Failed, RolledBack, NeedsReboot, Aborted }

public sealed record TaskResult(
    string TaskId,
    string DisplayName,
    TaskOutcome Outcome,
    string? Message,
    double ElapsedMs,
    IReadOnlyList<string> ManualSteps);

public sealed record ExecutionResult(
    string PlanId,
    DateTime StartedAt,
    DateTime? FinishedAt,
    bool Aborted,
    bool RebootRequired,
    IReadOnlyList<TaskResult> Results)
{
    public int Count(TaskOutcome o) { var n = 0; foreach (var r in Results) if (r.Outcome == o) n++; return n; }
}

public sealed record RunnerProgress(int Completed, int Total, string CurrentTaskId, string CurrentDisplayName, TaskResult? LastResult);

public sealed record AppState(string? LastPlanId, bool PendingResume);
