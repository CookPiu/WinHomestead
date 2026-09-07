using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Engine;
using NewPcSetup.Core.Models;

namespace NewPcSetup.Tasks;

/// <summary>系统级 TEMP/TMP（Machine 作用域）指向数据盘 Temp\System。默认不勾选；受管理的机器不提供。</summary>
public sealed class MachineTempTask : TaskBase
{
    public const string Id = "path.temp.machine";

    public override TaskMetadata Metadata { get; } = new(Id, "path", "迁移系统临时目录（可选）",
        "把系统级 TEMP 与 TMP 指向数据盘 Temp\\System。影响服务与安装程序的临时文件，重启后完全生效。默认不勾选，只建议在数据盘可靠且常驻时开启。",
        RiskFlags.Reversible | RiskFlags.AdminOnly | RiskFlags.NeedsReboot, new[] { PathSkeletonTask.Id }, 121);

    public override bool IsApplicable(EnvironmentSnapshot s, Answers a) => HasDataDrive(s, a) && !s.IsMdmEnrolled;
    public override bool DefaultChecked(EnvironmentSnapshot s, Answers a) => false;

    private static string Target(TaskContext ctx) => Path.Combine(DataRoot(ctx), "Temp", "System");

    public override DetectResult Detect(TaskContext ctx)
    {
        var temp = ctx.Environment.Get(EnvScope.Machine, "TEMP");
        var tmp = ctx.Environment.Get(EnvScope.Machine, "TMP");
        var target = Target(ctx);
        var ok = temp != null && tmp != null
                 && !ctx.Snapshot.IsOnSystemDrive(Environment.ExpandEnvironmentVariables(temp))
                 && !ctx.Snapshot.IsOnSystemDrive(Environment.ExpandEnvironmentVariables(tmp));
        return new DetectResult(ok, $"TEMP={temp ?? "未设置"}; TMP={tmp ?? "未设置"}", $"TEMP={target}; TMP={target}");
    }

    public override void Apply(TaskContext ctx)
    {
        var target = Target(ctx);
        ctx.FileSystem.CreateDirectory(target);
        ctx.Environment.Set(EnvScope.Machine, "TEMP", target);
        ctx.Environment.Set(EnvScope.Machine, "TMP", target);
    }
}

/// <summary>把数据盘 Applications 固定到资源管理器“快速访问”，安装软件时一眼能找到目标目录。</summary>
public sealed class QuickAccessPinTask : TaskBase
{
    public const string Id = "path.quick_access";

    public override TaskMetadata Metadata { get; } = new(Id, "path", "把 Applications 固定到快速访问",
        "在资源管理器左侧“快速访问”中固定数据盘的 Applications 目录，安装软件选路径时直接可达。",
        RiskFlags.Reversible, new[] { PathSkeletonTask.Id }, 105);

    public override bool IsApplicable(EnvironmentSnapshot s, Answers a) => HasDataDrive(s, a);

    private static string Target(TaskContext ctx) => Path.Combine(DataRoot(ctx), "Applications");

    public override DetectResult Detect(TaskContext ctx)
    {
        var target = Target(ctx);
        var pinned = ctx.FileSystem.DirectoryExists(target) && ctx.Shell.IsPinnedToQuickAccess(target);
        return new DetectResult(pinned, pinned ? "已固定" : "未固定", target);
    }

    public override void Apply(TaskContext ctx)
    {
        var target = Target(ctx);
        ctx.FileSystem.CreateDirectory(target);
        ctx.Shell.PinToQuickAccess(target);
    }

    public override bool Verify(TaskContext ctx) => ctx.Shell.IsPinnedToQuickAccess(Target(ctx));
}

/// <summary>清理旧的用户临时目录中 7 天前的文件。删除不可撤销，因此不声明 Reversible。</summary>
public sealed class TempCleanupTask : TaskBase
{
    public const string Id = "storage.temp_cleanup";
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    public override TaskMetadata Metadata { get; } = new(Id, "storage", "清理用户临时目录中的旧文件",
        "删除原用户临时目录（通常是 %LOCALAPPDATA%\\Temp）中 7 天前的文件；正在使用的文件自动跳过。此操作不可撤销。",
        RiskFlags.None, Array.Empty<string>(), 500);

