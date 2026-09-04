using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Engine;
using NewPcSetup.Core.Models;

namespace NewPcSetup.Tasks;

/// <summary>没有环境变量可用、只能改配置文件的迁移项。改写前由 JournalingFileSystem 自动备份原文件，回滚时还原。</summary>
public abstract class ConfigFileTaskBase : TaskBase
{
    protected static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    protected static string Target(TaskContext ctx, string relative) => Path.Combine(DataRoot(ctx), relative);
}

/// <summary>Maven 本地仓库：改写 settings.xml 的 &lt;localRepository&gt;。</summary>
public sealed class MavenSettingsTask : ConfigFileTaskBase
{
    public const string Id = "env.maven";
    private const string Relative = @"DevCache\m2-repository";

    public override TaskMetadata Metadata { get; } = new(Id, "env", "Maven 本地仓库迁移到数据盘",
        @"改写 %USERPROFILE%\.m2\settings.xml 的 <localRepository>，把 jar 仓库放到数据盘 DevCache\m2-repository。原文件先备份为 settings.xml.newpcsetup-bak，已下载的 jar 不会被移动（Maven 会按需重新下载）。",
        RiskFlags.Reversible, new[] { PathSkeletonTask.Id }, 142);

    public override bool IsApplicable(EnvironmentSnapshot s, Answers a) => HasDataDrive(s, a) && s.HasTool("maven");

    private static string SettingsPath => Path.Combine(UserProfile, ".m2", "settings.xml");

    public override DetectResult Detect(TaskContext ctx)
    {
        var target = Target(ctx, Relative);
        var text = ctx.FileSystem.ReadAllText(SettingsPath);
        if (text == null)
            return new DetectResult(false, @"未设置（默认 %USERPROFILE%\.m2\repository）", target);

        XDocument doc;
        try { doc = XDocument.Parse(text); }
        catch (XmlException ex) { return DetectResult.NotApplicableBecause($"settings.xml 解析失败（{ex.Message}），请手动把 <localRepository> 改到 {target}"); }
        if (doc.Root == null || doc.Root.Name.LocalName != "settings")
            return DetectResult.NotApplicableBecause($"settings.xml 的根元素不是 <settings>，请手动把 <localRepository> 改到 {target}");

        var current = LocalRepository(doc)?.Value.Trim();
        var satisfied = !string.IsNullOrEmpty(current) && !ctx.Snapshot.IsOnSystemDrive(Environment.ExpandEnvironmentVariables(current!));
        return new DetectResult(satisfied, string.IsNullOrEmpty(current) ? @"未设置（默认 %USERPROFILE%\.m2\repository）" : current, satisfied ? current : target);
    }

    public override void Apply(TaskContext ctx)
    {
        var target = Target(ctx, Relative);
        ctx.FileSystem.CreateDirectory(target);
        var text = ctx.FileSystem.ReadAllText(SettingsPath);
        if (text == null)
        {
            ctx.FileSystem.WriteAllText(SettingsPath, NewSettings(target));
            return;
        }
        var doc = XDocument.Parse(text);
        var ns = doc.Root!.Name.Namespace;
        var element = LocalRepository(doc);
        if (element != null) element.Value = target;
        else doc.Root.AddFirst(new XElement(ns + "localRepository", target));
        var declaration = doc.Declaration?.ToString() ?? "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";
        ctx.FileSystem.WriteAllText(SettingsPath, declaration + Environment.NewLine + doc.ToString() + Environment.NewLine);
    }

    private static XElement? LocalRepository(XDocument doc)
        => doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "localRepository");

    private static string NewSettings(string target) => string.Join(Environment.NewLine,
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
        "<settings xmlns=\"http://maven.apache.org/SETTINGS/1.0.0\"",
        "          xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"",
        "          xsi:schemaLocation=\"http://maven.apache.org/SETTINGS/1.0.0 https://maven.apache.org/xsd/settings-1.0.0.xsd\">",
        "  <localRepository>" + target + "</localRepository>",
        "</settings>",
        string.Empty);
}

/// <summary>npm 全局包目录：写 .npmrc 的 prefix，并把该目录补进用户 PATH（否则全局命令调不到）。</summary>
public sealed class NpmPrefixTask : ConfigFileTaskBase
{
    public const string Id = "env.npm_prefix";
    private const string Relative = @"DevCache\npm-global";

    public override TaskMetadata Metadata { get; } = new(Id, "env", "npm 全局包目录迁移到数据盘",
        @"改写 %USERPROFILE%\.npmrc 的 prefix，把 npm -g 装的包放到数据盘 DevCache\npm-global，并把该目录加进用户 PATH。原文件先备份为 .npmrc.newpcsetup-bak，registry 等其他配置原样保留。",
        RiskFlags.Reversible | RiskFlags.NeedsSignOut, new[] { PathSkeletonTask.Id }, 145);

    public override bool IsApplicable(EnvironmentSnapshot s, Answers a) => HasDataDrive(s, a) && s.HasTool("npm");

    private static string NpmrcPath => Path.Combine(UserProfile, ".npmrc");
    private static string DefaultPrefix => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");

    /// <summary>优先级与 npm 一致：环境变量 &gt; .npmrc &gt; 默认 %APPDATA%\npm。</summary>
    private static string? CurrentPrefix(TaskContext ctx)
    {
        var env = ctx.Environment.Get(EnvScope.User, "npm_config_prefix");
        if (!string.IsNullOrEmpty(env)) return Environment.ExpandEnvironmentVariables(env!);
        var line = ReadPrefixLine(ctx.FileSystem.ReadAllText(NpmrcPath));
        return line ?? DefaultPrefix;
    }

