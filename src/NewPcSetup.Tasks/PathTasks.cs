using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Engine;
using NewPcSetup.Core.Models;

namespace NewPcSetup.Tasks;

/// <summary>在数据盘建立统一目录骨架。已存在的目录直接复用。</summary>
public sealed class PathSkeletonTask : TaskBase
{
    public const string Id = "path.skeleton";
    public static readonly string[] Folders = { "Applications", "DevCache", "Data", "Temp", "Models", "VMs", "Games" };

    public override TaskMetadata Metadata { get; } = new(Id, "path", "建立数据盘目录骨架",
        "在数据盘创建 Applications / DevCache / Data / Temp / Models / VMs / Games，后续迁移任务都以此为目标。已存在的目录会被复用。",
        RiskFlags.Reversible, new[] { ShrinkAndCreateTask.Id }, 100);

    public override bool IsApplicable(EnvironmentSnapshot s, Answers a) => HasDataDrive(s, a);

    public override DetectResult Detect(TaskContext ctx)
    {
        var root = DataRoot(ctx);
        var missing = Folders.Where(f => !ctx.FileSystem.DirectoryExists(Path.Combine(root, f))).ToList();
        return new DetectResult(missing.Count == 0,
            missing.Count == 0 ? "全部存在" : "缺少: " + string.Join(", ", missing),
            root + "{" + string.Join(",", Folders) + "}");
    }

    public override void Apply(TaskContext ctx)
    {
        var root = DataRoot(ctx);
        foreach (var f in Folders) ctx.FileSystem.CreateDirectory(Path.Combine(root, f));
    }
}

/// <summary>用户级 TEMP/TMP 指向数据盘。</summary>
public sealed class UserTempTask : TaskBase
{
    public const string Id = "path.temp.user";

    public override TaskMetadata Metadata { get; } = new(Id, "path", "迁移用户临时目录 TEMP/TMP",
        "把当前用户的 TEMP 与 TMP 指向数据盘的 Temp 目录。新启动的程序即生效，已打开的程序需重启。旧临时目录不会被删除。",
        RiskFlags.Reversible | RiskFlags.NeedsSignOut, new[] { PathSkeletonTask.Id }, 120);

    public override bool IsApplicable(EnvironmentSnapshot s, Answers a) => HasDataDrive(s, a);

    private static string Target(TaskContext ctx) => Path.Combine(DataRoot(ctx), "Temp");

    public override DetectResult Detect(TaskContext ctx)
    {
        var temp = ctx.Environment.Get(EnvScope.User, "TEMP");
        var tmp = ctx.Environment.Get(EnvScope.User, "TMP");
        var target = Target(ctx);
        var ok = !ctx.Snapshot.IsOnSystemDrive(Expand(temp)) && !ctx.Snapshot.IsOnSystemDrive(Expand(tmp)) && temp != null && tmp != null;
        return new DetectResult(ok, $"TEMP={temp ?? "未设置"}; TMP={tmp ?? "未设置"}", $"TEMP={target}; TMP={target}");
    }

    public override void Apply(TaskContext ctx)
    {
        var target = Target(ctx);
        ctx.FileSystem.CreateDirectory(target);
        ctx.Environment.Set(EnvScope.User, "TEMP", target);
        ctx.Environment.Set(EnvScope.User, "TMP", target);
    }

    private static string? Expand(string? v) => v == null ? null : Environment.ExpandEnvironmentVariables(v);
}

/// <summary>迁移一个已知文件夹（文档/下载/图片/视频/音乐/桌面）到数据盘 Data\ 下，并移动已有内容。</summary>
public sealed class KnownFolderTask : TaskBase
{
    private readonly KnownFolder _folder;
    private readonly string _subdir;

    public KnownFolderTask(KnownFolder folder, string subdir, string displayName, int order)
    {
        _folder = folder; _subdir = subdir;
        var desc = folder == KnownFolder.Desktop
            ? "把桌面移到数据盘。若桌面正被 OneDrive 备份接管，需先在 OneDrive 设置中停止桌面备份，本工具不会改动 OneDrive。"
            : $"把\"{displayName}\"的系统位置改到数据盘 Data\\{subdir}，并移动其中已有文件。目标已存在同名项时跳过不覆盖。";
        Metadata = new TaskMetadata(IdFor(folder), "path", $"迁移{displayName}到数据盘", desc,
            RiskFlags.Reversible, new[] { PathSkeletonTask.Id }, order);
    }

    public static string IdFor(KnownFolder f) => "path.known_folder." + f.ToString().ToLowerInvariant();

    public override TaskMetadata Metadata { get; }

