using System;
using System.Collections.Generic;
using NewPcSetup.Core.Models;

namespace NewPcSetup.Core.Abstractions;

public enum RegRoot { CurrentUser, LocalMachine }
public enum RegKind { DWord, QWord, String, ExpandString }

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

public interface IFileSystem
{
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    bool IsDirectoryEmpty(string path);
    void DeleteEmptyDirectory(string path);
    /// <summary>递归列出 dir 下最后写入时间早于 before 的文件；目录不存在时返回空。</summary>
    IReadOnlyList<FileEntry> FilesOlderThan(string dir, DateTime before);
    /// <summary>删除单个文件；被占用或无权限时返回 false，不抛出。</summary>
    bool TryDeleteFile(string path);
    bool FileExists(string path);
    /// <summary>读取文本文件；不存在时返回 null。</summary>
    string? ReadAllText(string path);
    /// <summary>写入文本文件（UTF-8 无 BOM）；父目录不存在时创建。</summary>
    void WriteAllText(string path, string content);
    /// <summary>复制文件，覆盖已存在的目标。</summary>
    void CopyFile(string source, string target);
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
}

/// <summary>电源配置。首版只覆盖休眠开关（powercfg /hibernate）。</summary>
public interface IPower
{
    bool IsHibernateEnabled();
    void SetHibernate(bool enabled);
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
