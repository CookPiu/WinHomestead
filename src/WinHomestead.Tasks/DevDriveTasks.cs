using System;
using WinHomestead.Core.Abstractions;
using WinHomestead.Core.Engine;
using WinHomestead.Core.Infrastructure;
using WinHomestead.Core.Models;

namespace WinHomestead.Tasks;

/// <summary>
/// Dev Drive 建议。Dev Drive 是 ReFS 卷加信任标记，Defender 在其上跑 performance mode，
/// 适合放源码、包缓存与编译产物。只能在格式化时指定：现有的 NTFS 数据盘无法就地转换，
/// 所以这一条只给建议与步骤，永远不动已有分区里的数据。
/// </summary>
public sealed class DevDriveSuggestTask : TaskBase
{
    public const string Id = "disk.devdrive";
    private const long Gb = 1L << 30;

    /// <summary>官方要求：卷至少 50 GB、内存最低 8 GB、Build 10.0.22621.2338 及以上。</summary>
    private const long MinVolumeBytes = 50 * Gb;
    private const long MinRamBytes = 8 * Gb;
    private const int MinBuild = 22621;

    public override TaskMetadata Metadata { get; } = new(Id, "disk",
        L.S("Dev Drive 建议（手动）", "Dev Drive advice (manual)"),
        L.S("Dev Drive 是微软为开发负载提供的 ReFS 卷：Defender 在它上面跑 performance mode（异步扫描）而不是实时扫描，" +
            "源码、包缓存、编译中间产物放在这里读写更快。只能在格式化时指定，已有数据的 NTFS 卷不能就地转换，" +
            "所以本工具只给出建议与步骤，不会格式化任何卷。开发工具与 SDK 本体仍应装在系统盘。",
            "A Dev Drive is Microsoft's ReFS volume for development work: Defender runs in performance mode (async scanning) " +
            "rather than real-time, so source trees, package caches and build output are faster to work with. It can only be " +
            "designated at format time — an NTFS volume with data on it cannot be converted in place — so this item only gives " +
            "advice and steps, and never formats anything. Dev tools and SDKs themselves still belong on the system drive."),
        RiskFlags.PromptOnly, Array.Empty<string>(), 92);

    /// <summary>内存或系统版本不够时这台机器就不该用 Dev Drive，直接不列出；MDM 只是限制，仍然列出并在 Detect 里说明原因。</summary>
    public override bool IsApplicable(EnvironmentSnapshot s, Answers a)
        => s.Build >= MinBuild && s.RamBytes >= MinRamBytes && HasDataDrive(s, a);

    /// <summary>属于可选优化，不标推荐。</summary>
    public override bool DefaultChecked(EnvironmentSnapshot s, Answers a) => false;

    private static string DataDriveLetter(TaskContext ctx)
        => (ctx.Answers.DataDrive ?? ctx.Snapshot.DataDrive ?? ctx.Snapshot.SystemDrive).TrimEnd('\\');

    /// <summary>
    /// Windows 11 客户端只有创建 Dev Drive 这一条路径能格式化出 ReFS 卷，所以这里用文件系统名做判定。
    /// 信任状态另有 fsutil devdrv query 可查，需要管理员，不在探测阶段做。
    /// </summary>
    private static bool IsRefs(string? fileSystem)
        => fileSystem != null && fileSystem.IndexOf("ReFS", StringComparison.OrdinalIgnoreCase) >= 0;

