using System;
using System.Collections.Generic;
using System.Linq;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Engine;
using NewPcSetup.Core.Infrastructure;
using NewPcSetup.Core.Models;

namespace NewPcSetup.Core.Tests;

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
    public bool IsDirectoryEmpty(string path) => true;
    public void DeleteEmptyDirectory(string path) => Dirs.Remove(path);
    /// <summary>键为目录，值为其中“旧文件”列表；TryDeleteFile 对 Locked 中的路径返回 false。</summary>
    public readonly Dictionary<string, List<FileEntry>> OldFiles = new(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> Locked = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<string> Deleted = new();
    public IReadOnlyList<FileEntry> FilesOlderThan(string dir, DateTime before) => OldFiles.TryGetValue(dir, out var l) ? l.ToList() : new List<FileEntry>();
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
        OsCaption: "Windows 11 Professional 25H2", DisplayVersion: "25H2", Build: 26200, IsLaptop: isLaptop, RamGb: 32,
        Manufacturer: "Test", Model: "T1", IsMdmEnrolled: mdm, IsDomainJoined: false, ProxyEnabled: false,
        Disks: new[] { new DiskInfo(0, "SSD", 1_000_000_000_000, "NVMe") },
        Volumes: dataDrive == null
            ? new[] { new VolumeInfo("C:", "Windows", 500L << 30, 100L << 30, true) }
            : new[] { new VolumeInfo("C:", "Windows", 250L << 30, 50L << 30, true), new VolumeInfo(dataDrive, "Data", 450L << 30, 140L << 30, false) },
        SystemDrive: "C:", DataDrive: dataDrive, InstallDate: DateTime.Now.AddDays(-2),
        OneDrive: new OneDriveInfo(true, true, desktopProtected ? 512 : 0), Gpu: new GpuInfo(Array.Empty<string>(), false),
        KnownFolders: new Dictionary<string, string>(), UserEnvironment: new Dictionary<string, string>(),
        Tools: tools.Select(t => new DetectedTool(t, t, true, null)).ToList(),
        LargeItems: Array.Empty<LargeItem>(), TakenAt: DateTime.Now);

    public static Answers Answers(EnvironmentSnapshot s, UiStyle style = UiStyle.Win11Default, bool promo = false, ImeMode ime = ImeMode.ChineseDefault, bool keepShift = true)
        => new(Usage.Dev, s.DataDrive, false, style, false, ime, keepShift, promo);

    public static (ExecutionServices Services, FakeRegistry Reg, FakeEnvironment Env, FakeShell Shell, FakeFileSystem Fs) Services()
    {
        var (svc, reg, env, shell, fs, _) = ServicesWithPower();
        return (svc, reg, env, shell, fs);
    }

    public static (ExecutionServices Services, FakeRegistry Reg, FakeEnvironment Env, FakeShell Shell, FakeFileSystem Fs, FakePower Power) ServicesWithPower()
    {
        var reg = new FakeRegistry(); var env = new FakeEnvironment(); var shell = new FakeShell(); var fs = new FakeFileSystem(); var power = new FakePower();
        return (new ExecutionServices(reg, env, shell, fs, power, NullLogger.Instance), reg, env, shell, fs, power);
    }
}
