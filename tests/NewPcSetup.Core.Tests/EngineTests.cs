using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Engine;
using NewPcSetup.Core.Infrastructure;
using NewPcSetup.Core.Models;
using NewPcSetup.Tasks;
using Xunit;

namespace NewPcSetup.Core.Tests;

public class PlannerTests
{
    private static RegistryValueTask Dword(string id, string key, string name, int target, Func<EnvironmentSnapshot, Answers, bool>? applicable = null, string[]? deps = null)
        => new(new TaskMetadata(id, "ui", id, id, RiskFlags.Reversible, deps ?? Array.Empty<string>(), 1), new[] { RegistryEntry.Dword(key, name, target) }, applicable);

    [Fact]
    public void SatisfiedTaskIsSkipped_UnsatisfiedIsPlanned_NotApplicableExcluded()
    {
        var (svc, reg, _, _, _) = TestData.Services();
        reg.Seed(RegRoot.CurrentUser, "K", "A", 0, RegKind.DWord);
        var s = TestData.Snapshot();
        var a = TestData.Answers(s);
        var tasks = new ITask[]
        {
            Dword("t.a", "K", "A", 0),
            Dword("t.b", "K", "B", 1),
            Dword("t.c", "K", "C", 1, (_, _) => false),
        };
        var plan = new Planner(svc).Build(tasks, s, a);

        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(PlanState.Skipped, plan.Items.Single(i => i.TaskId == "t.a").State);
        Assert.False(plan.Items.Single(i => i.TaskId == "t.a").Checked);
        Assert.Equal(PlanState.Planned, plan.Items.Single(i => i.TaskId == "t.b").State);
        Assert.True(plan.Items.Single(i => i.TaskId == "t.b").Checked);
    }

    [Fact]
    public void UncheckParentCascadesToChildren_CheckChildCascadesToParent()
    {
        var (svc, _, _, _, _) = TestData.Services();
        var s = TestData.Snapshot();
        var a = TestData.Answers(s);
        var tasks = new ITask[]
        {
            Dword("p", "K", "P", 1),
            Dword("c1", "K", "C1", 1, deps: new[] { "p" }),
            Dword("c2", "K", "C2", 1, deps: new[] { "c1" }),
        };
        var plan = new Planner(svc).Build(tasks, s, a);

        plan = PlanEditor.SetChecked(plan, "p", false);
        Assert.All(plan.Items, i => Assert.False(i.Checked));

        plan = PlanEditor.SetChecked(plan, "c2", true);
        Assert.All(plan.Items, i => Assert.True(i.Checked));
    }
}

public class RunnerTests
{
    [Fact]
    public void ApplyVerifyRollback_RestoresOldValueAndDeletesMissing()
    {
        var (svc, reg, _, _, _) = TestData.Services();
        reg.Seed(RegRoot.CurrentUser, "K", "Existing", 5, RegKind.DWord);
        var s = TestData.Snapshot();
        var a = TestData.Answers(s);
        var task = new RegistryValueTask(new TaskMetadata("t", "ui", "t", "t", RiskFlags.Reversible, Array.Empty<string>(), 1),
            new[] { RegistryEntry.Dword("K", "Existing", 1), RegistryEntry.Str("K", "Missing", "x") });
        var journal = new InMemoryJournal();
        var ctx = svc.CreateContext("t", s, a, journal, CancellationToken.None);

        task.Apply(ctx);
        Assert.True(task.Verify(ctx));
        Assert.Equal(1, reg.GetValue(RegRoot.CurrentUser, "K", "Existing").Value);
        Assert.Equal("x", reg.GetValue(RegRoot.CurrentUser, "K", "Missing").Value);

        task.Rollback(ctx, journal.EntriesFor("t"));
        Assert.Equal(5, reg.GetValue(RegRoot.CurrentUser, "K", "Existing").Value);
        Assert.Null(reg.GetValue(RegRoot.CurrentUser, "K", "Missing").Value);
    }

    private sealed class FailingVerifyTask : TaskBase
    {
        public override TaskMetadata Metadata { get; } = new("fail", "ui", "fail", "fail", RiskFlags.Reversible | RiskFlags.NeedsExplorerRestart, Array.Empty<string>(), 1);
        public override DetectResult Detect(TaskContext ctx) => new(false, "a", "b");
        public override void Apply(TaskContext ctx) => ctx.Registry.SetValue(RegRoot.CurrentUser, "K", "F", 1, RegKind.DWord);
        public override bool Verify(TaskContext ctx) => false;
    }

