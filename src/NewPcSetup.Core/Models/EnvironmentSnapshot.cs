using System;
using System.Collections.Generic;

namespace NewPcSetup.Core.Models;

public sealed record DiskInfo(int Number, string Model, long SizeBytes, string InterfaceType);

public sealed record VolumeInfo(string DriveLetter, string Label, long SizeBytes, long FreeBytes, bool IsSystem)
{
    public double SizeGb => SizeBytes / 1073741824d;
    public double FreeGb => FreeBytes / 1073741824d;
}

/// <summary>KfmProtectedMask 位含义为本机观测推断：512 = 桌面。其余位待更多样本验证。</summary>
public sealed record OneDriveInfo(bool Installed, bool SignedIn, int KfmProtectedMask)
{
    public bool DesktopProtected => (KfmProtectedMask & 512) != 0;
}

public sealed record GpuInfo(IReadOnlyList<string> Names, bool HagsEnabled);

public sealed record DetectedTool(string Id, string Name, bool Installed, string? ConfiguredPath);

public sealed record LargeItem(string Path, long SizeBytes, string Category)
{
    public double SizeGb => SizeBytes / 1073741824d;
}

public sealed record EnvironmentSnapshot(
    string OsCaption,
    string DisplayVersion,
    int Build,
    bool IsLaptop,
    int RamGb,
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
    IReadOnlyList<LargeItem> LargeItems,
    DateTime TakenAt)
{
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
