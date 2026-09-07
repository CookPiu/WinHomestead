using System;
using System.Collections.Generic;
using WinHomestead.Core.Engine;
using WinHomestead.Core.Models;

namespace WinHomestead.Core.Abstractions;

public sealed record TaskMetadata(
    string Id,
    string Module,
    string DisplayName,
    string Description,
    RiskFlags Risk,
    IReadOnlyList<string> DependsOn,
    int Order);

/// <summary>
/// Reason 为 "not_applicable" 时 Planner 将该项标为 NotApplicable。
/// CurrentValue / TargetValue 是给人看的文字；Detail 放原始键值（如 "SearchboxTaskbarMode=3 → 1"），界面只在悬停时展示。
/// </summary>
public sealed record DetectResult(bool Satisfied, string? CurrentValue, string? TargetValue, string? Reason = null, string? Detail = null)
{
    public const string NotApplicable = "not_applicable";
    public static DetectResult NotApplicableBecause(string why) => new(false, null, null, NotApplicable + ": " + why);
}

public interface ITask
{
    TaskMetadata Metadata { get; }
    bool IsApplicable(EnvironmentSnapshot snapshot, Answers answers);
    bool DefaultChecked(EnvironmentSnapshot snapshot, Answers answers);
    DetectResult Detect(TaskContext ctx);
    void Apply(TaskContext ctx);
    bool Verify(TaskContext ctx);
    void Rollback(TaskContext ctx, IReadOnlyList<JournalEntry> entries);
}

public sealed class TaskFailedException : Exception
{
    public TaskFailedException(string message, Exception? inner = null) : base(message, inner) { }
}
