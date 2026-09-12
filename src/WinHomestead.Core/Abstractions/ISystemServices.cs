using System;
using System.Collections.Generic;
using WinHomestead.Core.Models;

namespace WinHomestead.Core.Abstractions;

public enum RegRoot { CurrentUser, LocalMachine }
public enum RegKind { DWord, QWord, String, ExpandString, Binary }

public interface IRegistry
{
    bool KeyExists(RegRoot root, string key);
    (object? Value, RegKind? Kind) GetValue(RegRoot root, string key, string name);
    /// <summary>不存在的键会被创建。</summary>
    void SetValue(RegRoot root, string key, string name, object value, RegKind kind);
    void DeleteValue(RegRoot root, string key, string name);
    IReadOnlyList<string> GetSubKeyNames(RegRoot root, string key);
    IReadOnlyList<string> GetValueNames(RegRoot root, string key);
}

public enum EnvScope { User, Machine }

public interface IEnvironment
{
    string? Get(EnvScope scope, string name);
    /// <summary>value 为 null 时删除该变量。实现负责广播 WM_SETTINGCHANGE。</summary>
    void Set(EnvScope scope, string name, string? value);
}

public enum KnownFolder { Desktop, Documents, Downloads, Pictures, Videos, Music }

public sealed record MoveResult(int Moved, IReadOnlyList<string> Skipped);

public interface IShell
{
    string? GetKnownFolderPath(KnownFolder folder);
    void SetKnownFolderPath(KnownFolder folder, string path);
    /// <summary>移动 source 下全部内容到 target；同名已存在的项跳过并返回。</summary>
    MoveResult MoveContents(string source, string target);
    void RestartExplorer();
    /// <summary>资源管理器“快速访问”固定项。path 需为目录。</summary>
    bool IsPinnedToQuickAccess(string path);
    void PinToQuickAccess(string path);
    void UnpinFromQuickAccess(string path);
}

public sealed record FileEntry(string Path, long SizeBytes);

/// <summary>一次有上限的文件扫描结果。Truncated 表示碰到条数上限或时间预算，结果不完整。</summary>
public sealed record FileScan(IReadOnlyList<FileEntry> Files, bool Truncated)
{
    public long SizeBytes { get { long n = 0; foreach (var f in Files) n += f.SizeBytes; return n; } }
}

public interface IFileSystem
{
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    bool IsDirectoryEmpty(string path);
    void DeleteEmptyDirectory(string path);
    /// <summary>
    /// 递归列出 dir 下最后写入时间早于 before 的文件；目录不存在时返回空。
    /// 命中 maxCount 个或用满 budget 后停止并置 Truncated——临时目录动辄十万个文件，探测阶段不能整目录走一遍。
    /// </summary>
    FileScan FilesOlderThan(string dir, DateTime before, int maxCount, TimeSpan budget);
    /// <summary>删除单个文件；被占用或无权限时返回 false，不抛出。</summary>
    bool TryDeleteFile(string path);
}

public sealed record PartitionInfo(int DiskNumber, int PartitionNumber, string? DriveLetter, long SizeBytes, long OffsetBytes);
public sealed record ShrinkSupport(long SizeMin, long SizeMax);

public sealed class StorageException : Exception
{
    public StorageException(string operation, uint code, string? extendedStatus)
        : base($"{operation} 失败，返回码 {code}{(string.IsNullOrEmpty(extendedStatus) ? string.Empty : "：" + extendedStatus)}")
    {
        Operation = operation; Code = code;
    }
    public string Operation { get; }
    public uint Code { get; }
}

/// <summary>
/// 分区操作（WMI root\Microsoft\Windows\Storage）。全部不可撤销，不经 journal；只有 disk.* 任务使用。
/// 永远不提供删除、移动、合并分区的方法。
/// </summary>
public interface IStorage
{
    PartitionInfo? GetPartition(string driveLetter);
    /// <summary>MSFT_Partition.GetSupportedSize：该分区可缩到的最小尺寸与可扩到的最大尺寸。</summary>
    ShrinkSupport GetSupportedSize(PartitionInfo partition);
    void Resize(PartitionInfo partition, long newSizeBytes);
    /// <summary>在磁盘剩余的最大连续空闲空间上新建基本数据分区并分配盘符。</summary>
    PartitionInfo CreatePartitionUsingMaximumSize(int diskNumber, char driveLetter);
    void FormatNtfs(PartitionInfo partition, string label);
    IReadOnlyList<string> UsedDriveLetters();
    /// <summary>卷的文件系统名（NTFS / ReFS 等）；读不到返回 null。</summary>
    string? FileSystemOf(string driveLetter);
    /// <summary>磁盘上最大的一块连续空闲空间（MSFT_Disk.LargestFreeExtent）；读不到返回 0。</summary>
    long LargestFreeExtent(int diskNumber);
}

/// <summary>当前电源方案的四个空闲超时，单位秒。0 表示"从不"。</summary>
public sealed record PowerTimeouts(int MonitorAcSeconds, int MonitorDcSeconds, int StandbyAcSeconds, int StandbyDcSeconds)
{
    public int MonitorAcMinutes => MonitorAcSeconds / 60;
    public int StandbyAcMinutes => StandbyAcSeconds / 60;
}

/// <summary>电源配置：休眠开关与当前方案的空闲超时。</summary>
public interface IPower
{
    bool IsHibernateEnabled();
    void SetHibernate(bool enabled);
    /// <summary>读当前活动电源方案的四个超时；读不到返回 null。</summary>
    PowerTimeouts? ReadTimeouts();
    /// <summary>只改交流电源（插电）下的关屏与睡眠超时，单位分钟；0 表示从不。电池侧不动。</summary>
    void SetAcTimeouts(int monitorMinutes, int standbyMinutes);
}

/// <summary>主显示器当前的分辨率与刷新率，以及同一分辨率下驱动报告的最高刷新率。</summary>
public sealed record DisplayMode(int Width, int Height, int Hz, int MaxHz);

/// <summary>显示模式。只碰主显示器的刷新率，不改分辨率与缩放。</summary>
public interface IDisplay
{
    /// <summary>读不到返回 null。</summary>
    DisplayMode? Primary();
    /// <summary>把主显示器切到指定刷新率并写进注册表持久化；分辨率与色深保持不变。</summary>
    void SetRefreshRate(int hz);
}

public interface IJournal
{
    void Record(JournalEntry entry);
    IReadOnlyList<JournalEntry> EntriesFor(string taskId);
    IReadOnlyList<JournalEntry> All { get; }
}

public interface ILogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}

public interface ISystemRestore
{
    /// <summary>返回 false 表示系统拒绝（如系统保护关闭）。24 小时内已有还原点被系统跳过时视为成功。</summary>
    bool CreateRestorePoint(string description);
}
