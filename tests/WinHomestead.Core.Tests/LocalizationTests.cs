using System;
using System.Linq;
using WinHomestead.App.ViewModels;
using WinHomestead.Core.Infrastructure;
using WinHomestead.Tasks;
using Xunit;

namespace WinHomestead.Core.Tests;

/// <summary>
/// 双语文案的回归。UI 冒烟测试跑在开发机的中文环境下，只覆盖中文那一份；
/// 这里把语言扳到英文，检查面向用户的字符串确实换了语言，而不是漏了 L.S 还留着中文。
/// L.Chinese 是进程级静态，每个用例用完必须扳回去。
/// </summary>
public class LocalizationTests
{
    private static bool HasChinese(string? text)
        => text != null && text.Any(c => c >= 0x4E00 && c <= 0x9FFF);

    private static void InEnglish(Action body)
    {
        var was = L.Chinese;
        try { L.Chinese = false; body(); }
        finally { L.Chinese = was; }
    }

    [Fact]
    public void CategoryTitlesAreTranslated() => InEnglish(() =>
    {
        Assert.Equal("This PC", new CategoryViewModel(CategoryViewModel.MachineKey).Title);
        Assert.Equal("Display", new CategoryViewModel("display").Title);
        Assert.Equal("Power", new CategoryViewModel("power").Title);

        foreach (var key in new[] { "disk", "path", "env", "ui", "display", "power", "ime", "promo", "gpu", "storage" })
            Assert.False(HasChinese(new CategoryViewModel(key).Title), key + " 的分类标题还是中文");
    });

    /// <summary>任务的名称与说明是列表里最显眼的文字，漏译一条就很明显。</summary>
    [Fact]
    public void TaskNamesAndDescriptionsAreTranslated() => InEnglish(() =>
    {
        // 必须用 BuildFresh：TaskCatalog.All 在首次访问时就把文案取好了，多半已经是中文那一份
        foreach (var task in TaskCatalog.BuildFresh())
        {
            Assert.False(HasChinese(task.Metadata.DisplayName), task.Metadata.Id + " 的名称还是中文");
            Assert.False(HasChinese(task.Metadata.Description), task.Metadata.Id + " 的说明还是中文");
        }
    });

    [Fact]
    public void ChineseStillWorks()
    {
        var was = L.Chinese;
        try
        {
            L.Chinese = true;
            Assert.Equal("这台电脑", new CategoryViewModel(CategoryViewModel.MachineKey).Title);
            Assert.True(TaskCatalog.BuildFresh().All(t => t.Metadata.DisplayName.Length > 0));
        }
        finally { L.Chinese = was; }
    }

    [Fact]
    public void JoinUsesTheRightSeparator()
    {
        var was = L.Chinese;
        try
        {
            L.Chinese = true;
            Assert.Equal("a、b", L.Join("a", "b"));
            L.Chinese = false;
            Assert.Equal("a, b", L.Join("a", "b"));
        }
        finally { L.Chinese = was; }
    }
}
