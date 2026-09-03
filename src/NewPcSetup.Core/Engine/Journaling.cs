using System;
using System.Collections.Generic;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Models;

namespace NewPcSetup.Core.Engine;

internal sealed class JournalingRegistry : IRegistry
{
    private readonly IRegistry _inner;
    private readonly IJournal _journal;
    private readonly string _taskId;

    public JournalingRegistry(IRegistry inner, IJournal journal, string taskId) { _inner = inner; _journal = journal; _taskId = taskId; }

    public bool KeyExists(RegRoot root, string key) => _inner.KeyExists(root, key);
    public (object? Value, RegKind? Kind) GetValue(RegRoot root, string key, string name) => _inner.GetValue(root, key, name);
    public IReadOnlyList<string> GetSubKeyNames(RegRoot root, string key) => _inner.GetSubKeyNames(root, key);
    public IReadOnlyList<string> GetValueNames(RegRoot root, string key) => _inner.GetValueNames(root, key);

    public void SetValue(RegRoot root, string key, string name, object value, RegKind kind)
    {
        var (old, oldKind) = _inner.GetValue(root, key, name);
        _journal.Record(new JournalEntry(_taskId, DateTime.Now, JournalKind.Registry,
            $"{root}::{key}::{name}", RegistryValueCodec.Encode(old, oldKind), RegistryValueCodec.Encode(value, kind),
            old == null ? "Missing" : (oldKind ?? RegKind.String).ToString()));
        _inner.SetValue(root, key, name, value, kind);
    }

    public void DeleteValue(RegRoot root, string key, string name)
    {
        var (old, oldKind) = _inner.GetValue(root, key, name);
        if (old == null) return;
        _journal.Record(new JournalEntry(_taskId, DateTime.Now, JournalKind.Registry,
            $"{root}::{key}::{name}", RegistryValueCodec.Encode(old, oldKind), null, (oldKind ?? RegKind.String).ToString()));
        _inner.DeleteValue(root, key, name);
    }
}

internal sealed class JournalingEnvironment : IEnvironment
{
    private readonly IEnvironment _inner;
    private readonly IJournal _journal;
    private readonly string _taskId;

    public JournalingEnvironment(IEnvironment inner, IJournal journal, string taskId) { _inner = inner; _journal = journal; _taskId = taskId; }

    public string? Get(EnvScope scope, string name) => _inner.Get(scope, name);

    public void Set(EnvScope scope, string name, string? value)
    {
        var old = _inner.Get(scope, name);
        _journal.Record(new JournalEntry(_taskId, DateTime.Now, JournalKind.EnvVar, $"{scope}::{name}", old, value));
        _inner.Set(scope, name, value);
    }
}

internal sealed class JournalingShell : IShell
{
    private readonly IShell _inner;
    private readonly IJournal _journal;
    private readonly string _taskId;

    public JournalingShell(IShell inner, IJournal journal, string taskId) { _inner = inner; _journal = journal; _taskId = taskId; }

    public string? GetKnownFolderPath(KnownFolder folder) => _inner.GetKnownFolderPath(folder);

    public void SetKnownFolderPath(KnownFolder folder, string path)
    {
        var old = _inner.GetKnownFolderPath(folder);
        _journal.Record(new JournalEntry(_taskId, DateTime.Now, JournalKind.KnownFolder, folder.ToString(), old, path));
        _inner.SetKnownFolderPath(folder, path);
    }

    public MoveResult MoveContents(string source, string target) => _inner.MoveContents(source, target);
    public void RestartExplorer() => _inner.RestartExplorer();
    public bool IsPinnedToQuickAccess(string path) => _inner.IsPinnedToQuickAccess(path);

    public void PinToQuickAccess(string path)
    {
        if (_inner.IsPinnedToQuickAccess(path)) return;
        _inner.PinToQuickAccess(path);
        _journal.Record(new JournalEntry(_taskId, DateTime.Now, JournalKind.File, "quick_access_pinned", null, path));
    }

    public void UnpinFromQuickAccess(string path)
    {
        if (!_inner.IsPinnedToQuickAccess(path)) return;
        _inner.UnpinFromQuickAccess(path);
        _journal.Record(new JournalEntry(_taskId, DateTime.Now, JournalKind.File, "quick_access_unpinned", path, null));
    }
}

internal sealed class JournalingFileSystem : IFileSystem
{
    private readonly IFileSystem _inner;
    private readonly IJournal _journal;
    private readonly string _taskId;

    public JournalingFileSystem(IFileSystem inner, IJournal journal, string taskId) { _inner = inner; _journal = journal; _taskId = taskId; }

    public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
    public bool IsDirectoryEmpty(string path) => _inner.IsDirectoryEmpty(path);
    public void DeleteEmptyDirectory(string path) => _inner.DeleteEmptyDirectory(path);
    public IReadOnlyList<FileEntry> FilesOlderThan(string dir, DateTime before) => _inner.FilesOlderThan(dir, before);
    /// <summary>文件删除不可撤销，不记 journal；调用方任务不得声明 Reversible。</summary>
    public bool TryDeleteFile(string path) => _inner.TryDeleteFile(path);

    public void CreateDirectory(string path)
    {
        if (_inner.DirectoryExists(path)) return;
        _inner.CreateDirectory(path);
        _journal.Record(new JournalEntry(_taskId, DateTime.Now, JournalKind.File, "dir_created", null, path));
    }
}

internal sealed class JournalingPower : IPower
{
    private readonly IPower _inner;
    private readonly IJournal _journal;
    private readonly string _taskId;

    public JournalingPower(IPower inner, IJournal journal, string taskId) { _inner = inner; _journal = journal; _taskId = taskId; }

    public bool IsHibernateEnabled() => _inner.IsHibernateEnabled();

    public void SetHibernate(bool enabled)
    {
        var old = _inner.IsHibernateEnabled();
        if (old == enabled) return;
        _journal.Record(new JournalEntry(_taskId, DateTime.Now, JournalKind.Power, "hibernate", old ? "1" : "0", enabled ? "1" : "0"));
        _inner.SetHibernate(enabled);
    }
}
