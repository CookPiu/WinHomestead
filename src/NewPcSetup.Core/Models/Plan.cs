using System;
using System.Collections.Generic;

namespace NewPcSetup.Core.Models;

public enum PlanState { Skipped, Planned, NotApplicable }

[Flags]
public enum RiskFlags
{
    None = 0,
    Reversible = 1,
    NeedsExplorerRestart = 2,
    NeedsReboot = 4,
    AdminOnly = 8,
    PromptOnly = 16,
    NeedsSignOut = 32,
}

public sealed record PlanItem(
    string TaskId,
    string Module,
    string DisplayName,
    string Description,
    PlanState State,
    bool Checked,
    string? CurrentValue,
    string? TargetValue,
    RiskFlags Risk,
    string? Reason,
    IReadOnlyList<string> DependsOn,
    string? Detail = null);

public sealed record Plan(string Id, DateTime CreatedAt, Answers Answers, IReadOnlyList<PlanItem> Items)
{
    public int PlannedCount { get { var n = 0; foreach (var i in Items) if (i.State == PlanState.Planned && i.Checked) n++; return n; } }
    public int SkippedCount { get { var n = 0; foreach (var i in Items) if (i.State == PlanState.Skipped) n++; return n; } }
    public int RebootCount { get { var n = 0; foreach (var i in Items) if (i.State == PlanState.Planned && i.Checked && (i.Risk & RiskFlags.NeedsReboot) != 0) n++; return n; } }
}
