using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Models;

namespace NewPcSetup.Core.Engine;

/// <summary>原生服务集合。Runner 与 Planner 用它为每个任务创建 TaskContext。</summary>
public sealed class ExecutionServices
{
    public ExecutionServices(IRegistry registry, IEnvironment environment, IShell shell, IFileSystem fileSystem, IPower power, IStorage storage, ILogger logger)
    {
        Registry = registry; Environment = environment; Shell = shell; FileSystem = fileSystem; Power = power; Storage = storage; Logger = logger;
    }

    public IRegistry Registry { get; }
    public IEnvironment Environment { get; }
    public IShell Shell { get; }
    public IFileSystem FileSystem { get; }
    public IPower Power { get; }
    public IStorage Storage { get; }
    public ILogger Logger { get; }

    public TaskContext CreateContext(string taskId, EnvironmentSnapshot snapshot, Answers answers, IJournal journal, CancellationToken ct)
        => new(taskId, snapshot, answers, journal, ct, this);
}

public sealed class TaskContext
{
    private readonly ExecutionServices _raw;

    internal TaskContext(string taskId, EnvironmentSnapshot snapshot, Answers answers, IJournal journal, CancellationToken ct, ExecutionServices raw)
    {
        _raw = raw;
        TaskId = taskId;
        Snapshot = snapshot;
        Answers = answers;
        Journal = journal;
        Cancellation = ct;
        Registry = new JournalingRegistry(raw.Registry, journal, taskId);
        Environment = new JournalingEnvironment(raw.Environment, journal, taskId);
        Shell = new JournalingShell(raw.Shell, journal, taskId);
        FileSystem = new JournalingFileSystem(raw.FileSystem, journal, taskId);
        Power = new JournalingPower(raw.Power, journal, taskId);
        Storage = raw.Storage;
        Log = raw.Logger;
    }

    public string TaskId { get; }
    public EnvironmentSnapshot Snapshot { get; }
    public Answers Answers { get; }
    public IJournal Journal { get; }
    public CancellationToken Cancellation { get; }
    public IRegistry Registry { get; }
    public IEnvironment Environment { get; }
    public IShell Shell { get; }
    public IFileSystem FileSystem { get; }
    public IPower Power { get; }
    /// <summary>分区操作不可撤销，不经 journal。</summary>
    public IStorage Storage { get; }
    public ILogger Log { get; }
    public IList<string> ManualSteps { get; } = new List<string>();

    /// <summary>按 journal 逆序撤销，直接写原始服务，不再记录。</summary>
    public void Undo(IReadOnlyList<JournalEntry> entries)
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            var e = entries[i];
            switch (e.Kind)
            {
                case JournalKind.Registry:
                    UndoRegistry(e);
                    break;
                case JournalKind.EnvVar:
                {
                    var parts = e.Key.Split(new[] { "::" }, 2, StringSplitOptions.None);
                    var scope = (EnvScope)Enum.Parse(typeof(EnvScope), parts[0]);
                    _raw.Environment.Set(scope, parts[1], e.OldValue);
                    break;
                }
                case JournalKind.KnownFolder:
                    if (e.OldValue != null)
                        _raw.Shell.SetKnownFolderPath((KnownFolder)Enum.Parse(typeof(KnownFolder), e.Key), e.OldValue);
                    break;
                case JournalKind.File:
                    if (e.Key == "dir_created" && e.NewValue != null && _raw.FileSystem.DirectoryExists(e.NewValue) && _raw.FileSystem.IsDirectoryEmpty(e.NewValue))
                        _raw.FileSystem.DeleteEmptyDirectory(e.NewValue);
                    else if (e.Key == "quick_access_pinned" && e.NewValue != null && _raw.Shell.IsPinnedToQuickAccess(e.NewValue))
                        _raw.Shell.UnpinFromQuickAccess(e.NewValue);
                    else if (e.Key == "quick_access_unpinned" && e.OldValue != null && !_raw.Shell.IsPinnedToQuickAccess(e.OldValue))
                        _raw.Shell.PinToQuickAccess(e.OldValue);
                    break;
                case JournalKind.Power:
                    if (e.Key == "hibernate" && e.OldValue != null)
                        _raw.Power.SetHibernate(e.OldValue == "1");
                    break;
            }
        }
    }

    private void UndoRegistry(JournalEntry e)
    {
        var parts = e.Key.Split(new[] { "::" }, 3, StringSplitOptions.None);
        var root = (RegRoot)Enum.Parse(typeof(RegRoot), parts[0]);
        var key = parts[1];
        var name = parts.Length > 2 ? parts[2] : string.Empty;
        if (e.OldValue == null || e.Extra == "Missing")
        {
            _raw.Registry.DeleteValue(root, key, name);
            return;
        }
        var kind = e.Extra != null ? (RegKind)Enum.Parse(typeof(RegKind), e.Extra) : RegKind.String;
        _raw.Registry.SetValue(root, key, name, RegistryValueCodec.Decode(e.OldValue, kind), kind);
    }
}

public static class RegistryValueCodec
{
    public static string? Encode(object? value, RegKind? kind)
    {
        if (value == null) return null;
        return kind switch
        {
            RegKind.DWord => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            RegKind.QWord => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }

    public static object Decode(string text, RegKind kind) => kind switch
    {
        RegKind.DWord => unchecked((int)long.Parse(text, CultureInfo.InvariantCulture)),
        RegKind.QWord => long.Parse(text, CultureInfo.InvariantCulture),
        _ => text,
    };
}
