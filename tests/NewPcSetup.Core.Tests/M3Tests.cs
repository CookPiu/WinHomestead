using System;
using System.IO;
using System.Linq;
using System.Threading;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Engine;
using NewPcSetup.Core.Infrastructure;
using NewPcSetup.Core.Models;
using NewPcSetup.Tasks;
using Xunit;

namespace NewPcSetup.Core.Tests;

public class DiskAdvisorTests
{
    [Fact]
    public void SingleFreshDisk_SuggestsByCapacity_AndAutomatable()
    {
        var a = DiskAdvisor.Advise(TestData.SingleDiskFresh());
        Assert.True(a.NeedsPartition);
        Assert.True(a.Automatable);
        Assert.Equal(240, Math.Round(a.SuggestedSystemGb));
        Assert.Equal(260, Math.Round(a.SuggestedDataGb));
    }

    [Fact]
    public void NotAutomatable_WhenUsedOver80Gb_OrOld_OrMdm()
    {
        Assert.False(DiskAdvisor.Advise(TestData.SingleDiskFresh(usedBytes: 100L << 30)).Automatable);
        Assert.False(DiskAdvisor.Advise(TestData.SingleDiskFresh(installedDaysAgo: 40)).Automatable);
        Assert.False(DiskAdvisor.Advise(TestData.SingleDiskFresh(mdm: true)).Automatable);
        Assert.True(DiskAdvisor.Advise(TestData.SingleDiskFresh(usedBytes: 100L << 30)).NeedsPartition);
    }

    [Fact]
    public void NoPartition_WhenDataVolumeExists_OrSmallDisk()
    {
        Assert.False(DiskAdvisor.Advise(TestData.Snapshot()).NeedsPartition);
        Assert.False(DiskAdvisor.Advise(TestData.SingleDiskFresh(diskBytes: 240L << 30)).NeedsPartition);
        Assert.Equal(0, DiskAdvisor.SuggestSystemSize(256L << 30));
        Assert.Equal(176L << 30, DiskAdvisor.SuggestSystemSize(512L << 30));
        Assert.Equal(288L << 30, DiskAdvisor.SuggestSystemSize(2000L << 30));
    }

    [Fact]
    public void NextFreeLetter_SkipsUsed()
    {
        Assert.Equal('D', DiskAdvisor.NextFreeDriveLetter(new[] { "C:" }));
        Assert.Equal('E', DiskAdvisor.NextFreeDriveLetter(new[] { "C:\\", "D:\\" }));
    }
}

public class ShrinkAndCreateTaskTests
{
    private static Answers Consent(EnvironmentSnapshot s) => TestData.Answers(s) with { CreatePartition = true, DataDrive = "D:" };

    [Fact]
    public void PlannedWithConsent_PathTasksDependOnIt_ApplyRunsResizeCreateFormat()
    {
        var (svc, _, _, _, _, _, storage) = TestData.ServicesFull();
        var s = TestData.SingleDiskFresh();
        var a = Consent(s);
        var plan = new Planner(svc).Build(TaskCatalog.All, s, a);

        var disk = plan.Items.Single(i => i.TaskId == ShrinkAndCreateTask.Id);
        Assert.Equal(PlanState.Planned, disk.State);
        Assert.True(disk.Checked);
        Assert.Contains("压缩 C: 到 240 GB", disk.TargetValue);
        Assert.DoesNotContain(plan.Items, i => i.TaskId == DiskSuggestTask.Id);
        var skeleton = plan.Items.Single(i => i.TaskId == PathSkeletonTask.Id);
        Assert.True(skeleton.Checked);
        Assert.Contains(ShrinkAndCreateTask.Id, skeleton.DependsOn);

        plan = PlanEditor.SetChecked(plan, ShrinkAndCreateTask.Id, false);
        Assert.False(plan.Items.Single(i => i.TaskId == PathSkeletonTask.Id).Checked);
        Assert.False(plan.Items.Single(i => i.TaskId == "path.temp.user").Checked);

        var task = new ShrinkAndCreateTask();
        var ctx = svc.CreateContext(ShrinkAndCreateTask.Id, s, a, new InMemoryJournal(), CancellationToken.None);
        task.Apply(ctx);
        Assert.Equal(new[] { "resize:240", "create:D", "format:D::Data" }, storage.Calls.ToArray());
        Assert.True(task.Verify(ctx));
    }

