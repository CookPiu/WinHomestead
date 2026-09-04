using System;
using System.Collections.Generic;
using System.Linq;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Engine;
using NewPcSetup.Core.Models;

namespace NewPcSetup.Tasks;

/// <summary>压缩 C 并新建数据分区。不可撤销；单盘无数据分区时列出，由用户点击执行并二次确认。</summary>
public sealed class ShrinkAndCreateTask : TaskBase
{
    public const string Id = "disk.shrink_and_create";
    private const long Gb = 1L << 30;

    public override TaskMetadata Metadata { get; } = new(Id, "disk", "压缩 C 盘并新建数据分区",
        "按建议把 C 盘压缩到目标大小，用释放出的空间新建一个 NTFS 分区作为数据盘（卷标 Data）。不删除、不移动任何已有分区与文件。此操作不可撤销。",
        RiskFlags.AdminOnly, Array.Empty<string>(), 90);

    public override bool IsApplicable(EnvironmentSnapshot s, Answers a)
        => a.CreatePartition && s.DataDrive == null && !s.IsMdmEnrolled && DiskAdvisor.Advise(s).NeedsPartition;
    /// <summary>不可撤销的分区操作不标“推荐”，由用户明确点击执行并二次确认。</summary>
    public override bool DefaultChecked(EnvironmentSnapshot s, Answers a) => false;

    private sealed record Layout(PartitionInfo System, long NewSystemBytes, long DataBytes, char Letter);

    private static char TargetLetter(TaskContext ctx)
        => !string.IsNullOrEmpty(ctx.Answers.DataDrive) ? char.ToUpperInvariant(ctx.Answers.DataDrive![0])
           : DiskAdvisor.NextFreeDriveLetter(ctx.Storage.UsedDriveLetters());

    /// <summary>返回 null 时 reason 说明为何不能自动执行。</summary>
    private static Layout? Compute(TaskContext ctx, out string reason)
    {
        var advice = DiskAdvisor.Advise(ctx.Snapshot);
        if (!advice.Automatable) { reason = advice.Reason; return null; }
        var part = ctx.Storage.GetPartition(ctx.Snapshot.SystemDrive);
        if (part == null) { reason = "无法读取系统分区信息"; return null; }
        var support = ctx.Storage.GetSupportedSize(part);
        var shrinkable = part.SizeBytes - support.SizeMin;
        if (shrinkable < advice.SuggestedDataBytes * DiskAdvisor.MinShrinkRatio)
        {
            reason = $"C 盘最多只能压缩 {shrinkable / (double)Gb:F0} GB（建议 {advice.SuggestedDataGb:F0} GB）。不可移动的系统文件挡住了压缩，可先关闭休眠与页面文件后重试，或在磁盘管理中手动处理";
            return null;
        }
        var newSystem = Math.Max(advice.SuggestedSystemBytes, support.SizeMin);
        reason = string.Empty;
        return new Layout(part, newSystem, part.SizeBytes - newSystem, TargetLetter(ctx));
    }

    public override DetectResult Detect(TaskContext ctx)
    {
        var layout = Compute(ctx, out var reason);
        if (layout == null) return DetectResult.NotApplicableBecause(reason);
        var sys = ctx.Snapshot.Volumes.First(v => v.IsSystem);
        return new DetectResult(false,
            $"{sys.DriveLetter} 单分区 {sys.SizeGb:F2} GB，已用 {sys.UsedGb:F2} GB",
            $"压缩 {sys.DriveLetter} 到 {layout.NewSystemBytes / (double)Gb:F0} GB，新建 {layout.Letter}: {layout.DataBytes / (double)Gb:F0} GB（NTFS，卷标 Data）");
    }

    public override void Apply(TaskContext ctx)
    {
        var layout = Compute(ctx, out var reason) ?? throw new TaskFailedException("前置条件已变化，未执行分区：" + reason);
        ctx.Log.Info($"[{Id}] 压缩 {layout.System.DriveLetter} → {layout.NewSystemBytes >> 30} GB");
        ctx.Storage.Resize(layout.System, layout.NewSystemBytes);
        ctx.Log.Info($"[{Id}] 新建分区 {layout.Letter}:");
        var created = ctx.Storage.CreatePartitionUsingMaximumSize(layout.System.DiskNumber, layout.Letter);
        ctx.Log.Info($"[{Id}] 格式化 {created.DriveLetter}");
        ctx.Storage.FormatNtfs(created, "Data");
    }

