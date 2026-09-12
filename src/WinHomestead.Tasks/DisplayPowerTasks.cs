using System;
using WinHomestead.Core.Abstractions;
using WinHomestead.Core.Engine;
using WinHomestead.Core.Models;

namespace WinHomestead.Tasks;

/// <summary>
/// 把主显示器切到当前分辨率下支持的最高刷新率。高刷屏出厂跑 60 Hz 是新机常态——
/// 面板支持 165 Hz，系统却按默认模式启动，不少人用了很久都没发现。
/// </summary>
public sealed class RefreshRateTask : TaskBase
{
    public const string Id = "display.refresh_rate";

    public override TaskMetadata Metadata { get; } = new(Id, "display", "把刷新率拉到最高",
        "把主显示器切到当前分辨率下驱动报告的最高刷新率。分辨率与色深不变，改动写进注册表，重启后保持。" +
        "笔记本用电池时系统可能主动降回 60 Hz，那是省电策略，不是这一项没生效。",
        RiskFlags.Reversible, Array.Empty<string>(), 240);

    public override DetectResult Detect(TaskContext ctx)
    {
        var mode = ctx.Display.Primary();
        if (mode == null) return DetectResult.NotApplicableBecause("读不到当前的显示模式");
        if (mode.MaxHz <= mode.Hz)
            return new DetectResult(true, $"{mode.Hz} Hz", "已是最高",
                null, $"{mode.Width}×{mode.Height} 下最高 {mode.MaxHz} Hz");

        return new DetectResult(false, $"{mode.Hz} Hz", $"{mode.MaxHz} Hz",
            null, $"{mode.Width}×{mode.Height}，当前 {mode.Hz} Hz，最高 {mode.MaxHz} Hz");
    }

    public override void Apply(TaskContext ctx)
    {
        var mode = ctx.Display.Primary() ?? throw new TaskFailedException("读不到当前的显示模式");
        if (mode.MaxHz > mode.Hz) ctx.Display.SetRefreshRate(mode.MaxHz);
    }
}

/// <summary>
/// 关闭快速启动。它让"关机"实际变成把内核会话休眠到 hiberfil.sys，于是关机重启并不等于一次干净的冷启动：
/// 双系统下会锁住 NTFS 分区，外设与驱动异常有时也只有真关机才能复位。代价是开机慢几秒。
/// </summary>
public sealed class FastStartupOffTask : TaskBase
{
    public const string Id = "power.fast_startup_off";
    private const string Key = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";
    private const string Name = "HiberbootEnabled";

    public override TaskMetadata Metadata { get; } = new(Id, "power", "关闭快速启动",
        "关掉之后，\"关机\"就是真正的关机，而不是把内核会话休眠起来。双系统、外接设备异常、驱动更新后行为不对这几类问题都和它有关。" +
        "代价是开机慢几秒。休眠本身不受影响。",
        RiskFlags.Reversible | RiskFlags.AdminOnly | RiskFlags.NeedsReboot, Array.Empty<string>(), 241);

    public override bool IsApplicable(EnvironmentSnapshot s, Answers a) => !s.IsMdmEnrolled;
    public override bool DefaultChecked(EnvironmentSnapshot s, Answers a) => false;

    public override DetectResult Detect(TaskContext ctx)
    {
        var (v, _) = ctx.Registry.GetValue(RegRoot.LocalMachine, Key, Name);
        if (v == null) return DetectResult.NotApplicableBecause("系统没有提供快速启动开关（休眠关闭时它也不存在）");
        var on = Convert.ToInt64(v) != 0;
        return new DetectResult(!on, on ? "已开启" : "已关闭", "关闭", null, $"{Name}={(on ? 1 : 0)} → 0");
    }

    public override void Apply(TaskContext ctx) => ctx.Registry.SetValue(RegRoot.LocalMachine, Key, Name, 0, RegKind.DWord);
}

/// <summary>
/// 插电时的关屏与睡眠超时。默认值（关屏 10 分钟、睡眠 30 分钟）是按笔记本省电定的，
/// 插着电源下载、编译、挂后台任务时嫌短。这一项只动交流电源那一侧，电池侧保持系统默认。
/// </summary>
public sealed class AcTimeoutsTask : TaskBase
{
    public const string Id = "power.ac_timeouts";

    /// <summary>插电时：关屏 15 分钟、睡眠 60 分钟。</summary>
    private const int MonitorMinutes = 15;
    private const int StandbyMinutes = 60;

    public override TaskMetadata Metadata { get; } = new(Id, "power", "插电时晚点关屏与睡眠",
        $"把插电状态下的关屏时间设为 {MonitorMinutes} 分钟、睡眠时间设为 {StandbyMinutes} 分钟。" +
        "系统默认按省电定（关屏 10 分钟、睡眠 30 分钟），插着电源下载或编译时容易被打断。" +
        "电池供电那一侧不动，保持系统默认的省电策略。",
        RiskFlags.Reversible | RiskFlags.AdminOnly, Array.Empty<string>(), 242);

    public override bool DefaultChecked(EnvironmentSnapshot s, Answers a) => false;

    private static string Describe(int seconds) => seconds == 0 ? "从不" : $"{seconds / 60} 分钟";

    public override DetectResult Detect(TaskContext ctx)
    {
        var t = ctx.Power.ReadTimeouts();
        if (t == null) return DetectResult.NotApplicableBecause("读不到当前电源方案的超时设置");

        var satisfied = t.MonitorAcMinutes >= MonitorMinutes && (t.StandbyAcSeconds == 0 || t.StandbyAcMinutes >= StandbyMinutes);
        return new DetectResult(satisfied,
            $"插电时 {Describe(t.MonitorAcSeconds)}关屏、{Describe(t.StandbyAcSeconds)}睡眠",
            $"{MonitorMinutes} 分钟关屏、{StandbyMinutes} 分钟睡眠",
            null, $"电池侧当前为 {Describe(t.MonitorDcSeconds)}关屏、{Describe(t.StandbyDcSeconds)}睡眠，本项不动");
    }

    public override void Apply(TaskContext ctx) => ctx.Power.SetAcTimeouts(MonitorMinutes, StandbyMinutes);
}