    [Fact]
    public void FailedVerifyIsRolledBack_ExplorerNotRestarted_OthersContinue()
    {
        var (svc, reg, _, shell, _) = TestData.Services();
        var s = TestData.Snapshot();
        var a = TestData.Answers(s);
        var ok = new RegistryValueTask(new TaskMetadata("ok", "ui", "ok", "ok", RiskFlags.Reversible | RiskFlags.NeedsExplorerRestart, Array.Empty<string>(), 2),
            new[] { RegistryEntry.Dword("K", "OK", 1) });
        var tasks = new ITask[] { new FailingVerifyTask(), ok };
        var plan = new Planner(svc).Build(tasks, s, a);

        var result = new TaskRunner(svc).Run(plan, tasks, s, new InMemoryJournal(), null, CancellationToken.None);

        Assert.Equal(TaskOutcome.RolledBack, result.Results.Single(r => r.TaskId == "fail").Outcome);
        Assert.Equal(TaskOutcome.Done, result.Results.Single(r => r.TaskId == "ok").Outcome);
        Assert.Null(reg.GetValue(RegRoot.CurrentUser, "K", "F").Value);
        Assert.Equal(1, reg.GetValue(RegRoot.CurrentUser, "K", "OK").Value);
        Assert.Equal(1, shell.ExplorerRestarts);
        Assert.False(result.Aborted);
    }

    [Fact]
    public void DependenciesRunBeforeDependents_RegardlessOfOrder()
    {
        var child = new RegistryValueTask(new TaskMetadata("child", "ui", "c", "c", RiskFlags.None, new[] { "parent" }, 1), new[] { RegistryEntry.Dword("K", "C", 1) });
        var parent = new RegistryValueTask(new TaskMetadata("parent", "ui", "p", "p", RiskFlags.None, Array.Empty<string>(), 9), new[] { RegistryEntry.Dword("K", "P", 1) });
        var sorted = TaskRunner.TopologicalSort(new List<ITask> { child, parent });
        Assert.Equal(new[] { "parent", "child" }, sorted.Select(t => t.Metadata.Id).ToArray());
    }
}

public class PathTaskTests
{
    [Fact]
    public void KnownFolder_MovesAndSetsPath_RollbackRestoresPathOnMoveFailure()
    {
        var (svc, _, _, shell, fs) = TestData.Services();
        shell.Folders[KnownFolder.Documents] = @"C:\Users\u\Documents";
        fs.Dirs.Add(@"C:\Users\u\Documents");
        var s = TestData.Snapshot();
        var a = TestData.Answers(s);
        var task = new KnownFolderTask(KnownFolder.Documents, "Documents", "文档", 1);
        var journal = new InMemoryJournal();
        var ctx = svc.CreateContext(task.Metadata.Id, s, a, journal, CancellationToken.None);

        Assert.False(task.Detect(ctx).Satisfied);
        task.Apply(ctx);
        Assert.Equal(@"D:\Data\Documents", shell.Folders[KnownFolder.Documents]);
        Assert.Single(shell.Moves);
        Assert.True(task.Verify(ctx));

        // 失败路径：移动抛错 → 抛 TaskFailedException，Rollback 后路径恢复
        shell.Folders[KnownFolder.Documents] = @"C:\Users\u\Documents";
        shell.FailMove = true;
        var journal2 = new InMemoryJournal();
        var ctx2 = svc.CreateContext(task.Metadata.Id, s, a, journal2, CancellationToken.None);
        Assert.Throws<TaskFailedException>(() => task.Apply(ctx2));
        task.Rollback(ctx2, journal2.EntriesFor(task.Metadata.Id));
        Assert.Equal(@"C:\Users\u\Documents", shell.Folders[KnownFolder.Documents]);
    }

    [Fact]
    public void Desktop_NotApplicableWhenOneDriveProtectsIt()
    {
        var (svc, _, _, shell, _) = TestData.Services();
        shell.Folders[KnownFolder.Desktop] = @"C:\Users\u\OneDrive\Desktop";
        var s = TestData.Snapshot(desktopProtected: true);
        var plan = new Planner(svc).Build(TaskCatalog.All, s, TestData.Answers(s));
        var desktop = plan.Items.Single(i => i.TaskId == KnownFolderTask.IdFor(KnownFolder.Desktop));
        Assert.Equal(PlanState.NotApplicable, desktop.State);
    }

