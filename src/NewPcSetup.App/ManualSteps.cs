using System.Collections.Generic;
using System.Linq;
using NewPcSetup.Core.Models;

namespace NewPcSetup.App;

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
        if (s.OneDrive.DesktopProtected) steps.Add("桌面由 OneDrive 备份接管：如需迁移桌面，先在 OneDrive 设置 → 同步和备份 → 管理备份 中停止桌面备份，再点“重新探测”。");
        if (s.HasTool("docker")) steps.Add("Docker Desktop：在 Settings → Resources → Advanced 中把 Disk image location 改到数据盘 VMs\\docker。");
        // 只对已经装了的工具提；开荒工具不代为迁移使用中的工具，只把步骤列出来
        if (s.HasTool("maven")) steps.Add(@"Maven 已安装：本机仓库位置只能改 %USERPROFILE%\.m2\settings.xml 的 <localRepository>，工具不代改。想搬到数据盘就把它指向 DevCache\m2-repository，已下载的 jar 会重新拉。");
        if (s.HasTool("android")) steps.Add("Android SDK 已安装：SDK 与 AVD 镜像动辄几十 GB，改 ANDROID_HOME 不会把它们搬过去。要迁移就在 Android Studio 的 SDK Manager 里改位置并手动移动目录。");
        var phone = s.LargeItems.FirstOrDefault(i => i.Category == "phonelink" && i.SizeGb >= 1);
        if (phone != null) steps.Add($"手机连接缓存占用 {phone.SizeGb:F1} GB（{phone.Path}）：可在“手机连接”应用设置中清理或断开设备。");
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
