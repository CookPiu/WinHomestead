using System;
using System.IO;
using System.Linq;
using System.Threading;
using NewPcSetup.Core.Engine;
using NewPcSetup.Core.Infrastructure;
using NewPcSetup.Core.Models;
using NewPcSetup.Tasks;
using Xunit;

namespace NewPcSetup.Core.Tests;

public class MavenSettingsTaskTests
{
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".m2", "settings.xml");
    private const string Backup = ".newpcsetup-bak";

    [Fact]
    public void CreatesSettingsWhenMissing_RollbackRemovesIt()
    {
        var (svc, _, _, _, fs) = TestData.Services();
        var s = TestData.Snapshot("D:", false, "maven");
        var task = new MavenSettingsTask();
        var journal = new InMemoryJournal();
        var ctx = svc.CreateContext(MavenSettingsTask.Id, s, TestData.Answers(s), journal, CancellationToken.None);

        Assert.False(task.Detect(ctx).Satisfied);
        task.Apply(ctx);
        Assert.True(task.Verify(ctx));
        Assert.Contains(@"D:\DevCache\m2-repository", fs.Files[SettingsPath]);
        Assert.Contains(@"D:\DevCache\m2-repository", fs.Dirs);

        task.Rollback(ctx, journal.EntriesFor(MavenSettingsTask.Id));
        Assert.False(fs.FileExists(SettingsPath));
    }

    [Fact]
    public void RewritesExistingLocalRepository_KeepsOtherElements_RollbackRestoresBackup()
    {
        var (svc, _, _, _, fs) = TestData.Services();
        var original = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<settings><localRepository>C:\\Users\\t\\.m2\\repository</localRepository><mirrors><mirror><id>aliyun</id></mirror></mirrors></settings>";
        fs.Files[SettingsPath] = original;

        var s = TestData.Snapshot("D:", false, "maven");
        var task = new MavenSettingsTask();
        var journal = new InMemoryJournal();
        var ctx = svc.CreateContext(MavenSettingsTask.Id, s, TestData.Answers(s), journal, CancellationToken.None);

        Assert.False(task.Detect(ctx).Satisfied);
        task.Apply(ctx);
        Assert.True(task.Verify(ctx));
        Assert.Contains(@"D:\DevCache\m2-repository", fs.Files[SettingsPath]);
        Assert.Contains("aliyun", fs.Files[SettingsPath]);
        Assert.Equal(original, fs.Files[SettingsPath + Backup]);

        task.Rollback(ctx, journal.EntriesFor(MavenSettingsTask.Id));
        Assert.Equal(original, fs.Files[SettingsPath]);
    }

    [Fact]
    public void AlreadyOnDataDrive_IsSatisfied_MalformedXmlIsNotApplicable()
    {
        var (svc, _, _, _, fs) = TestData.Services();
        var s = TestData.Snapshot("D:", false, "maven");
        var task = new MavenSettingsTask();
        var ctx = svc.CreateContext(MavenSettingsTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        fs.Files[SettingsPath] = "<settings><localRepository>E:\\maven-repo</localRepository></settings>";
        Assert.True(task.Detect(ctx).Satisfied);

        fs.Files[SettingsPath] = "<settings><localRepository>";
        Assert.StartsWith(Core.Abstractions.DetectResult.NotApplicable, task.Detect(ctx).Reason);
    }
}

public class CondaRcTaskTests
{
    private static string CondaRcPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".condarc");

    [Fact]
    public void WritesCondaRcWhenMissing_RollbackRemovesIt()
    {
        var (svc, _, _, _, fs) = TestData.Services();
        var s = TestData.Snapshot("D:", false, "conda");
        var task = new CondaRcTask();
        var journal = new InMemoryJournal();
        var ctx = svc.CreateContext(CondaRcTask.Id, s, TestData.Answers(s), journal, CancellationToken.None);

        Assert.False(task.Detect(ctx).Satisfied);
        task.Apply(ctx);
        Assert.True(task.Verify(ctx));
        Assert.Contains(@"D:\DevCache\conda\envs", fs.Files[CondaRcPath]);
        Assert.Contains(@"D:\DevCache\conda\pkgs", fs.Dirs);

        task.Rollback(ctx, journal.EntriesFor(CondaRcTask.Id));
        Assert.False(fs.FileExists(CondaRcPath));
    }

    [Fact]
    public void ExistingCondaRcIsNotRewritten()
    {
        var (svc, _, _, _, fs) = TestData.Services();
        fs.Files[CondaRcPath] = "channels:\n  - defaults\n";
        var s = TestData.Snapshot("D:", false, "conda");
        var ctx = svc.CreateContext(CondaRcTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        var d = new CondaRcTask().Detect(ctx);
        Assert.StartsWith(Core.Abstractions.DetectResult.NotApplicable, d.Reason);
        Assert.Equal("channels:\n  - defaults\n", fs.Files[CondaRcPath]);
    }
}