    public override DetectResult Detect(TaskContext ctx)
    {
        var drive = DataDriveLetter(ctx);
        var fs = ctx.Storage.FileSystemOf(drive);
        if (fs == null) return DetectResult.NotApplicableBecause(
            L.S($"读不到 {drive} 的文件系统", $"can't read the file system of {drive}"));
        if (IsRefs(fs))
            return new DetectResult(true,
                L.S($"{drive} 是 ReFS 卷", $"{drive} is a ReFS volume"),
                L.S("已经是 Dev Drive", "already a Dev Drive"),
                null, L.S($"FileSystem={fs}；信任状态可用 fsutil devdrv query {drive} 自查",
                          $"FileSystem={fs}; check trust status yourself with fsutil devdrv query {drive}"));

        if (ctx.Snapshot.IsMdmEnrolled)
            return DetectResult.NotApplicableBecause(L.S("这台机器受企业策略管控，Dev Drive 要由管理员在组策略里放开才能创建",
                "this PC is under enterprise policy; an administrator has to allow Dev Drive in group policy first"));

        var part = ctx.Storage.GetPartition(drive);
        var free = part == null ? 0 : ctx.Storage.LargestFreeExtent(part.DiskNumber);
        var target = free >= MinVolumeBytes
            ? L.S($"用磁盘上剩余的 {free / (double)Gb:F0} GB 未分配空间新建一个 Dev Drive",
                  $"create a Dev Drive from the {free / (double)Gb:F0} GB of unallocated space on this disk")
            : L.S($"在 {drive} 上新建一个至少 {MinVolumeBytes / Gb} GB 的 VHDX 作为 Dev Drive",
                  $"create a VHDX of at least {MinVolumeBytes / Gb} GB on {drive} and use it as a Dev Drive");
        return new DetectResult(false,
            L.S($"{drive} 是 {fs}，不是 Dev Drive", $"{drive} is {fs}, not a Dev Drive"), target,
            null, L.S($"最大连续空闲空间 {free / (double)Gb:F1} GB", $"largest free extent {free / (double)Gb:F1} GB"));
    }

    public override void Apply(TaskContext ctx)
    {
        var drive = DataDriveLetter(ctx);
        var part = ctx.Storage.GetPartition(drive);
        var free = part == null ? 0 : ctx.Storage.LargestFreeExtent(part.DiskNumber);
        var how = free >= MinVolumeBytes
            ? L.S($"磁盘上还有 {free / (double)Gb:F0} GB 未分配空间，可直接“创建卷 → 创建开发驱动器”。",
                  $"There is {free / (double)Gb:F0} GB of unallocated space on the disk, so use Create volume → Create Dev Drive. ")
            : L.S($"磁盘没有足够的未分配空间，选“新建 VHD”，位置放在 {drive} 下，格式选 VHDX、动态扩展，大小至少 {MinVolumeBytes / Gb} GB。",
                  $"The disk has no spare unallocated space, so choose Create new VHD, put it under {drive}, pick VHDX with dynamic expansion, and give it at least {MinVolumeBytes / Gb} GB. ");

        ctx.ManualSteps.Add(L.S(
            "Dev Drive 建议：设置 → 系统 → 存储 → 高级存储设置 → 磁盘和卷 → 创建开发驱动器。" + how +
            $"注意 {drive} 已有数据，不能就地转成 Dev Drive——重新格式化会清空整个卷，本工具不会这么做。" +
            "建好之后把源码仓库与包缓存放进去（缓存目录可在“预设开发缓存目录”里把根目录改成新盘符），" +
            "开发工具与 SDK 本体仍然装在系统盘。" +
            "可用 fsutil devdrv query <盘符>: 确认卷已被标记为受信任。" +
            "如果之后要把 TEMP/TMP 也指到 Dev Drive，需要先放行 WinSetupMon 过滤器（fsutil devdrv setfiltersallowed WinSetupMon），否则会影响 Windows 升级。",

            "Dev Drive: Settings → System → Storage → Advanced storage settings → Disks & volumes → Create dev drive. " + how +
            $"Note that {drive} already holds data and cannot be converted in place — reformatting would wipe the whole volume, which this tool will not do. " +
            "Once it exists, put source repositories and package caches on it (you can point the cache root at the new drive letter " +
            "from the \"Preset dev cache locations\" item). Dev tools and SDKs themselves stay on the system drive. " +
            "Use fsutil devdrv query <letter>: to confirm the volume is marked trusted. " +
            "If you later point TEMP/TMP at the Dev Drive, allow the WinSetupMon filter first (fsutil devdrv setfiltersallowed WinSetupMon), " +
            "otherwise Windows upgrades are affected."));
    }

    public override bool Verify(TaskContext ctx) => true;
}
