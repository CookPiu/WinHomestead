using System.Threading;
using WinHomestead.Core.Abstractions;
using WinHomestead.Core.Infrastructure;
using WinHomestead.Tasks;
using Xunit;

namespace WinHomestead.Core.Tests;

public class DevDriveSuggestTaskTests
{
    private const long Gb = 1L << 30;

    [Fact]
    public void NtfsDataDrive_SuggestsVhdWhenNoUnallocatedSpace()
    {
        var (svc, _, _, _, _, _, storage) = TestData.ServicesFull();
        storage.FreeExtent = 0;
        var s = TestData.Snapshot();
        var task = new DevDriveSuggestTask();
        var ctx = svc.CreateContext(DevDriveSuggestTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        Assert.True(task.IsApplicable(s, TestData.Answers(s)));
        var d = task.Detect(ctx);
        Assert.False(d.Satisfied);
        Assert.Contains("NTFS", d.CurrentValue);
        Assert.Contains("VHDX", d.TargetValue);
    }

    [Fact]
    public void UnallocatedSpace_SuggestsNewVolume()
    {
        var (svc, _, _, _, _, _, storage) = TestData.ServicesFull();
        storage.Partitions["D:"] = new PartitionInfo(0, 3, "D:", 450 * Gb, 0);
        storage.FreeExtent = 120 * Gb;
        var s = TestData.Snapshot();
        var ctx = svc.CreateContext(DevDriveSuggestTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        var d = new DevDriveSuggestTask().Detect(ctx);
        Assert.False(d.Satisfied);
        Assert.Contains("未分配空间", d.TargetValue);
    }

    /// <summary>Windows 11 客户端上的 ReFS 卷只可能来自 Dev Drive，视为已满足。</summary>
    [Fact]
    public void RefsDataDrive_IsSatisfied()
    {
        var (svc, _, _, _, _, _, storage) = TestData.ServicesFull();
        storage.FileSystems["D:"] = "ReFS";
        var s = TestData.Snapshot();
        var ctx = svc.CreateContext(DevDriveSuggestTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        Assert.True(new DevDriveSuggestTask().Detect(ctx).Satisfied);
    }

    [Fact]
    public void NotListedWithoutDataDriveOrOnSmallMemory()
    {
        var task = new DevDriveSuggestTask();
        var single = TestData.SingleDiskFresh();
        Assert.False(task.IsApplicable(single, TestData.Answers(single)));

        var s = TestData.Snapshot();
        Assert.False(task.IsApplicable(s with { RamBytes = 4L * Gb }, TestData.Answers(s)));
    }

    /// <summary>MDM 机器仍然列出，但说明原因，而不是让条目凭空消失。</summary>
    [Fact]
    public void MdmEnrolled_ListedButNotApplicable()
    {
        var (svc, _, _, _, _, _, _) = TestData.ServicesFull();
        var s = TestData.Snapshot("D:", false, isLaptop: true, mdm: true);
        var task = new DevDriveSuggestTask();
        var ctx = svc.CreateContext(DevDriveSuggestTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        Assert.True(task.IsApplicable(s, TestData.Answers(s)));
        Assert.StartsWith(DetectResult.NotApplicable, task.Detect(ctx).Reason);
    }

    [Fact]
    public void Apply_WritesManualStepOnly()
    {
        var (svc, _, _, _, _, _, storage) = TestData.ServicesFull();
        storage.FreeExtent = 0;
        var s = TestData.Snapshot();
        var ctx = svc.CreateContext(DevDriveSuggestTask.Id, s, TestData.Answers(s), new InMemoryJournal(), CancellationToken.None);

        new DevDriveSuggestTask().Apply(ctx);

        Assert.Single(ctx.ManualSteps);
        Assert.Contains("不能就地转成 Dev Drive", ctx.ManualSteps[0]);
        Assert.Empty(storage.Calls);
    }
}
