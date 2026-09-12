using System.Linq;
using System.Threading;
using WinHomestead.Core.Abstractions;
using WinHomestead.Core.Engine;
using WinHomestead.Core.Infrastructure;
using WinHomestead.Tasks;
using Xunit;

namespace WinHomestead.Core.Tests;

public class RefreshRateTaskTests
{
    [Fact]
    public void HighRefreshPanelStuckAt60_IsPlannedAndFixed()
    {
        var (svc, _, _, _, _, _, _, display) = TestData.ServicesWithDisplay();
        var s = TestData.Snapshot();
        var task = new RefreshRateTask();
        var journal = new InMemoryJournal();
        var ctx = svc.CreateContext(RefreshRateTask.Id, s, TestData.Answers(s), journal, CancellationToken.None);

        var before = task.Detect(ctx);
        Assert.False(before.Satisfied);
        Assert.Equal("60 Hz", before.CurrentValue);
        Assert.Equal("165 Hz", before.TargetValue);

        task.Apply(ctx);
        Assert.Equal(new[] { 165 }, display.SetCalls);
        Assert.True(task.Verify(ctx));

        task.Rollback(ctx, journal.EntriesFor(RefreshRateTask.Id));
        Assert.Equal(60, display.Mode!.Hz);
    }

    [Fact]
    public void AlreadyAtMax_IsSatisfied_AndApplyDoesNothing()
    {
        var (svc, _, _, _, _, _, _, display) = TestData.ServicesWithDisplay();
        display.Mode = new DisplayMode(1920, 1080, 60, 60);
        var s = TestData.Snapshot();
        var task = new RefreshRateTask();
        var ctx = svc.CreateContext(RefreshRateTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        Assert.True(task.Detect(ctx).Satisfied);
        task.Apply(ctx);
        Assert.Empty(display.SetCalls);
    }

    [Fact]
    public void NoDisplayMode_IsNotApplicable()
    {
        var (svc, _, _, _, _, _, _, display) = TestData.ServicesWithDisplay();
        display.Mode = null;
        var s = TestData.Snapshot();
        var ctx = svc.CreateContext(RefreshRateTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        Assert.StartsWith(DetectResult.NotApplicable, new RefreshRateTask().Detect(ctx).Reason);
    }
}

public class AcTimeoutsTaskTests
{
    [Fact]
    public void DefaultTimeouts_ArePlanned_AndOnlyAcSideChanges()
    {
        var (svc, _, _, _, _, power, _, _) = TestData.ServicesWithDisplay();
        var s = TestData.Snapshot();
        var task = new AcTimeoutsTask();
        var journal = new InMemoryJournal();
        var ctx = svc.CreateContext(AcTimeoutsTask.Id, s, TestData.Answers(s), journal, CancellationToken.None);

        Assert.False(task.Detect(ctx).Satisfied);
        task.Apply(ctx);

        Assert.Equal(new[] { (15, 60) }, power.AcTimeoutCalls);
        Assert.Equal(900, power.Timeouts!.MonitorAcSeconds);
        Assert.Equal(3600, power.Timeouts.StandbyAcSeconds);
        // 电池侧保持出厂值
        Assert.Equal(300, power.Timeouts.MonitorDcSeconds);
        Assert.Equal(900, power.Timeouts.StandbyDcSeconds);
        Assert.True(task.Verify(ctx));

        task.Rollback(ctx, journal.EntriesFor(AcTimeoutsTask.Id));
        Assert.Equal(600, power.Timeouts.MonitorAcSeconds);
        Assert.Equal(1800, power.Timeouts.StandbyAcSeconds);
    }

    /// <summary>已经比建议值更宽松（或设成"从不"）的机器不该被标成待办。</summary>
    [Fact]
    public void AlreadyLongerOrNever_IsSatisfied()
    {
        var (svc, _, _, _, _, power, _, _) = TestData.ServicesWithDisplay();
        power.Timeouts = new PowerTimeouts(1800, 300, 0, 900);
        var s = TestData.Snapshot();
        var ctx = svc.CreateContext(AcTimeoutsTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        Assert.True(new AcTimeoutsTask().Detect(ctx).Satisfied);
    }

    [Fact]
    public void UnreadableTimeouts_IsNotApplicable()
    {
        var (svc, _, _, _, _, power, _, _) = TestData.ServicesWithDisplay();
        power.Timeouts = null;
        var s = TestData.Snapshot();
        var ctx = svc.CreateContext(AcTimeoutsTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        Assert.StartsWith(DetectResult.NotApplicable, new AcTimeoutsTask().Detect(ctx).Reason);
    }
}

public class FastStartupOffTaskTests
{
    private const string Key = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";

    [Fact]
    public void EnabledFastStartup_IsTurnedOff_AndRollsBack()
    {
        var (svc, reg, _, _, _, _, _, _) = TestData.ServicesWithDisplay();
        reg.Seed(RegRoot.LocalMachine, Key, "HiberbootEnabled", 1, RegKind.DWord);
        var s = TestData.Snapshot();
        var task = new FastStartupOffTask();
        var journal = new InMemoryJournal();
        var ctx = svc.CreateContext(FastStartupOffTask.Id, s, TestData.Answers(s), journal, CancellationToken.None);

        Assert.False(task.Detect(ctx).Satisfied);
        task.Apply(ctx);
        Assert.True(task.Verify(ctx));

        task.Rollback(ctx, journal.EntriesFor(FastStartupOffTask.Id));
        Assert.Equal(1, System.Convert.ToInt64(reg.GetValue(RegRoot.LocalMachine, Key, "HiberbootEnabled").Value));
    }

    /// <summary>休眠关掉之后这个值就不存在了，此时该项没有意义。</summary>
    [Fact]
    public void MissingValue_IsNotApplicable()
    {
        var (svc, _, _, _, _, _, _, _) = TestData.ServicesWithDisplay();
        var s = TestData.Snapshot();
        var ctx = svc.CreateContext(FastStartupOffTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        Assert.StartsWith(DetectResult.NotApplicable, new FastStartupOffTask().Detect(ctx).Reason);
    }

    [Fact]
    public void MdmMachine_IsNotListed()
    {
        var s = TestData.Snapshot("D:", false, isLaptop: true, mdm: true);
        Assert.False(new FastStartupOffTask().IsApplicable(s, TestData.Answers(s)));
    }
}
