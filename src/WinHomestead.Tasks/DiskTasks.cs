using System;
using System.Collections.Generic;
using System.Linq;
using WinHomestead.Core.Abstractions;
using WinHomestead.Core.Engine;
using WinHomestead.Core.Infrastructure;
using WinHomestead.Core.Models;

namespace WinHomestead.Tasks;

/// <summary>压缩 C 并新建数据分区。不可撤销；单盘无数据分区时列出，由用户点击执行并二次确认。</summary>
public sealed class ShrinkAndCreateTask : TaskBase
{
    public const string Id = "disk.shrink_and_create";
    private const long Gb = 1L << 30;

    public override TaskMetadata Metadata { get; } = new(Id, "disk",
        L.S("压缩 C 盘并新建数据分区", "Shrink C: and create a data partition"),
        L.S("按建议把 C 盘压缩到目标大小，用释放出的空间新建一个 NTFS 分区作为数据盘（卷标 Data）。不删除、不移动任何已有分区与文件。此操作不可撤销。",
            "Shrinks C: to the suggested size and turns the freed space into an NTFS data partition labelled Data. No existing partition or file is deleted or moved. This cannot be undone."),
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
        if (part == null) { reason = L.S("无法读取系统分区信息", "can't read the system partition"); return null; }
        var support = ctx.Storage.GetSupportedSize(part);
        var shrinkable = part.SizeBytes - support.SizeMin;
        if (shrinkable < advice.SuggestedDataBytes * DiskAdvisor.MinShrinkRatio)
        {
            reason = L.S($"C 盘最多只能压缩 {shrinkable / (double)Gb:F0} GB（建议 {advice.SuggestedDataGb:F0} GB）。不可移动的系统文件挡住了压缩，可先关闭休眠与页面文件后重试，或在磁盘管理中手动处理",
                $"C: can only shrink by {shrinkable / (double)Gb:F0} GB ({advice.SuggestedDataGb:F0} GB suggested). Unmovable system files are in the way — turn off hibernation and the page file and retry, or handle it in Disk Management");
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
            L.S($"{sys.DriveLetter} 单分区 {sys.SizeGb:F2} GB，已用 {sys.UsedGb:F2} GB",
                $"{sys.DriveLetter} is the only partition: {sys.SizeGb:F2} GB, {sys.UsedGb:F2} GB used"),
            L.S($"压缩 {sys.DriveLetter} 到 {layout.NewSystemBytes / (double)Gb:F0} GB，新建 {layout.Letter}: {layout.DataBytes / (double)Gb:F0} GB（NTFS，卷标 Data）",
                $"shrink {sys.DriveLetter} to {layout.NewSystemBytes / (double)Gb:F0} GB and create {layout.Letter}: with {layout.DataBytes / (double)Gb:F0} GB (NTFS, labelled Data)"));
    }

    public override void Apply(TaskContext ctx)
    {
        var layout = Compute(ctx, out var reason) ?? throw new TaskFailedException(
            L.S("前置条件已变化，未执行分区：", "preconditions changed, nothing was partitioned: ") + reason);
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
        ctx.ManualSteps.Add(L.S(
            "分区操作未完成：请打开“磁盘管理”（Win+X → 磁盘管理）检查 C 盘是否已被压缩、是否存在未分配空间，需要时手动新建简单卷并分配盘符。",
            "The partition operation did not finish. Open Disk Management (Win+X → Disk Management) and check whether C: was shrunk and whether unallocated space is left; create a simple volume and assign a letter by hand if needed."));
    }
}

/// <summary>只给分盘建议与手动步骤；单盘无数据分区且未自动分区时出现。</summary>
public sealed class DiskSuggestTask : TaskBase
{
    public const string Id = "disk.suggest";

    public override TaskMetadata Metadata { get; } = new(Id, "disk",
        L.S("分盘建议（手动）", "Partition advice (manual)"),
        L.S("这台电脑只有一个分区。按容量规则给出 C/D 的建议大小与磁盘管理操作步骤，写入报告的手动项。本工具不会自动改动分区。",
            "This PC has a single partition. Gives suggested sizes for C: and D: plus the Disk Management steps, recorded as a manual follow-up. This item never changes partitions by itself."),
        RiskFlags.PromptOnly, Array.Empty<string>(), 91);