    /// <summary>探测时记录的用户 TEMP（迁移前的旧目录）；缺失时退回 %LOCALAPPDATA%\Temp。</summary>
    public static string OldTempDir(EnvironmentSnapshot s)
    {
        if (s.UserEnvironment.TryGetValue("TEMP", out var t) && !string.IsNullOrWhiteSpace(t))
            return Environment.ExpandEnvironmentVariables(t).TrimEnd('\\');
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
    }

    /// <summary>探测只要够判断"有没有、大概多少"，所以设上限；整目录走一遍在真实临时目录里要十几秒。</summary>
    private const int DetectMaxFiles = 3000;
    private static readonly TimeSpan DetectBudget = TimeSpan.FromMilliseconds(800);
    /// <summary>执行时才需要完整清单，但同样给个上限，避免极端目录把执行拖到没边；剩下的下次再跑。</summary>
    private const int ApplyMaxFiles = 200_000;
    private static readonly TimeSpan ApplyBudget = TimeSpan.FromSeconds(60);

    public override DetectResult Detect(TaskContext ctx)
    {
        var dir = OldTempDir(ctx.Snapshot);
        var scan = ctx.FileSystem.FilesOlderThan(dir, DateTime.Now - MaxAge, DetectMaxFiles, DetectBudget);
        var count = scan.Files.Count;
        if (count == 0) return new DetectResult(true, $"{dir} 无 7 天前的文件", "无需清理");
        var mb = scan.SizeBytes / 1048576d;
        var more = scan.Truncated ? "以上" : string.Empty;
        return new DetectResult(false,
            $"{dir} 有 {count}{(scan.Truncated ? "+" : string.Empty)} 个旧文件，约 {mb:F0} MB{more}",
            $"删除 7 天前的临时文件，释放约 {mb:F0} MB{more}");
    }

    public override void Apply(TaskContext ctx)
    {
        var scan = ctx.FileSystem.FilesOlderThan(OldTempDir(ctx.Snapshot), DateTime.Now - MaxAge, ApplyMaxFiles, ApplyBudget);
        var failed = 0;
        foreach (var f in scan.Files)
        {
            ctx.Cancellation.ThrowIfCancellationRequested();
            if (!ctx.FileSystem.TryDeleteFile(f.Path)) failed++;
        }
        ctx.Log.Info($"[{Id}] 删除 {scan.Files.Count - failed}/{scan.Files.Count} 个旧临时文件");
        if (failed > 0) ctx.ManualSteps.Add($"临时目录清理：{failed} 个文件正被占用未删除，重启后再运行一次即可。");
        if (scan.Truncated) ctx.ManualSteps.Add("临时目录清理：文件太多，本次只清理了一部分，再执行一次可继续。");
    }

    /// <summary>尽力而为：被占用的文件跳过不算失败。</summary>
    public override bool Verify(TaskContext ctx) => true;
}

/// <summary>台式机关闭休眠以释放 hiberfil.sys。笔记本不提供此项。</summary>
public sealed class HibernateOffTask : TaskBase
{
    public const string Id = "storage.hibernate_off";

    public override TaskMetadata Metadata { get; } = new(Id, "storage", "关闭休眠（台式机）",
        "执行 powercfg /hibernate off，删除系统盘上的 hiberfil.sys。台式机不用休眠，关闭后快速启动一并失效，开机走完整冷启动。可随时用 powercfg /hibernate on 恢复。",
        RiskFlags.Reversible | RiskFlags.AdminOnly, Array.Empty<string>(), 501);

    public override bool IsApplicable(EnvironmentSnapshot s, Answers a) => !s.IsLaptop && !s.IsMdmEnrolled;
    public override bool DefaultChecked(EnvironmentSnapshot s, Answers a) => false;

    public override DetectResult Detect(TaskContext ctx)
    {
        var enabled = ctx.Power.IsHibernateEnabled();
        return new DetectResult(!enabled, enabled ? "休眠已启用" : "休眠已关闭", "休眠关闭");
    }

    public override void Apply(TaskContext ctx) => ctx.Power.SetHibernate(false);
}
