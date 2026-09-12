using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WinHomestead.Core.Abstractions;
using WinHomestead.Core.Engine;
using WinHomestead.Core.Infrastructure;
using WinHomestead.Core.Models;

namespace WinHomestead.Tasks;

/// <summary>
/// 装工具之前先把各语言包管理器的缓存目录指到数据盘。开荒工具面对的是"还没装"的机器，
/// 所以这里只做预设：变量已经设过的不动，对应工具已经装上的整条跳过——迁移使用中的工具
/// 需要搬存量数据，不在本工具的范围内。
/// </summary>
public sealed class DevCachePresetTask : TaskBase
{
    public const string Id = "env.preset";

    /// <summary>
    /// ToolId 为空表示与具体工具无关；非空时该工具已安装就跳过这一项。
    /// ValueFormat 非空时变量值不是裸路径，而是按该格式拼出来的——Maven 没有专用的仓库路径变量，
    /// 只能通过 MAVEN_OPTS 传一段 JVM 参数。
    /// </summary>
    private sealed record Var(string Name, string RelativeTarget, string? ToolId, string Tool, string? ValueFormat = null);

    // 工具名会进 Detect 的文案，所以也要双语；这张表是静态的，语言在类型初始化时定下
    private static readonly Var[] Vars =
    {
        new("PIP_CACHE_DIR", @"DevCache\pip-cache", "pip", "Python / pip"),
        new("UV_CACHE_DIR", @"DevCache\uv-cache", "uv", "uv"),
        new("npm_config_cache", @"DevCache\npm-cache", "npm", "npm"),
        new("npm_config_prefix", @"DevCache\npm-global", "npm", L.S("npm 全局包", "npm global packages")),
        new("PNPM_HOME", @"DevCache\pnpm", "pnpm", "pnpm"),
        new("YARN_CACHE_FOLDER", @"DevCache\yarn-cache", "yarn", "Yarn"),
        new("GRADLE_USER_HOME", @"DevCache\gradle", "gradle", "Gradle"),
        new("MAVEN_OPTS", @"DevCache\maven-repo", "maven", "Maven", "-Dmaven.repo.local={0}"),
        new("NUGET_PACKAGES", @"DevCache\nuget-packages", "nuget", "NuGet"),
        new("VCPKG_DEFAULT_BINARY_CACHE", @"DevCache\vcpkg-cache", "vcpkg", "vcpkg"),
        new("CARGO_HOME", @"DevCache\cargo", "cargo", "Cargo"),
        new("RUSTUP_HOME", @"DevCache\rustup", "cargo", "rustup"),
        new("GOPATH", @"DevCache\go", "go", "Go"),
        new("GOMODCACHE", @"DevCache\go\pkg\mod", "go", L.S("Go 模块缓存", "Go module cache")),
        new("PUB_CACHE", @"DevCache\pub-cache", "pub", "Flutter / Dart"),
        // conda 把配置项 envs_dirs / pkgs_dirs 映射成同名环境变量，优先级高于 .condarc
        new("CONDA_ENVS_DIRS", @"DevCache\conda\envs", "conda", L.S("Conda 环境", "Conda environments")),
        new("CONDA_PKGS_DIRS", @"DevCache\conda\pkgs", "conda", L.S("Conda 包缓存", "Conda package cache")),
        new("HF_HOME", @"Models\huggingface", "hf", "Hugging Face"),
        new("OLLAMA_MODELS", @"Models\ollama", "ollama", "Ollama"),
    };

    /// <summary>npm 全局包目录不在 PATH 里的话，npm -g 装出来的命令敲不出来。</summary>
    private const string NpmPrefixVar = "npm_config_prefix";

    /// <summary>变量要写进去的值：默认就是目标目录，带 ValueFormat 的包一层。</summary>
    private static string Value(Var v, string target) => v.ValueFormat == null ? target : string.Format(v.ValueFormat, target);

