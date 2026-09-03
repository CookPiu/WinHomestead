using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Engine;
using NewPcSetup.Core.Infrastructure;
using NewPcSetup.Core.Models;
using NewPcSetup.Tasks;
using Xunit;

namespace NewPcSetup.Core.Tests;

public class MachineTempTaskTests
{
    [Fact]
    public void HiddenWhenMdm_DefaultUnchecked_ApplyAndRollback()
    {
        var (svc, _, env, _, fs) = TestData.Services();
        env.Machine["TEMP"] = @"C:\WINDOWS\TEMP"; env.Machine["TMP"] = @"C:\WINDOWS\TEMP";

        var mdm = TestData.Snapshot("D:", false, isLaptop: true, mdm: true);
        Assert.DoesNotContain(new Planner(svc).Build(TaskCatalog.All, mdm, TestData.Answers(mdm)).Items, i => i.TaskId == MachineTempTask.Id);

        var s = TestData.Snapshot();
        var plan = new Planner(svc).Build(TaskCatalog.All, s, TestData.Answers(s));
        var item = plan.Items.Single(i => i.TaskId == MachineTempTask.Id);
        Assert.Equal(PlanState.Planned, item.State);
        Assert.False(item.Checked);

        var task = new MachineTempTask();
        var journal = new InMemoryJournal();
        var ctx = svc.CreateContext(MachineTempTask.Id, s, TestData.Answers(s), journal, CancellationToken.None);
        task.Apply(ctx);
        Assert.True(task.Verify(ctx));
        Assert.Equal(@"D:\Temp\System", env.Machine["TEMP"]);
        Assert.Contains(@"D:\Temp\System", fs.Dirs);

        task.Rollback(ctx, journal.EntriesFor(MachineTempTask.Id));
        Assert.Equal(@"C:\WINDOWS\TEMP", env.Machine["TEMP"]);
        Assert.Equal(@"C:\WINDOWS\TEMP", env.Machine["TMP"]);
    }
}

public class QuickAccessPinTaskTests
{
    [Fact]
    public void PinsApplications_SkippedWhenAlreadyPinned_RollbackUnpins()
    {
        var (svc, _, _, shell, fs) = TestData.Services();
        var s = TestData.Snapshot();
        var a = TestData.Answers(s);
        var task = new QuickAccessPinTask();
        var journal = new InMemoryJournal();
        var ctx = svc.CreateContext(QuickAccessPinTask.Id, s, a, journal, CancellationToken.None);

        Assert.False(task.Detect(ctx).Satisfied);
        task.Apply(ctx);
        Assert.True(task.Verify(ctx));
        Assert.Contains(@"D:\Applications", shell.Pinned);
        Assert.Contains(@"D:\Applications", fs.Dirs);

        task.Rollback(ctx, journal.EntriesFor(QuickAccessPinTask.Id));
        Assert.DoesNotContain(@"D:\Applications", shell.Pinned);

        shell.Pinned.Add(@"D:\Applications"); fs.Dirs.Add(@"D:\Applications");
        var plan = new Planner(svc).Build(TaskCatalog.All, s, a);
        Assert.Equal(PlanState.Skipped, plan.Items.Single(i => i.TaskId == QuickAccessPinTask.Id).State);
    }
}

public class TempCleanupTaskTests
{
    [Fact]
    public void DeletesOldFiles_SkipsLocked_ReportsManualStep_NotReversible()
    {
        var (svc, _, _, _, fs) = TestData.Services();
        var s = TestData.Snapshot();
        var dir = TempCleanupTask.OldTempDir(s);
        fs.OldFiles[dir] = new List<FileEntry> { new(dir + @"\a.tmp", 10), new(dir + @"\b.tmp", 20), new(dir + @"\locked.tmp", 30) };
        fs.Locked.Add(dir + @"\locked.tmp");

        var task = new TempCleanupTask();
        Assert.Equal(RiskFlags.None, task.Metadata.Risk & RiskFlags.Reversible);
        var ctx = svc.CreateContext(TempCleanupTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);
        var d = task.Detect(ctx);
        Assert.False(d.Satisfied);
        Assert.Contains("3", d.CurrentValue);

        task.Apply(ctx);
        Assert.True(task.Verify(ctx));
        Assert.Equal(2, fs.Deleted.Count);
        Assert.Single(ctx.ManualSteps);

        fs.OldFiles[dir].Clear();
        Assert.True(task.Detect(ctx).Satisfied);
    }
}

public class HibernateOffTaskTests
{
    [Fact]
    public void OnlyForDesktop_DefaultUnchecked_ApplyAndRollback()
    {
        var (svc, _, _, _, _, power) = TestData.ServicesWithPower();
        var laptop = TestData.Snapshot();
        Assert.DoesNotContain(new Planner(svc).Build(TaskCatalog.All, laptop, TestData.Answers(laptop)).Items, i => i.TaskId == HibernateOffTask.Id);

        var desktop = TestData.Snapshot("D:", false, isLaptop: false, mdm: false);
        var plan = new Planner(svc).Build(TaskCatalog.All, desktop, TestData.Answers(desktop));
        var item = plan.Items.Single(i => i.TaskId == HibernateOffTask.Id);
        Assert.Equal(PlanState.Planned, item.State);
        Assert.False(item.Checked);

        var task = new HibernateOffTask();
        var journal = new InMemoryJournal();
        var ctx = svc.CreateContext(HibernateOffTask.Id, desktop, TestData.Answers(desktop), journal, CancellationToken.None);
        task.Apply(ctx);
        Assert.True(task.Verify(ctx));
        Assert.False(power.Hibernate);

        task.Rollback(ctx, journal.EntriesFor(HibernateOffTask.Id));
        Assert.True(power.Hibernate);
        Assert.Equal(2, power.SetCalls);
    }
}

public class ImeTaskTests
{
    [Fact]
    public void ImeTasksListedAsOptional()
    {
        var (svc, _, _, _, _) = TestData.Services();
        var s = TestData.Snapshot();
        var plan = new Planner(svc).Build(TaskCatalog.All, s, TestData.Answers(s));
        foreach (var id in new[] { "ime.shift_switch", "ime.punct_hotkey", "ime.default_english" })
        {
            var item = plan.Items.Single(i => i.TaskId == id);
            Assert.Equal(PlanState.Planned, item.State);
            Assert.False(item.Checked);
        }
    }
}

public class ReportHtmlTests
{
    [Fact]
    public void ContainsResultsAndEscapesHtml()
    {
        var result = new ExecutionResult("p1", DateTime.Now, DateTime.Now, false, true, new[]
        {
            new TaskResult("t1", "任务<一>", TaskOutcome.Done, null, 1, Array.Empty<string>()),
            new TaskResult("t2", "任务二", TaskOutcome.Failed, "错误 & 原因", 1, Array.Empty<string>()),
        });
        var html = ReportHtml.Render(TestData.Snapshot(), null, result, new[] { "手动<步骤>" });
        Assert.Contains("任务&lt;一&gt;", html);
        Assert.Contains("错误 &amp; 原因", html);
        Assert.Contains("手动&lt;步骤&gt;", html);
        Assert.Contains("需要重启", html);
        Assert.DoesNotContain("<一>", html);
    }
}