    [Fact]
    public void NotApplicable_WhenShrinkRoomTooSmall_OrNoConsent()
    {
        var (svc, _, _, _, _, _, storage) = TestData.ServicesFull();
        var s = TestData.SingleDiskFresh();
        storage.SizeMin = 450L << 30;
        var plan = new Planner(svc).Build(TaskCatalog.All, s, Consent(s));
        var disk = plan.Items.Single(i => i.TaskId == ShrinkAndCreateTask.Id);
        Assert.Equal(PlanState.NotApplicable, disk.State);
        Assert.Contains("最多只能压缩 50 GB", disk.Reason);

        var noConsent = new Planner(svc).Build(TaskCatalog.All, s, TestData.Answers(s));
        Assert.DoesNotContain(noConsent.Items, i => i.TaskId == ShrinkAndCreateTask.Id);
        var suggest = noConsent.Items.Single(i => i.TaskId == DiskSuggestTask.Id);
        Assert.Equal(PlanState.Planned, suggest.State);
        Assert.DoesNotContain(noConsent.Items, i => i.Module == "path");

        var ctx = svc.CreateContext(DiskSuggestTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);
        new DiskSuggestTask().Apply(ctx);
        Assert.Contains("压缩卷", ctx.ManualSteps.Single());
    }
}

public class HagsTaskTests
{
    [Fact]
    public void NotApplicableWithoutKey_PlannedWhenOff_HiddenOnMdm()
    {
        var (svc, reg, _, _, _) = TestData.Services();
        var s = TestData.Snapshot();
        var plan = new Planner(svc).Build(TaskCatalog.All, s, TestData.Answers(s));
        Assert.Equal(PlanState.NotApplicable, plan.Items.Single(i => i.TaskId == HagsTask.Id).State);

        reg.Seed(RegRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 1, RegKind.DWord);
        plan = new Planner(svc).Build(TaskCatalog.All, s, TestData.Answers(s));
        var item = plan.Items.Single(i => i.TaskId == HagsTask.Id);
        Assert.Equal(PlanState.Planned, item.State);
        Assert.True(item.Checked);
        Assert.NotEqual(0, (int)(item.Risk & RiskFlags.NeedsReboot));

        var mdm = TestData.Snapshot("D:", false, isLaptop: true, mdm: true);
        Assert.DoesNotContain(new Planner(svc).Build(TaskCatalog.All, mdm, TestData.Answers(mdm)).Items, i => i.TaskId == HagsTask.Id);
    }
}

public class ResumeTests
{
    private sealed class NoopRestore : ISystemRestore { public bool CreateRestorePoint(string d) => true; }

    private static RegistryValueTask Dword(string id, string name, RiskFlags risk = RiskFlags.Reversible)
        => new(new TaskMetadata(id, "ui", id, id, risk, Array.Empty<string>(), 1), new[] { RegistryEntry.Dword("K", name, 1) });

    [Fact]
    public void ContinueSkipsFinishedTasks_ReverifyPromotesNeedsReboot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "NewPcSetupTests", Guid.NewGuid().ToString("N"));
        var store = new StateStore(dir);
        try
        {
            var (svc, reg, _, _, _) = TestData.Services();
            var coordinator = new ExecutionCoordinator(svc, new NoopRestore(), store);
            var s = TestData.Snapshot();
            var a = TestData.Answers(s);
            var tasks = new ITask[] { Dword("t.a", "A"), Dword("t.b", "B", RiskFlags.Reversible | RiskFlags.NeedsReboot) };
            var plan = new Planner(svc).Build(tasks, s, a);

            // 模拟崩溃：t.a 已完成并落盘为部分结果，state 仍为 pending
            store.Save($"plan-{plan.Id}.json", plan);
            store.Save($"snapshot-{plan.Id}.json", s);
            store.Save($"result-{plan.Id}.json", new ExecutionResult(plan.Id, DateTime.Now, null, false, false,
                new[] { new TaskResult("t.a", "t.a", TaskOutcome.Done, null, 1, Array.Empty<string>()) }));
            store.SaveState(new AppState(plan.Id, true));
            reg.Seed(RegRoot.CurrentUser, "K", "A", 1, RegKind.DWord);

            var session = coordinator.LoadLast();
            Assert.NotNull(session);
            Assert.True(session!.Unfinished);

            var result = coordinator.Continue(session, tasks, null, null, CancellationToken.None);
            Assert.Equal(new[] { "t.a", "t.b" }, result.Results.Select(r => r.TaskId).ToArray());
            Assert.Equal(TaskOutcome.NeedsReboot, result.Results[1].Outcome);
            Assert.True(result.RebootRequired);
            Assert.NotNull(result.FinishedAt);
            Assert.False(store.LoadState().PendingResume);
            Assert.Equal(1, reg.GetValue(RegRoot.CurrentUser, "K", "B").Value);

            var after = coordinator.LoadLast()!;
            Assert.False(after.Unfinished);
            var verified = coordinator.Reverify(after, tasks);
            Assert.Equal(TaskOutcome.Done, verified.Results[1].Outcome);
            Assert.False(verified.RebootRequired);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
