using System;
using System.Linq;
using System.Threading;
using WinHomestead.Core.Abstractions;
using WinHomestead.Core.Engine;
using WinHomestead.Core.Infrastructure;
using WinHomestead.Core.Models;
using WinHomestead.Tasks;
using Xunit;

namespace WinHomestead.Core.Tests;

public class DevCachePresetTaskTests
{
    [Fact]
    public void FreshMachine_SetsAllVariables_AndPutsNpmGlobalOnPath()
    {
        var (svc, _, env, _, fs) = TestData.Services();
        var s = TestData.Snapshot();
        var task = new DevCachePresetTask();
        var journal = new InMemoryJournal();
        var ctx = svc.CreateContext(DevCachePresetTask.Id, s, TestData.Answers(s), journal, CancellationToken.None);

        Assert.False(task.Detect(ctx).Satisfied);
        task.Apply(ctx);
        Assert.True(task.Verify(ctx));

        Assert.Equal(@"D:\DevCache\pip-cache", env.User["PIP_CACHE_DIR"]);
        Assert.Equal(@"D:\DevCache\gradle", env.User["GRADLE_USER_HOME"]);
        Assert.Equal(@"D:\DevCache\conda\envs", env.User["CONDA_ENVS_DIRS"]);
        Assert.Equal(@"D:\Models\ollama", env.User["OLLAMA_MODELS"]);
        Assert.Equal(@"D:\DevCache\npm-global", env.User["npm_config_prefix"]);
        Assert.Contains(@"D:\DevCache\npm-global", env.User["Path"]);
        Assert.Contains(@"D:\DevCache\pip-cache", fs.Dirs);

        task.Rollback(ctx, journal.EntriesFor(DevCachePresetTask.Id));
        Assert.False(env.User.ContainsKey("PIP_CACHE_DIR"));
    }

    [Fact]
    public void InstalledToolsAreSkipped_OthersStillPreset()
    {
        var (svc, _, env, _, _) = TestData.Services();
        var s = TestData.Snapshot("D:", false, "pip", "gradle");
        var task = new DevCachePresetTask();
        var ctx = svc.CreateContext(DevCachePresetTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        task.Apply(ctx);
        Assert.False(env.User.ContainsKey("PIP_CACHE_DIR"));
        Assert.False(env.User.ContainsKey("GRADLE_USER_HOME"));
        Assert.Equal(@"D:\DevCache\pnpm", env.User["PNPM_HOME"]);
    }

    /// <summary>Maven 没有专用的仓库路径变量，写进 MAVEN_OPTS 的是一段 JVM 参数而不是裸路径。</summary>
    [Fact]
    public void MavenGetsJvmArgument_VcpkgGetsPlainPath()
    {
        var (svc, _, env, _, fs) = TestData.Services();
        var s = TestData.Snapshot();
        var ctx = svc.CreateContext(DevCachePresetTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        new DevCachePresetTask().Apply(ctx);

        Assert.Equal(@"-Dmaven.repo.local=D:\DevCache\maven-repo", env.User["MAVEN_OPTS"]);
        Assert.Contains(@"D:\DevCache\maven-repo", fs.Dirs);
        Assert.Equal(@"D:\DevCache\vcpkg-cache", env.User["VCPKG_DEFAULT_BINARY_CACHE"]);
    }

    [Fact]
    public void ExistingVariablesAreLeftAlone()
    {
        var (svc, _, env, _, _) = TestData.Services();
        env.User["PIP_CACHE_DIR"] = @"E:\my-pip";
        var s = TestData.Snapshot();
        var task = new DevCachePresetTask();
        var ctx = svc.CreateContext(DevCachePresetTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        task.Apply(ctx);
        Assert.Equal(@"E:\my-pip", env.User["PIP_CACHE_DIR"]);
    }

    [Fact]
    public void AllToolsInstalled_IsNotApplicable()
    {
        var (svc, _, _, _, _) = TestData.Services();
        var tools = new[] { "pip", "uv", "npm", "pnpm", "yarn", "gradle", "maven", "nuget", "vcpkg", "cargo", "go", "pub", "conda", "hf", "ollama" };
        var s = TestData.Snapshot("D:", false, tools);
        var ctx = svc.CreateContext(DevCachePresetTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        var d = new DevCachePresetTask().Detect(ctx);
        Assert.StartsWith(DetectResult.NotApplicable, d.Reason);
    }

    [Fact]
    public void NoDataDrive_TaskIsNotListed()
    {
        var (svc, _, _, _, _) = TestData.Services();
        var s = TestData.SingleDiskFresh();
        var plan = new Planner(svc).Build(TaskCatalog.All, s, TestData.Answers(s));
        Assert.DoesNotContain(plan.Items, i => i.TaskId == DevCachePresetTask.Id);
    }
}
