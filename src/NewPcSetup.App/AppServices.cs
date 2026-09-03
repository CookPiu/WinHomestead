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
        ExecutionCoordinator coordinator, Planner planner, InstalledPrograms installed, SoftwareCatalog software)
    {
        Store = store; Logger = logger; Execution = execution; Collector = collector; Coordinator = coordinator;
        Planner = planner; Installed = installed; Software = software;
    }

    public StateStore Store { get; }
    public FileLogger Logger { get; }
    public ExecutionServices Execution { get; }
    public SnapshotCollector Collector { get; }
    public ExecutionCoordinator Coordinator { get; }
    public Planner Planner { get; }
    public InstalledPrograms Installed { get; }
    public SoftwareCatalog Software { get; }
    public IReadOnlyList<ITask> Catalog => TaskCatalog.All;
    public SessionState Session { get; } = new();

    public static AppServices Create()
    {
        var store = new StateStore();
        var logger = new FileLogger(store.LogDir);
        var registry = new WindowsRegistry();
        var shell = new WindowsShell();
        var execution = new ExecutionServices(registry, new WindowsEnvironment(registry), shell, new WindowsFileSystem(), new WindowsPower(registry), logger);
        var collector = new SnapshotCollector(registry, shell, logger);
        var coordinator = new ExecutionCoordinator(execution, new WmiSystemRestore(logger), store);
        return new AppServices(store, logger, execution, collector, coordinator, new Planner(execution), new InstalledPrograms(registry), SoftwareCatalog.LoadEmbedded());
    }
}

/// <summary>向导各页共享的会话状态。</summary>
public sealed class SessionState
{
    public EnvironmentSnapshot? Snapshot { get; set; }
    public Answers? Answers { get; set; }
    public Plan? Plan { get; set; }
    public ExecutionResult? Result { get; set; }
}
