using System;
using System.Collections.Generic;
using NewPcSetup.Core.Engine;
using NewPcSetup.Core.Models;

namespace NewPcSetup.Core.Abstractions;

public sealed record TaskMetadata(
    string Id,
    string Module,
    string DisplayName,
    string Description,
    RiskFlags Risk,
    IReadOnlyList<string> DependsOn,
    int Order);

/// <summary>Reason 为 "not_applicable" 时 Planner 将该项标为 NotApplicable。</summary>
public sealed record DetectResult(bool Satisfied, string? CurrentValue, string? TargetValue, string? Reason = null)
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
