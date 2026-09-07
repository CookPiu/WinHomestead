using System;
using System.Collections.Generic;

namespace WinHomestead.Core.Models;

public sealed record DiskInfo(int Number, string Model, long SizeBytes, string InterfaceType)
{
    public double SizeGb => SizeBytes / 1073741824d;
}

public sealed record CpuInfo(string Name, int Cores, int LogicalProcessors);

public sealed record VolumeInfo(string DriveLetter, string Label, long SizeBytes, long FreeBytes, bool IsSystem)
{
    public double SizeGb => SizeBytes / 1073741824d;
    public double FreeGb => FreeBytes / 1073741824d;
    public long UsedBytes => SizeBytes - FreeBytes;
    public double UsedGb => UsedBytes / 1073741824d;
}

/// <summary>KfmProtectedMask 位含义为本机观测推断：512 = 桌面。其余位待更多样本验证。</summary>
public sealed record OneDriveInfo(bool Installed, bool SignedIn, int KfmProtectedMask)
{
    public bool DesktopProtected => (KfmProtectedMask & 512) != 0;
}

public sealed record GpuInfo(IReadOnlyList<string> Names, bool HagsEnabled);

public sealed record DetectedTool(string Id, string Name, bool Installed, string? ConfiguredPath);

public sealed record EnvironmentSnapshot(
    string OsCaption,
    string DisplayVersion,
    int Build,
    bool IsLaptop,
    long RamBytes,
    CpuInfo Cpu,
    string Manufacturer,
    string Model,
    bool IsMdmEnrolled,
    bool IsDomainJoined,
    bool ProxyEnabled,
    IReadOnlyList<DiskInfo> Disks,
    IReadOnlyList<VolumeInfo> Volumes,
    string SystemDrive,
    string? DataDrive,
    DateTime InstallDate,
    OneDriveInfo OneDrive,
    GpuInfo Gpu,
    IReadOnlyDictionary<string, string> KnownFolders,
    IReadOnlyDictionary<string, string> UserEnvironment,
    IReadOnlyList<DetectedTool> Tools,
    DateTime TakenAt)
{
    /// <summary>与资源管理器一致按 GiB 折算；展示时不要再取整，需要精确值时用 RamBytes。</summary>
    public double RamGb => RamBytes / 1073741824d;

    public bool IsWindows11 => Build >= 22000;
    public bool IsFreshInstall => (TakenAt - InstallDate).TotalDays <= 30;

    public bool IsOnSystemDrive(string? path)
        => !string.IsNullOrEmpty(path) && path!.StartsWith(SystemDrive, StringComparison.OrdinalIgnoreCase);

    public bool HasTool(string id)
    {
        foreach (var t in Tools)
            if (t.Installed && string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
