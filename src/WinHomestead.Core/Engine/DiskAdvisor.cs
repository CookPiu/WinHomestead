using System;
using System.Collections.Generic;
using System.Linq;
using WinHomestead.Core.Models;
using WinHomestead.Core.Infrastructure;

namespace WinHomestead.Core.Engine;

/// <summary>
/// 分盘建议（《01》3.1 规则表）。纯函数：只看 Snapshot，不碰系统。
/// Automatable 只表示静态前置条件满足；可压缩量要在任务 Detect 里用 IStorage 再查一次。
/// </summary>
public sealed record PartitionAdvice(
    bool NeedsPartition,          // 单盘且无非系统分区
    long SuggestedSystemBytes,    // 建议 C 大小（0 表示不建议分区）
    long SuggestedDataBytes,      // 建议 D 大小
    bool Automatable,             // 全新机、C 已用 < 80 GB、非 MDM
    string Reason)
{
    public double SuggestedSystemGb => SuggestedSystemBytes / 1073741824d;
    public double SuggestedDataGb => SuggestedDataBytes / 1073741824d;
}

public static class DiskAdvisor
{
    private const long Gb = 1L << 30;
    public const long MaxUsedForAutomation = 80 * Gb;
    /// <summary>可压缩量至少要达到建议 D 的这个比例才提供一键执行。</summary>
    public const double MinShrinkRatio = 0.8;

    public static PartitionAdvice Advise(EnvironmentSnapshot s)
    {
        var sys = s.Volumes.FirstOrDefault(v => v.IsSystem);
        if (sys == null) return new PartitionAdvice(false, 0, 0, false, L.S("未找到系统卷", "no system volume found"));
        if (s.Volumes.Any(v => !v.IsSystem)) return new PartitionAdvice(false, 0, 0, false,
            L.S("已有数据分区，不动分区", "a data partition already exists; partitions are left alone"));
        if (s.Disks.Count != 1) return new PartitionAdvice(false, 0, 0, false,
            L.S("多块物理盘：请在磁盘管理中把非系统盘整块建为数据盘",
                "several physical disks: use Disk Management to make the non-system disk one big data volume"));

        var disk = s.Disks[0].SizeBytes;
        var systemTarget = SuggestSystemSize(disk);
        if (systemTarget == 0) return new PartitionAdvice(false, 0, 0, false,
            L.S("磁盘不超过 256 GB，不建议分区", "the disk is 256 GB or smaller; splitting it isn't worth it"));

        var dataTarget = sys.SizeBytes - systemTarget;
        if (dataTarget < 64 * Gb) return new PartitionAdvice(false, 0, 0, false,
            L.S("系统卷容量不足以切出有意义的数据盘", "the system volume is too small to carve out a useful data drive"));

        var used = sys.SizeBytes - sys.FreeBytes;
        string? block = null;
        if (s.IsMdmEnrolled) block = L.S("此电脑受组织管理", "this PC is managed by an organization");
        else if (!s.IsFreshInstall) block = L.S("系统安装已超过 30 天", "Windows was installed more than 30 days ago");
        else if (used > MaxUsedForAutomation) block = L.S($"系统盘已用 {used / (double)Gb:F0} GB，超过 80 GB",
            $"the system drive already holds {used / (double)Gb:F0} GB, over the 80 GB limit");
        return new PartitionAdvice(true, systemTarget, dataTarget, block == null,
            block == null
                ? L.S("满足一键分区的静态条件", "meets the static preconditions for one-click partitioning")
                : block + L.S("，只给建议，不自动执行", " — advice only, nothing is done automatically"));
    }

    /// <summary>按物理盘容量给 C 盘建议值：512 GB → 176 GB；1 TB → 240 GB；2 TB+ → 288 GB；≤256 GB → 0（不分区）。</summary>
    public static long SuggestSystemSize(long diskBytes)
    {
        if (diskBytes <= 256 * Gb + 8 * Gb) return 0;
        if (diskBytes <= 512 * Gb + 32 * Gb) return 176 * Gb;
        if (diskBytes <= 1024 * Gb + 64 * Gb) return 240 * Gb;
        return 288 * Gb;
    }

    /// <summary>首选 D，被占用则 E、F…；跳过 A/B 与 C。</summary>
    public static char NextFreeDriveLetter(IEnumerable<string> usedRoots, char preferred = 'D')
    {
        var used = new HashSet<char>(usedRoots.Where(r => r.Length > 0).Select(r => char.ToUpperInvariant(r[0])));
        for (var c = preferred; c <= 'Z'; c++) if (!used.Contains(c)) return c;
        throw new InvalidOperationException("没有可用盘符");
    }
}