    public override bool Verify(TaskContext ctx)
    {
        var letter = TargetLetter(ctx) + ":";
        return ctx.Storage.UsedDriveLetters().Any(l => string.Equals(l, letter, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>分区无法撤销：不做任何事，只把现场情况写进手动项。</summary>
    public override void Rollback(TaskContext ctx, IReadOnlyList<JournalEntry> entries)
    {
        ctx.Log.Warn($"[{Id}] 分区操作失败后不做回滚");
        ctx.ManualSteps.Add("分区操作未完成：请打开“磁盘管理”（Win+X → 磁盘管理）检查 C 盘是否已被压缩、是否存在未分配空间，需要时手动新建简单卷并分配盘符。");
    }
}

/// <summary>只给分盘建议与手动步骤；单盘无数据分区且未自动分区时出现。</summary>
public sealed class DiskSuggestTask : TaskBase
{
    public const string Id = "disk.suggest";

    public override TaskMetadata Metadata { get; } = new(Id, "disk", "分盘建议（手动）",
        "这台电脑只有一个分区。按容量规则给出 C/D 的建议大小与磁盘管理操作步骤，写入报告的手动项。本工具不会自动改动分区。",
        RiskFlags.PromptOnly, Array.Empty<string>(), 91);

    public override bool IsApplicable(EnvironmentSnapshot s, Answers a)
        => s.DataDrive == null && DiskAdvisor.Advise(s).NeedsPartition;

    public override DetectResult Detect(TaskContext ctx)
    {
        var advice = DiskAdvisor.Advise(ctx.Snapshot);
        return new DetectResult(false, "单分区，" + advice.Reason,
            $"建议 C 约 {advice.SuggestedSystemGb:F0} GB，其余约 {advice.SuggestedDataGb:F0} GB 作为数据盘");
    }

    public override void Apply(TaskContext ctx)
    {
        var advice = DiskAdvisor.Advise(ctx.Snapshot);
        var shrinkMb = (long)(advice.SuggestedDataBytes / 1048576d);
        ctx.ManualSteps.Add(
            $"分盘建议：C 保留约 {advice.SuggestedSystemGb:F0} GB，其余约 {advice.SuggestedDataGb:F0} GB 作为数据盘。" +
            $"步骤：Win+X → 磁盘管理 → 右键 C: → 压缩卷 → “输入压缩空间量”填 {shrinkMb} → 压缩；" +
            "然后右键新出现的“未分配”空间 → 新建简单卷 → 一路默认（NTFS，卷标 Data，盘符 D）。完成后重新运行本工具即可迁移路径。" +
            (advice.Automatable ? string.Empty : $"（未自动执行的原因：{advice.Reason}）"));
    }

    public override bool Verify(TaskContext ctx) => true;
}

/// <summary>GPU 硬件加速调度（HAGS）。HwSchMode 值不存在说明显卡/驱动不支持，标不适用。</summary>
public sealed class HagsTask : TaskBase
{
    public const string Id = "gpu.hags";
    private const string Key = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string Name = "HwSchMode";

    public override TaskMetadata Metadata { get; } = new(Id, "gpu", "开启 GPU 硬件加速调度",
        "让显卡自行管理显存调度，降低延迟。只在显卡与驱动支持时出现（设置 → 显示 → 显示卡 中有此开关）。重启后生效。",
        RiskFlags.Reversible | RiskFlags.AdminOnly | RiskFlags.NeedsReboot, Array.Empty<string>(), 450);

    public override bool IsApplicable(EnvironmentSnapshot s, Answers a) => !s.IsMdmEnrolled;

    public override DetectResult Detect(TaskContext ctx)
    {
        var (v, _) = ctx.Registry.GetValue(RegRoot.LocalMachine, Key, Name);
        if (v == null) return DetectResult.NotApplicableBecause("显卡或驱动不支持硬件加速调度");
        var on = Convert.ToInt64(v) == 2;
        return new DetectResult(on, on ? "已开启" : "未开启", "开启");
    }

    public override void Apply(TaskContext ctx) => ctx.Registry.SetValue(RegRoot.LocalMachine, Key, Name, 2, RegKind.DWord);
}
