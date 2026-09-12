using System;
using System.Diagnostics;
using System.Windows;
using WinHomestead.Core.Abstractions;
using WinHomestead.Core.Infrastructure;

namespace WinHomestead.App;

/// <summary>UC-11：写 HKCU RunOnce 指向 exe --resume，然后延时重启；重启后自动拉起并复核需重启项。</summary>
public static class RebootHelper
{
    public static void RebootWithResume(AppServices services)
    {
        var ok = MessageBox.Show(L.S(
                "将在 15 秒后重启电脑。重启后本工具会自动打开并确认改动已生效。\n请先保存其他程序中的工作。",
                "The PC will restart in 15 seconds. This tool reopens afterwards to confirm the changes took effect.\nSave your work in other programs first."),
            L.S("开荒", "WinHomestead"),
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (ok != MessageBoxResult.OK) return;
        try
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName ?? throw new InvalidOperationException("无法取得程序路径");
            services.Execution.Registry.SetValue(RegRoot.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "WinHomestead", $"\"{exe}\" --resume", RegKind.String);
            services.Logger.Info("已写入 RunOnce，准备重启");
            Process.Start(new ProcessStartInfo("shutdown.exe",
                L.S("/r /t 15 /c \"开荒：重启以完成设置\"", "/r /t 15 /c \"WinHomestead: restarting to finish setup\"")) { UseShellExecute = false, CreateNoWindow = true });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            services.Logger.Error("安排重启失败", ex);
            MessageBox.Show(L.S("安排重启失败：", "Couldn't schedule the restart: ") + ex.Message,
                L.S("开荒", "WinHomestead"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
