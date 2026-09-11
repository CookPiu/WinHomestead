using System;
using System.Collections.Generic;
using System.Linq;
using WinHomestead.Core.Abstractions;
using WinHomestead.Core.Engine;
using WinHomestead.Core.Infrastructure;
using WinHomestead.Core.Models;

namespace WinHomestead.Core.Tests;

public sealed class FakeRegistry : IRegistry
{
    private readonly Dictionary<string, (object Value, RegKind Kind)> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

    private static string K(RegRoot r, string k) => $"{r}|{k}";
    private static string V(RegRoot r, string k, string n) => $"{r}|{k}|{n}";

    public void Seed(RegRoot root, string key, string name, object value, RegKind kind) { _keys.Add(K(root, key)); _values[V(root, key, name)] = (value, kind); }

    public bool KeyExists(RegRoot root, string key) => _keys.Contains(K(root, key));
    public (object? Value, RegKind? Kind) GetValue(RegRoot root, string key, string name)
        => _values.TryGetValue(V(root, key, name), out var v) ? (v.Value, v.Kind) : (null, null);
    public void SetValue(RegRoot root, string key, string name, object value, RegKind kind) { _keys.Add(K(root, key)); _values[V(root, key, name)] = (value, kind); }
    public void DeleteValue(RegRoot root, string key, string name) => _values.Remove(V(root, key, name));
    public IReadOnlyList<string> GetSubKeyNames(RegRoot root, string key)
    {
        var prefix = K(root, key) + "\\";
        return _keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(k => k.Substring(prefix.Length).Split('\\')[0]).Distinct().ToList();
    }
    public IReadOnlyList<string> GetValueNames(RegRoot root, string key)
    {
        var prefix = K(root, key) + "|";
        return _values.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(k => k.Substring(prefix.Length)).ToList();
    }
}

public sealed class FakeEnvironment : IEnvironment
{
    public readonly Dictionary<string, string> User = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> Machine = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> D(EnvScope s) => s == EnvScope.User ? User : Machine;
    public string? Get(EnvScope scope, string name) => D(scope).TryGetValue(name, out var v) ? v : null;
    public void Set(EnvScope scope, string name, string? value) { if (value == null) D(scope).Remove(name); else D(scope)[name] = value; }
}

public sealed class FakeShell : IShell
{
    public readonly Dictionary<KnownFolder, string> Folders = new();
    public readonly List<(string, string)> Moves = new();
    public bool FailMove;
    public int ExplorerRestarts;
    public string? GetKnownFolderPath(KnownFolder folder) => Folders.TryGetValue(folder, out var p) ? p : null;
    public void SetKnownFolderPath(KnownFolder folder, string path) => Folders[folder] = path;
    public MoveResult MoveContents(string source, string target)
    {
        if (FailMove) throw new InvalidOperationException("move failed");
        Moves.Add((source, target));
        return new MoveResult(1, Array.Empty<string>());
    }
    public void RestartExplorer() => ExplorerRestarts++;
    public readonly HashSet<string> Pinned = new(StringComparer.OrdinalIgnoreCase);
    public bool IsPinnedToQuickAccess(string path) => Pinned.Contains(path);
    public void PinToQuickAccess(string path) => Pinned.Add(path);
    public void UnpinFromQuickAccess(string path) => Pinned.Remove(path);
}

public sealed class FakeStorage : IStorage
{
    public PartitionInfo? System = new(0, 2, "C:", 500L << 30, 1L << 30);
    public long SizeMin = 100L << 30;
    public readonly List<string> Calls = new();
    public readonly List<string> Letters = new() { "C:" };
    /// <summary>额外的盘符 → 分区；查不到时只有 C: 有分区信息。</summary>
    public readonly Dictionary<string, PartitionInfo> Partitions = new(StringComparer.OrdinalIgnoreCase);
    public PartitionInfo? GetPartition(string driveLetter)
        => Partitions.TryGetValue(driveLetter, out var p) ? p
           : string.Equals(driveLetter, "C:", StringComparison.OrdinalIgnoreCase) ? System : null;
    public ShrinkSupport GetSupportedSize(PartitionInfo p) => new(SizeMin, p.SizeBytes);
    public void Resize(PartitionInfo p, long newSizeBytes) { Calls.Add($"resize:{newSizeBytes >> 30}"); System = p with { SizeBytes = newSizeBytes }; }
    public PartitionInfo CreatePartitionUsingMaximumSize(int diskNumber, char driveLetter) { Calls.Add($"create:{driveLetter}"); Letters.Add(driveLetter + ":"); return new PartitionInfo(diskNumber, 3, driveLetter + ":", 1, 1); }
    public void FormatNtfs(PartitionInfo p, string label) => Calls.Add($"format:{p.DriveLetter}:{label}");
    public IReadOnlyList<string> UsedDriveLetters() => Letters;
    /// <summary>盘符 → 文件系统名；没放进来的按 NTFS 处理。</summary>
    public readonly Dictionary<string, string> FileSystems = new(StringComparer.OrdinalIgnoreCase);
    public string? FileSystemOf(string driveLetter) => FileSystems.TryGetValue(driveLetter, out var fs) ? fs : "NTFS";
    public long FreeExtent;
    public long LargestFreeExtent(int diskNumber) => FreeExtent;
}

public sealed class FakePower : IPower
{
    public bool Hibernate = true;
    public int SetCalls;
    public bool IsHibernateEnabled() => Hibernate;
    public void SetHibernate(bool enabled) { Hibernate = enabled; SetCalls++; }
}