    public override bool IsApplicable(EnvironmentSnapshot s, Answers a)
        => s.DataDrive == null && DiskAdvisor.Advise(s).NeedsPartition;

    public override DetectResult Detect(TaskContext ctx)
    {
        var advice = DiskAdvisor.Advise(ctx.Snapshot);
        return new DetectResult(false, L.S("单分区，", "single partition, ") + advice.Reason,
            L.S($"建议 C 约 {advice.SuggestedSystemGb:F0} GB，其余约 {advice.SuggestedDataGb:F0} GB 作为数据盘",
                $"suggest about {advice.SuggestedSystemGb:F0} GB for C: and about {advice.SuggestedDataGb:F0} GB as the data drive"));
    }

    public override void Apply(TaskContext ctx)
    {
        var advice = DiskAdvisor.Advise(ctx.Snapshot);
        var shrinkMb = (long)(advice.SuggestedDataBytes / 1048576d);
        ctx.ManualSteps.Add(L.S(
            $"分盘建议：C 保留约 {advice.SuggestedSystemGb:F0} GB，其余约 {advice.SuggestedDataGb:F0} GB 作为数据盘。" +
            $"步骤：Win+X → 磁盘管理 → 右键 C: → 压缩卷 → “输入压缩空间量”填 {shrinkMb} → 压缩；" +
            "然后右键新出现的“未分配”空间 → 新建简单卷 → 一路默认（NTFS，卷标 Data，盘符 D）。完成后重新运行本工具即可迁移路径。" +
            (advice.Automatable ? string.Empty : $"（未自动执行的原因：{advice.Reason}）"),

            $"Partition advice: keep about {advice.SuggestedSystemGb:F0} GB for C: and use the remaining {advice.SuggestedDataGb:F0} GB as a data drive. " +
            $"Steps: Win+X → Disk Management → right-click C: → Shrink Volume → enter {shrinkMb} as the amount of space to shrink → Shrink; " +
            "then right-click the new Unallocated space → New Simple Volume → accept the defaults (NTFS, labelled Data, letter D). " +
            "Run this tool again afterwards to relocate the paths." +
            (advice.Automatable ? string.Empty : $" (not done automatically because: {advice.Reason})")));
    }

    public override bool Verify(TaskContext ctx) => true;
}

/// <summary>GPU 硬件加速调度（HAGS）。HwSchMode 值不存在说明显卡/驱动不支持，标不适用。</summary>
public sealed class HagsTask : TaskBase
{
    public const string Id = "gpu.hags";
    private const string Key = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string Name = "HwSchMode";

    public override TaskMetadata Metadata { get; } = new(Id, "gpu",
        L.S("开启 GPU 硬件加速调度", "Turn on hardware-accelerated GPU scheduling"),
        L.S("让显卡自行管理显存调度，降低延迟。只在显卡与驱动支持时出现（设置 → 显示 → 显示卡 中有此开关）。重启后生效。",
            "Lets the GPU manage its own memory scheduling, which lowers latency. Only shown when the GPU and driver support it (the same switch lives in Settings → Display → Graphics). Takes effect after a reboot."),
        RiskFlags.Reversible | RiskFlags.AdminOnly | RiskFlags.NeedsReboot, Array.Empty<string>(), 450);

    public override bool IsApplicable(EnvironmentSnapshot s, Answers a) => !s.IsMdmEnrolled;

    public override DetectResult Detect(TaskContext ctx)
    {
        var (v, _) = ctx.Registry.GetValue(RegRoot.LocalMachine, Key, Name);
        if (v == null) return DetectResult.NotApplicableBecause(
            L.S("显卡或驱动不支持硬件加速调度", "the GPU or its driver doesn't support hardware-accelerated scheduling"));
        var on = Convert.ToInt64(v) == 2;
        return new DetectResult(on, on ? L.S("已开启", "on") : L.S("未开启", "off"), L.S("开启", "on"));
    }

    public override void Apply(TaskContext ctx) => ctx.Registry.SetValue(RegRoot.LocalMachine, Key, Name, 2, RegKind.DWord);
}