    public override bool IsApplicable(EnvironmentSnapshot s, Answers a) => HasDataDrive(s, a);
    public override bool DefaultChecked(EnvironmentSnapshot s, Answers a) => _folder != KnownFolder.Desktop;

    private string Target(TaskContext ctx) => Path.Combine(DataRoot(ctx), "Data", _subdir);

    public override DetectResult Detect(TaskContext ctx)
    {
        var current = ctx.Shell.GetKnownFolderPath(_folder);
        if (current == null) return DetectResult.NotApplicableBecause("无法读取当前位置");
        if (_folder == KnownFolder.Desktop && ctx.Snapshot.OneDrive.DesktopProtected)
            return new DetectResult(false, current, Target(ctx), DetectResult.NotApplicable + ": 桌面由 OneDrive 备份接管，请先在 OneDrive 设置中停止桌面备份");
        var satisfied = !ctx.Snapshot.IsOnSystemDrive(current);
        return new DetectResult(satisfied, current, satisfied ? current : Target(ctx));
    }

    public override void Apply(TaskContext ctx)
    {
        var current = ctx.Shell.GetKnownFolderPath(_folder);
        var target = Target(ctx);
        ctx.FileSystem.CreateDirectory(target);
        ctx.Shell.SetKnownFolderPath(_folder, target);
        if (current != null && ctx.FileSystem.DirectoryExists(current) && !string.Equals(Path.GetFullPath(current), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            MoveResult r;
            try { r = ctx.Shell.MoveContents(current, target); }
            catch (Exception ex) { throw new TaskFailedException("移动文件失败: " + ex.Message, ex); }
            if (r.Skipped.Count > 0)
                ctx.ManualSteps.Add($"{Metadata.DisplayName}：以下 {r.Skipped.Count} 项因目标已存在同名项未移动，仍在 {current}：{string.Join("; ", r.Skipped.Take(5))}{(r.Skipped.Count > 5 ? " …" : "")}");
        }
    }

    public override bool Verify(TaskContext ctx)
    {
        var now = ctx.Shell.GetKnownFolderPath(_folder);
        return now != null && string.Equals(Path.GetFullPath(now), Path.GetFullPath(Target(ctx)), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>把某个开发工具的缓存/数据目录通过用户环境变量指向数据盘。</summary>
public sealed class EnvVarTask : TaskBase
{
    public sealed record Var(string Name, string RelativeTarget);

    private readonly string _toolId;
    private readonly IReadOnlyList<Var> _vars;

    public EnvVarTask(string toolId, string toolName, IReadOnlyList<Var> vars, int order)
    {
        _toolId = toolId; _vars = vars;
        var names = string.Join(", ", vars.Select(v => v.Name));
        Metadata = new TaskMetadata("env." + toolId, "env", $"{toolName} 缓存迁移到数据盘",
            $"设置用户环境变量 {names}，把 {toolName} 的缓存/数据目录放到数据盘。只改路径，不移动已有缓存（旧缓存可手动删除）。",
            RiskFlags.Reversible | RiskFlags.NeedsSignOut, new[] { PathSkeletonTask.Id }, order);
    }

    public override TaskMetadata Metadata { get; }

    public override bool IsApplicable(EnvironmentSnapshot s, Answers a) => HasDataDrive(s, a) && s.HasTool(_toolId);

    public override DetectResult Detect(TaskContext ctx)
    {
        var root = DataRoot(ctx);
        var current = new List<string>();
        var target = new List<string>();
        var ok = true;
        foreach (var v in _vars)
        {
            var cur = ctx.Environment.Get(EnvScope.User, v.Name);
            current.Add($"{v.Name}={cur ?? "未设置"}");
            target.Add($"{v.Name}={Path.Combine(root, v.RelativeTarget)}");
            if (string.IsNullOrEmpty(cur) || ctx.Snapshot.IsOnSystemDrive(Environment.ExpandEnvironmentVariables(cur))) ok = false;
        }
        // 已经指向非系统盘的自定义位置视为已完成，不强行改成我们的骨架
        return new DetectResult(ok, string.Join("; ", current), ok ? string.Join("; ", current) : string.Join("; ", target));
    }

    public override void Apply(TaskContext ctx)
    {
        var root = DataRoot(ctx);
        foreach (var v in _vars)
        {
            var cur = ctx.Environment.Get(EnvScope.User, v.Name);
            if (!string.IsNullOrEmpty(cur) && !ctx.Snapshot.IsOnSystemDrive(Environment.ExpandEnvironmentVariables(cur))) continue;
            var target = Path.Combine(root, v.RelativeTarget);
            ctx.FileSystem.CreateDirectory(target);
            ctx.Environment.Set(EnvScope.User, v.Name, target);
        }
    }
}