public sealed class FakeFileSystem : IFileSystem
{
    public readonly HashSet<string> Dirs = new(StringComparer.OrdinalIgnoreCase);
    public bool DirectoryExists(string path) => Dirs.Contains(path);
    public void CreateDirectory(string path) => Dirs.Add(path);
    /// <summary>默认视为空目录；放进 NonEmptyDirs 的按非空处理。</summary>
    public readonly HashSet<string> NonEmptyDirs = new(StringComparer.OrdinalIgnoreCase);
    public bool IsDirectoryEmpty(string path) => !NonEmptyDirs.Contains(path);
    public void DeleteEmptyDirectory(string path) => Dirs.Remove(path);
    /// <summary>键为目录，值为其中“旧文件”列表；TryDeleteFile 对 Locked 中的路径返回 false。</summary>
    public readonly Dictionary<string, List<FileEntry>> OldFiles = new(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> Locked = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<string> Deleted = new();
    /// <summary>按 maxCount 截断，budget 在假实现里不起作用。</summary>
    public FileScan FilesOlderThan(string dir, DateTime before, int maxCount, TimeSpan budget)
    {
        if (!OldFiles.TryGetValue(dir, out var l)) return new FileScan(new List<FileEntry>(), false);
        var taken = l.Take(maxCount).ToList();
        return new FileScan(taken, taken.Count < l.Count);
    }
    public bool TryDeleteFile(string path)
    {
        if (Locked.Contains(path)) return false;
        Deleted.Add(path);
        foreach (var l in OldFiles.Values) l.RemoveAll(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
        return true;
    }

}

public static class TestData
{
    public static EnvironmentSnapshot Snapshot(string? dataDrive = "D:", bool desktopProtected = false, params string[] tools)
        => Snapshot(dataDrive, desktopProtected, isLaptop: true, mdm: false, tools);

    public static EnvironmentSnapshot Snapshot(string? dataDrive, bool desktopProtected, bool isLaptop, bool mdm, params string[] tools) => new(
        OsCaption: "Windows 11 Professional 25H2", DisplayVersion: "25H2", Build: 26200, IsLaptop: isLaptop,
        RamBytes: 34_270_404_608L, Cpu: new CpuInfo("Test CPU", 8, 16),
        Manufacturer: "Test", Model: "T1", IsMdmEnrolled: mdm, IsDomainJoined: false, ProxyEnabled: false,
        Disks: new[] { new DiskInfo(0, "SSD", 1_000_000_000_000, "NVMe") },
        Volumes: dataDrive == null
            ? new[] { new VolumeInfo("C:", "Windows", 500L << 30, 100L << 30, true) }
            : new[] { new VolumeInfo("C:", "Windows", 250L << 30, 50L << 30, true), new VolumeInfo(dataDrive, "Data", 450L << 30, 140L << 30, false) },
        SystemDrive: "C:", DataDrive: dataDrive, InstallDate: DateTime.Now.AddDays(-2),
        OneDrive: new OneDriveInfo(true, true, desktopProtected ? 512 : 0), Gpu: new GpuInfo(Array.Empty<string>(), false),
        KnownFolders: new Dictionary<string, string>(), UserEnvironment: new Dictionary<string, string>(),
        Tools: tools.Select(t => new DetectedTool(t, t, true, null)).ToList(),
        TakenAt: DateTime.Now);

    public static Answers Answers(EnvironmentSnapshot s) => new(s.DataDrive, false);

    /// <summary>单盘无数据分区的全新机：1 TB 盘，C 500 GB 已用 40 GB。</summary>
    public static EnvironmentSnapshot SingleDiskFresh(long diskBytes = 1000L << 30, long usedBytes = 40L << 30, bool mdm = false, int installedDaysAgo = 2)
    {
        var s = Snapshot(dataDrive: null, false, isLaptop: false, mdm: mdm);
        var size = 500L << 30;
        return s with
        {
            Disks = new[] { new DiskInfo(0, "NVMe", diskBytes, "NVMe") },
            Volumes = new[] { new VolumeInfo("C:", "Windows", size, size - usedBytes, true) },
            InstallDate = DateTime.Now.AddDays(-installedDaysAgo),
        };
    }

    public static (ExecutionServices Services, FakeRegistry Reg, FakeEnvironment Env, FakeShell Shell, FakeFileSystem Fs) Services()
    {
        var (svc, reg, env, shell, fs, _) = ServicesWithPower();
        return (svc, reg, env, shell, fs);
    }

    public static (ExecutionServices Services, FakeRegistry Reg, FakeEnvironment Env, FakeShell Shell, FakeFileSystem Fs, FakePower Power) ServicesWithPower()
    {
        var (svc, reg, env, shell, fs, power, _) = ServicesFull();
        return (svc, reg, env, shell, fs, power);
    }

    public static (ExecutionServices Services, FakeRegistry Reg, FakeEnvironment Env, FakeShell Shell, FakeFileSystem Fs, FakePower Power, FakeStorage Storage) ServicesFull()
    {
        var reg = new FakeRegistry(); var env = new FakeEnvironment(); var shell = new FakeShell(); var fs = new FakeFileSystem(); var power = new FakePower(); var storage = new FakeStorage();
        return (new ExecutionServices(reg, env, shell, fs, power, storage, NullLogger.Instance), reg, env, shell, fs, power, storage);
    }
}
