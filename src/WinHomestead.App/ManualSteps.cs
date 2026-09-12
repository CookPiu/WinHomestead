using System.Collections.Generic;
using System.Linq;
using WinHomestead.Core.Models;
using WinHomestead.Core.Infrastructure;

namespace WinHomestead.App;

/// <summary>
/// “需要你手动处理的事项”：任务执行中产生的（同名文件未移动等）+ 按探测结果给出的应用内设置步骤。
/// 执行记录页展示它，主窗口的“执行记录”按钮上显示它的数量，让没进过记录页的用户也知道有东西要看。
/// </summary>
public static class ManualSteps
{
    public static List<string> Collect(AppServices services, ExecutionResult? result)
    {
        var steps = new List<string>();
        if (result != null) steps.AddRange(result.Results.SelectMany(x => x.ManualSteps));

        var s = services.Session.Snapshot;
        if (s == null) return steps;
        if (s.OneDrive.DesktopProtected) steps.Add(L.S(
            "桌面由 OneDrive 备份接管：如需迁移桌面，先在 OneDrive 设置 → 同步和备份 → 管理备份 中停止桌面备份，再点“重新探测”。",
            "The Desktop is covered by OneDrive backup. To relocate it, turn off Desktop backup under OneDrive settings → Sync and backup → Manage backup, then hit Re-probe."));
        if (s.HasTool("docker")) steps.Add(L.S(
            "Docker Desktop：在 Settings → Resources → Advanced 中把 Disk image location 改到数据盘 VMs\\docker。",
            "Docker Desktop: point Disk image location at VMs\\docker on the data drive, under Settings → Resources → Advanced."));
        // 只对已经装了的工具提；开荒工具不代为迁移使用中的工具，只把步骤列出来
        if (s.HasTool("maven")) steps.Add(L.S(
            @"Maven 已安装：本机仓库位置只能改 %USERPROFILE%\.m2\settings.xml 的 <localRepository>，工具不代改。想搬到数据盘就把它指向 DevCache\m2-repository，已下载的 jar 会重新拉。",
            @"Maven is installed: the local repository path lives in <localRepository> inside %USERPROFILE%\.m2\settings.xml and this tool won't edit it. Point it at DevCache\m2-repository on the data drive if you want it moved; the jars will be fetched again."));
        if (s.HasTool("android")) steps.Add(L.S(
            "Android SDK 已安装：SDK 与 AVD 镜像动辄几十 GB，改 ANDROID_HOME 不会把它们搬过去。要迁移就在 Android Studio 的 SDK Manager 里改位置并手动移动目录。",
            "Android SDK is installed: the SDK and AVD images run to tens of gigabytes, and changing ANDROID_HOME does not move them. To relocate, set the new path in Android Studio's SDK Manager and move the folders by hand."));
        return steps;
    }

    /// <summary>当前会话的结果（未执行过任何项且有上次复核结果时用上次的）。</summary>
    public static ExecutionResult CurrentResult(AppServices services)
    {
        var runner = services.Runner;
        var previous = services.Session.PreviousResult;
        return runner.Results.Count == 0 && previous != null
            ? previous
            : new ExecutionResult(runner.SessionId, runner.StartedAt, System.DateTime.Now, false, runner.RebootPending, runner.Results);
    }
}
