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
