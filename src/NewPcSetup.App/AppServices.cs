using System.Collections.Generic;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Engine;
using NewPcSetup.Core.Infrastructure;
using NewPcSetup.Core.Models;
using NewPcSetup.Native;
using NewPcSetup.Tasks;

namespace NewPcSetup.App;

/// <summary>手工组装的依赖，不引入 IoC 容器。</summary>
public sealed class AppServices
{
    private AppServices(StateStore store, FileLogger logger, ExecutionServices execution, SnapshotCollector collector,
        ExecutionCoordinator coordinator, SessionRunner runner, Planner planner, InstalledPrograms installed, SoftwareCatalog software,
        SystemChecks checks)
    {
        Store = store; Logger = logger; Execution = execution; Collector = collector; Coordinator = coordinator; Runner = runner;
        Planner = planner; Installed = installed; Software = software; Checks = checks;
    }

    public StateStore Store { get; }
    public FileLogger Logger { get; }
    public ExecutionServices Execution { get; }
    public SnapshotCollector Collector { get; }
    public ExecutionCoordinator Coordinator { get; }
    public SessionRunner Runner { get; }
    public Planner Planner { get; }
    public InstalledPrograms Installed { get; }
    public SoftwareCatalog Software { get; }
    public SystemChecks Checks { get; }
    public IReadOnlyList<ITask> Catalog => TaskCatalog.All;
    public SessionState Session { get; } = new();

    public static AppServices Create()
    {
        var store = new StateStore();
        var logger = new FileLogger(store.LogDir);
        var registry = new WindowsRegistry();
        var shell = new WindowsShell();
        var execution = new ExecutionServices(registry, new WindowsEnvironment(registry), shell, new WindowsFileSystem(), new WindowsPower(registry), new WmiStorage(logger), logger);
        var collector = new SnapshotCollector(registry, shell, logger);
        var coordinator = new ExecutionCoordinator(execution, store);
        var runner = new SessionRunner(execution, new WmiSystemRestore(logger), store);
        var installed = new InstalledPrograms(registry);
        return new AppServices(store, logger, execution, collector, coordinator, runner, new Planner(execution), installed, SoftwareCatalog.LoadEmbedded(),
            new SystemChecks(registry, installed, logger));
    }
}

/// <summary>各页共享的会话状态。</summary>
public sealed class SessionState
{
    public EnvironmentSnapshot? Snapshot { get; set; }
    public Answers? Answers { get; set; }
    public Plan? Plan { get; set; }
    /// <summary>重启后复核（--resume）得到的上次结果；正常启动为 null。</summary>
    public ExecutionResult? PreviousResult { get; set; }
}

public enum StartupMode { Normal, Reverify }