    public override TaskMetadata Metadata { get; } = new(Id, "env",
        L.S("预设开发缓存目录（装工具之前做）", "Preset dev cache locations (before installing tools)"),
        L.S("把 pip、npm、pnpm、Gradle、Maven、NuGet、vcpkg、Cargo、Go、Conda、Ollama 等的缓存目录通过用户环境变量指到数据盘 DevCache/Models 下。" +
            "以后装上这些工具，缓存直接落在数据盘，不用事后再搬。已经设过的变量不动；对应工具已经装了的跳过那一项，避免把在用的工具指到空目录。" +
            "npm 全局包目录会同时追加进用户 PATH。" +
            "两点注意：NUGET_PACKAGES 的优先级高于 nuget.config 里的 globalPackagesFolder，会盖掉仓库自带的设置；" +
            "Maven 没有专用变量，走的是 MAVEN_OPTS 传 -Dmaven.repo.local，之后自己往 MAVEN_OPTS 里加参数时别把这段覆盖掉。",

            "Points the caches of pip, npm, pnpm, Gradle, Maven, NuGet, vcpkg, Cargo, Go, Conda, Ollama and friends at DevCache/Models " +
            "on the data drive, using user environment variables. Install those tools later and their caches land on the data drive " +
            "from the start, with nothing to move afterwards. Variables you already set are left alone, and any tool already installed " +
            "is skipped so a working tool never gets pointed at an empty folder. The npm global prefix is also appended to your PATH. " +
            "Two caveats: NUGET_PACKAGES outranks globalPackagesFolder in nuget.config and will override a repository's own setting; " +
            "and Maven has no dedicated variable, so the repository path rides along in MAVEN_OPTS as -Dmaven.repo.local — " +
            "don't overwrite that segment when you add your own JVM options later."),
        RiskFlags.Reversible | RiskFlags.NeedsSignOut, new[] { PathSkeletonTask.Id }, 130);

    public override bool IsApplicable(EnvironmentSnapshot s, Answers a) => HasDataDrive(s, a);

    /// <summary>待设置的变量：变量还没设，且对应工具还没装。</summary>
    private static List<Var> Pending(TaskContext ctx)
        => Vars.Where(v => string.IsNullOrEmpty(ctx.Environment.Get(EnvScope.User, v.Name))
                           && (v.ToolId == null || !ctx.Snapshot.HasTool(v.ToolId))).ToList();

    public override DetectResult Detect(TaskContext ctx)
    {
        var root = DataRoot(ctx);
        var pending = Pending(ctx);
        if (pending.Count == 0)
        {
            var installed = Vars.Where(v => v.ToolId != null && ctx.Snapshot.HasTool(v.ToolId!)
                                            && string.IsNullOrEmpty(ctx.Environment.Get(EnvScope.User, v.Name)))
                                .Select(v => v.Tool).Distinct().ToList();
            return installed.Count > 0
                ? DetectResult.NotApplicableBecause(
                    L.S($"{L.Join(installed.ToArray())} 已经装在这台机器上了。预设变量只对还没装的工具有意义，改在用工具的缓存位置需要连同已下载的内容一起搬，本工具不做",
                        $"{L.Join(installed.ToArray())} are already installed here. Presetting only helps for tools that aren't installed yet; moving a working tool's cache means moving what it already downloaded, which this tool doesn't do"))
                : new DetectResult(true, L.S("全部已设置", "all set"), L.S("全部已设置", "all set"));
        }

        var skipped = Vars.Where(v => v.ToolId != null && ctx.Snapshot.HasTool(v.ToolId!)).Select(v => v.Tool).Distinct().ToList();
        var current = L.S($"{pending.Count} 个变量未设置", $"{pending.Count} variables not set")
                      + (skipped.Count > 0 ? L.S($"；已装的 {L.Join(skipped.ToArray())} 跳过",
                                                 $"; skipping {L.Join(skipped.ToArray())} (already installed)") : string.Empty);
        var target = string.Join("; ", pending.Take(4).Select(v => $"{v.Name}={Value(v, Path.Combine(root, v.RelativeTarget))}"))
                     + (pending.Count > 4 ? L.S($" 等 {pending.Count} 项", $" and {pending.Count - 4} more") : string.Empty);
        return new DetectResult(false, current, target);
    }

    public override void Apply(TaskContext ctx)
    {
        var root = DataRoot(ctx);
        foreach (var v in Pending(ctx))
        {
            var target = Path.Combine(root, v.RelativeTarget);
            ctx.FileSystem.CreateDirectory(target);
            ctx.Environment.Set(EnvScope.User, v.Name, Value(v, target));
            if (v.Name == NpmPrefixVar) AddToPath(ctx, target);
        }
    }

    private static void AddToPath(TaskContext ctx, string target)
    {
        var path = ctx.Environment.Get(EnvScope.User, "Path") ?? string.Empty;
        var parts = path.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(p => string.Equals(p.Trim().TrimEnd('\\'), target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) return;
        ctx.Environment.Set(EnvScope.User, "Path", path.Length == 0 ? target : path.TrimEnd(';') + ";" + target);
    }
}