    [Fact]
    public void EnvVar_OnlyForDetectedTools_SkipsWhenAlreadyOffSystemDrive()
    {
        var (svc, _, env, _, _) = TestData.Services();
        env.User["PIP_CACHE_DIR"] = @"E:\mycache";
        var s = TestData.Snapshot("D:", false, "pip", "npm");
        var plan = new Planner(svc).Build(TaskCatalog.All, s, TestData.Answers(s));

        Assert.Equal(PlanState.Skipped, plan.Items.Single(i => i.TaskId == "env.pip").State);
        Assert.Equal(PlanState.Planned, plan.Items.Single(i => i.TaskId == "env.npm").State);
        Assert.DoesNotContain(plan.Items, i => i.TaskId == "env.go");
    }

    [Fact]
    public void NoDataDrive_HidesAllPathTasks()
    {
        var (svc, _, _, _, _) = TestData.Services();
        var s = TestData.Snapshot(dataDrive: null, false, "pip");
        var plan = new Planner(svc).Build(TaskCatalog.All, s, TestData.Answers(s));
        Assert.DoesNotContain(plan.Items, i => i.Module == "path" || i.Module == "env");
    }
}

public class CatalogTests
{
    [Fact]
    public void IdsUnique_DependenciesExist_ParentsOrderedFirst()
    {
        var all = TaskCatalog.All;
        var ids = all.Select(t => t.Metadata.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var byId = all.ToDictionary(t => t.Metadata.Id);
        foreach (var t in all)
            foreach (var d in t.Metadata.DependsOn)
            {
                Assert.True(byId.ContainsKey(d), $"{t.Metadata.Id} 依赖不存在的 {d}");
                Assert.True(byId[d].Metadata.Order < t.Metadata.Order, $"{d} 应排在 {t.Metadata.Id} 之前");
            }
    }

    [Fact]
    public void OptionalTasksAlwaysListed_ButNotRecommended()
    {
        var (svc, _, _, _, _) = TestData.Services();
        var s = TestData.Snapshot();
        var plan = new Planner(svc).Build(TaskCatalog.All, s, TestData.Answers(s));
        Assert.Contains(plan.Items, i => i.Module == "promo");
        Assert.All(plan.Items.Where(i => i.Module == "promo"), i => Assert.False(i.Checked));
        Assert.False(plan.Items.Single(i => i.TaskId == "ui.classic_menu").Checked);
        Assert.False(plan.Items.Single(i => i.TaskId == "ime.default_english").Checked);
        Assert.True(plan.Items.Single(i => i.TaskId == "ui.file_ext").Checked);
    }
}

public class SerializationTests
{
    [Fact]
    public void PlanAndSnapshotRoundTrip()
    {
        var (svc, _, _, _, _) = TestData.Services();
        var s = TestData.Snapshot("D:", true, "pip");
        var plan = new Planner(svc).Build(TaskCatalog.All, s, TestData.Answers(s));

        var planJson = JsonSerializer.Serialize(plan, JsonDefaults.Options);
        var plan2 = JsonSerializer.Deserialize<Plan>(planJson, JsonDefaults.Options)!;
        Assert.Equal(plan.Items.Count, plan2.Items.Count);
        Assert.Equal(plan.Answers, plan2.Answers);
        // record 相等性按引用比较 DependsOn 集合，这里逐字段比较
        var a0 = plan.Items[0]; var b0 = plan2.Items[0];
        Assert.Equal(a0.TaskId, b0.TaskId);
        Assert.Equal(a0.State, b0.State);
        Assert.Equal(a0.Checked, b0.Checked);
        Assert.Equal(a0.Risk, b0.Risk);
        Assert.Equal(a0.TargetValue, b0.TargetValue);
        Assert.Equal(a0.DependsOn.ToArray(), b0.DependsOn.ToArray());
        var deps = plan2.Items.Single(i => i.TaskId == "path.temp.user").DependsOn;
        Assert.Equal(new[] { "path.skeleton" }, deps.ToArray());

        var snapJson = JsonSerializer.Serialize(s, JsonDefaults.Options);
        var s2 = JsonSerializer.Deserialize<EnvironmentSnapshot>(snapJson, JsonDefaults.Options)!;
        Assert.Equal(s.DataDrive, s2.DataDrive);
        Assert.Equal(s.Tools.Count, s2.Tools.Count);
        Assert.True(s2.OneDrive.DesktopProtected);
    }
}
