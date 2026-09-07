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

/// <summary>注册表任务的“当前 → 目标”要给人话，原始键值只进 Detail。</summary>
public class FriendlyDetectTests
{
    private static RegistryValueTask Task(params RegistryEntry[] entries)
        => new(new TaskMetadata("t", "ui", "t", "t", RiskFlags.Reversible, Array.Empty<string>(), 1), entries);

    [Fact]
    public void LabeledEntriesProduceWordsAndKeepRawInDetail()
    {
        var (svc, reg, _, _, _) = TestData.Services();
        reg.Seed(RegRoot.CurrentUser, "K", "Mode", 3, RegKind.DWord);
        var s = TestData.Snapshot();
        var ctx = svc.CreateContext("t", s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        var task = Task(RegistryEntry.Dword("K", "Mode", 1).As("搜索样式", "未设置（默认搜索框）", (1, "仅图标"), (3, "搜索框+标签")),
                        RegistryEntry.Dword("K", "Unset", 0).As("小组件", "未设置（默认显示）", (0, "隐藏"), (1, "显示")));
        var d = task.Detect(ctx);

        Assert.False(d.Satisfied);
        Assert.Equal("搜索样式：搜索框+标签；小组件：未设置（默认显示）", d.CurrentValue);
        Assert.Equal("搜索样式：仅图标；小组件：隐藏", d.TargetValue);
        Assert.Equal("Mode=3; Unset=未设置 → Mode=1; Unset=0", d.Detail);
    }

    [Fact]
    public void SameLabelCollapsesWhenValuesAgree()
    {
        var (svc, reg, _, _, _) = TestData.Services();
        reg.Seed(RegRoot.CurrentUser, "K", "A", 1, RegKind.DWord);
        reg.Seed(RegRoot.CurrentUser, "K", "B", 1, RegKind.DWord);
        reg.Seed(RegRoot.CurrentUser, "K", "C", 0, RegKind.DWord);
        var s = TestData.Snapshot();
        var ctx = svc.CreateContext("t", s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        var agree = Task(RegistryEntry.Dword("K", "A", 0).As("建议", null, (1, "开启"), (0, "关闭")),
                         RegistryEntry.Dword("K", "B", 0).As("建议", null, (1, "开启"), (0, "关闭")));
        Assert.Equal("建议：开启", agree.Detect(ctx).CurrentValue);
        Assert.Equal("建议：关闭", agree.Detect(ctx).TargetValue);

        var mixed = Task(RegistryEntry.Dword("K", "A", 0).As("建议", null, (1, "开启"), (0, "关闭")),
                         RegistryEntry.Dword("K", "C", 0).As("建议", null, (1, "开启"), (0, "关闭")));
        Assert.Equal("建议：开启 / 关闭（各项不一致）", mixed.Detect(ctx).CurrentValue);
    }

    [Fact]
    public void UnlabeledTaskKeepsRawOutput()
    {
        var (svc, reg, _, _, _) = TestData.Services();
        reg.Seed(RegRoot.CurrentUser, "K", "X", 5, RegKind.DWord);
        var s = TestData.Snapshot();
        var ctx = svc.CreateContext("t", s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        var d = Task(RegistryEntry.Dword("K", "X", 1)).Detect(ctx);
        Assert.Equal("X=5", d.CurrentValue);
        Assert.Equal("X=1", d.TargetValue);
        Assert.Null(d.Detail);
    }

    [Fact]
    public void EveryCatalogRegistryTaskIsLabeled()
    {
        // 目录里的注册表任务都应标了含义，否则列表上又会冒出 "HideFileExt=0" 这种东西
        var unlabeled = TaskCatalog.All.OfType<RegistryValueTask>()
            .Where(t => t.Entries.All(e => e.Label == null))
            .Select(t => t.Metadata.Id).ToList();
        Assert.True(unlabeled.Count == 0, "未标含义的注册表任务: " + string.Join(", ", unlabeled));
    }
}