    private static string? ReadPrefixLine(string? npmrc)
    {
        if (npmrc == null) return null;
        foreach (var raw in npmrc.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
            var i = line.IndexOf('=');
            if (i > 0 && line.Substring(0, i).Trim().Equals("prefix", StringComparison.OrdinalIgnoreCase))
                return line.Substring(i + 1).Trim().Trim('"');
        }
        return null;
    }

    public override DetectResult Detect(TaskContext ctx)
    {
        var target = Target(ctx, Relative);
        var current = CurrentPrefix(ctx);
        if (current != null && !ctx.Snapshot.IsOnSystemDrive(current))
            return new DetectResult(true, current, current);
        // 默认目录里已有全局包：改 prefix 后这些包不会跟着走，npm -g 装的命令会突然失效
        if (ctx.FileSystem.DirectoryExists(DefaultPrefix) && !ctx.FileSystem.IsDirectoryEmpty(DefaultPrefix))
            return DetectResult.NotApplicableBecause(
                $"{DefaultPrefix} 里已经装了全局包，改 prefix 不会把它们搬过去。请先记下 npm ls -g --depth=0 的结果，在新目录重装，或保持现状");
        return new DetectResult(false, current ?? "未设置", target);
    }

    public override void Apply(TaskContext ctx)
    {
        var target = Target(ctx, Relative);
        ctx.FileSystem.CreateDirectory(target);
        WritePrefix(ctx, target);
        AddToPath(ctx, target);
    }

    private static void WritePrefix(TaskContext ctx, string target)
    {
        var npmrc = ctx.FileSystem.ReadAllText(NpmrcPath);
        if (npmrc == null)
        {
            ctx.FileSystem.WriteAllText(NpmrcPath, "prefix=" + target + Environment.NewLine);
            return;
        }
        var lines = npmrc.Replace("\r\n", "\n").Split('\n').ToList();
        var replaced = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
            var eq = line.IndexOf('=');
            if (eq <= 0 || !line.Substring(0, eq).Trim().Equals("prefix", StringComparison.OrdinalIgnoreCase)) continue;
            lines[i] = "prefix=" + target;
            replaced = true;
            break;
        }
        while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
        if (!replaced) lines.Add("prefix=" + target);
        ctx.FileSystem.WriteAllText(NpmrcPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    /// <summary>prefix 目录不在 PATH 里的话，npm -g 装的命令敲不出来，所以一并补上。</summary>
    private static void AddToPath(TaskContext ctx, string target)
    {
        var path = ctx.Environment.Get(EnvScope.User, "Path") ?? string.Empty;
        var parts = path.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(p => string.Equals(p.Trim().TrimEnd('\\'), target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) return;
        var updated = path.Length == 0 ? target : path.TrimEnd(';') + ";" + target;
        ctx.Environment.Set(EnvScope.User, "Path", updated);
    }
}

/// <summary>Conda 环境与包目录：写 .condarc 的 envs_dirs / pkgs_dirs。只在没有 .condarc 时写，不改现成的 YAML。</summary>
public sealed class CondaRcTask : ConfigFileTaskBase
{
    public const string Id = "env.conda";

    public override TaskMetadata Metadata { get; } = new(Id, "env", "Conda 环境与包目录迁移到数据盘",
        @"在 %USERPROFILE%\.condarc 写入 envs_dirs 与 pkgs_dirs，把虚拟环境与包缓存放到数据盘 DevCache\conda。已存在 .condarc 时不改写，只给出手动步骤，避免破坏其中的 channels、proxy 等设置。",
        RiskFlags.Reversible, new[] { PathSkeletonTask.Id }, 143);

    public override bool IsApplicable(EnvironmentSnapshot s, Answers a) => HasDataDrive(s, a) && s.HasTool("conda");

    private static string CondaRcPath => Path.Combine(UserProfile, ".condarc");
    private static string EnvsDir(TaskContext ctx) => Target(ctx, @"DevCache\conda\envs");
    private static string PkgsDir(TaskContext ctx) => Target(ctx, @"DevCache\conda\pkgs");

    public override DetectResult Detect(TaskContext ctx)
    {
        var envs = EnvsDir(ctx);
        var pkgs = PkgsDir(ctx);
        var target = $"envs_dirs={envs}; pkgs_dirs={pkgs}";
        var text = ctx.FileSystem.ReadAllText(CondaRcPath);
        if (text == null)
            return new DetectResult(false, @"未设置（默认 %USERPROFILE%\.conda 与 Anaconda 安装目录）", target);
        if (text.IndexOf(envs, StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf(pkgs, StringComparison.OrdinalIgnoreCase) >= 0)
            return new DetectResult(true, target, target);
        return DetectResult.NotApplicableBecause(
            $"已有 .condarc，本工具不改写现成的 YAML。请手动在其中加入 envs_dirs: [{envs}] 与 pkgs_dirs: [{pkgs}]");
    }

    public override void Apply(TaskContext ctx)
    {
        var envs = EnvsDir(ctx);
        var pkgs = PkgsDir(ctx);
        ctx.FileSystem.CreateDirectory(envs);
        ctx.FileSystem.CreateDirectory(pkgs);
        ctx.FileSystem.WriteAllText(CondaRcPath, string.Join(Environment.NewLine,
            "# 由 NewPcSetup 写入：conda 环境与包缓存放在数据盘",
            "envs_dirs:",
            "  - " + envs,
            "pkgs_dirs:",
            "  - " + pkgs,
            string.Empty));
    }
}
