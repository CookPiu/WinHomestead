using System;
using System.Management;
using System.Security.Principal;

namespace NewPcSetup.Native;

public static class ProcessInfo
{
    public static bool IsAdministrator()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>提权后 HKCU 归属校验：explorer.exe 的所有者 SID 应与当前进程一致。无 explorer 时无法判断，返回 true。</summary>
    public static bool ExplorerOwnedByCurrentUser()
    {
        try
        {
            using var me = WindowsIdentity.GetCurrent();
            var mySid = me.User?.Value;
            if (mySid == null) return true;
            using var searcher = new ManagementObjectSearcher("SELECT ProcessId FROM Win32_Process WHERE Name = 'explorer.exe'");
            foreach (ManagementObject p in searcher.Get())
            {
                using (p)
                {
                    var outParams = p.InvokeMethod("GetOwnerSid", null, null);
                    var sid = outParams?["Sid"]?.ToString();
                    if (sid != null) return string.Equals(sid, mySid, StringComparison.OrdinalIgnoreCase);
                }
            }
            return true;
        }
        catch { return true; }
    }

    public static bool IsWindows11OrLater() => Environment.OSVersion.Version.Build >= 22000;
}