public class AndroidEnvTaskTests
{
    private const string AndroidId = "env.android";

    private static Core.Abstractions.ITask Task => TaskCatalog.All.Single(t => t.Metadata.Id == AndroidId);

    [Fact]
    public void FreshMachine_SetsAllThreeVariables()
    {
        var (svc, _, env, _, _) = TestData.Services();
        var s = TestData.Snapshot("D:", false, "android");
        var task = Task;
        var ctx = svc.CreateContext(AndroidId, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        Assert.False(task.Detect(ctx).Satisfied);
        task.Apply(ctx);
        Assert.True(task.Verify(ctx));
        Assert.Equal(@"D:\Applications\AndroidSdk", env.User["ANDROID_HOME"]);
        Assert.Equal(@"D:\DevCache\android", env.User["ANDROID_USER_HOME"]);
        Assert.Equal(@"D:\VMs\android-avd", env.User["ANDROID_AVD_HOME"]);
    }

    [Fact]
    public void ExistingSdkInDefaultLocation_IsNotApplicable()
    {
        var (svc, _, _, _, fs) = TestData.Services();
        fs.Dirs.Add(Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Android\Sdk"));
        var s = TestData.Snapshot("D:", false, "android");
        var ctx = svc.CreateContext(AndroidId, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        Assert.StartsWith(Core.Abstractions.DetectResult.NotApplicable, Task.Detect(ctx).Reason);
    }

    [Fact]
    public void VariablePointingAtSystemDrive_IsNotApplicable()
    {
        var (svc, _, env, _, _) = TestData.Services();
        env.User["ANDROID_HOME"] = @"C:\Android\Sdk";
        var s = TestData.Snapshot("D:", false, "android");
        var ctx = svc.CreateContext(AndroidId, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        Assert.StartsWith(Core.Abstractions.DetectResult.NotApplicable, Task.Detect(ctx).Reason);
    }
}

public class NpmPrefixTaskTests
{
    private static string NpmrcPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".npmrc");
    private static string DefaultPrefix => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");

    [Fact]
    public void RewritesPrefixKeepingOtherKeys_AndAppendsToPath()
    {
        var (svc, _, env, _, fs) = TestData.Services();
        fs.Files[NpmrcPath] = "registry=https://registry.npmmirror.com\nprefix=C:\\Users\\t\\AppData\\Roaming\\npm\n";
        env.User["Path"] = @"C:\Windows;C:\Windows\System32";
        var s = TestData.Snapshot("D:", false, "npm");
        var task = new NpmPrefixTask();
        var journal = new InMemoryJournal();
        var ctx = svc.CreateContext(NpmPrefixTask.Id, s, TestData.Answers(s), journal, CancellationToken.None);

        Assert.False(task.Detect(ctx).Satisfied);
        task.Apply(ctx);
        Assert.True(task.Verify(ctx));
        Assert.Contains(@"prefix=D:\DevCache\npm-global", fs.Files[NpmrcPath]);
        Assert.Contains("registry=https://registry.npmmirror.com", fs.Files[NpmrcPath]);
        Assert.EndsWith(@";D:\DevCache\npm-global", env.User["Path"]);

        task.Rollback(ctx, journal.EntriesFor(NpmPrefixTask.Id));
        Assert.Equal(@"C:\Windows;C:\Windows\System32", env.User["Path"]);
        Assert.Contains(@"prefix=C:\Users\t\AppData\Roaming\npm", fs.Files[NpmrcPath]);
    }

    [Fact]
    public void PrefixAlreadyOffSystemDrive_IsSatisfied()
    {
        var (svc, _, _, _, fs) = TestData.Services();
        fs.Files[NpmrcPath] = "prefix=D:\\Applications\\claude-code\n";
        var s = TestData.Snapshot("D:", false, "npm");
        var ctx = svc.CreateContext(NpmPrefixTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);
        Assert.True(new NpmPrefixTask().Detect(ctx).Satisfied);
    }

    [Fact]
    public void DefaultPrefixHasGlobalPackages_IsNotApplicable()
    {
        var (svc, _, _, _, fs) = TestData.Services();
        fs.Dirs.Add(DefaultPrefix);
        fs.NonEmptyDirs.Add(DefaultPrefix);
        var s = TestData.Snapshot("D:", false, "npm");
        var ctx = svc.CreateContext(NpmPrefixTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);
        Assert.StartsWith(Core.Abstractions.DetectResult.NotApplicable, new NpmPrefixTask().Detect(ctx).Reason);
    }
}
