using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using WinHomestead.Core.Abstractions;

namespace WinHomestead.Native;

/// <summary>
/// 休眠开关：读注册表 HibernateEnabled，写走 powercfg /hibernate（唯一受支持的方式，需管理员）。
/// 空闲超时：读走 powrprof 的 PowerReadACValueIndex / PowerReadDCValueIndex（拿到的是秒，精确且与语言无关），
/// 写走 powercfg /change（文档化命令，省去 PowerWriteACValueIndex + PowerSetActiveScheme 的一整套调用）。
/// </summary>
public sealed class WindowsPower : IPower
{
    private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Power";

    // powrprof 公开的电源设置 GUID
    private static readonly Guid VideoSubgroup = new("7516b95f-f776-4464-8c53-06167f40cc99");
    private static readonly Guid VideoPowerdownTimeout = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
    private static readonly Guid SleepSubgroup = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    private static readonly Guid StandbyTimeout = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");

    private readonly IRegistry _registry;

    public WindowsPower(IRegistry registry) { _registry = registry; }

    public bool IsHibernateEnabled()
    {
        var (v, _) = _registry.GetValue(RegRoot.LocalMachine, PowerKey, "HibernateEnabled");
        return v != null && Convert.ToInt64(v) != 0;
    }

    public void SetHibernate(bool enabled) => Powercfg("/hibernate " + (enabled ? "on" : "off"));

    public PowerTimeouts? ReadTimeouts()
    {
        var scheme = IntPtr.Zero;
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, ref scheme) != 0 || scheme == IntPtr.Zero) return null;
            var guid = (Guid)Marshal.PtrToStructure(scheme, typeof(Guid))!;

            var monitorAc = ReadAc(guid, VideoSubgroup, VideoPowerdownTimeout);
            var monitorDc = ReadDc(guid, VideoSubgroup, VideoPowerdownTimeout);
            var standbyAc = ReadAc(guid, SleepSubgroup, StandbyTimeout);
            var standbyDc = ReadDc(guid, SleepSubgroup, StandbyTimeout);
            if (monitorAc == null || monitorDc == null || standbyAc == null || standbyDc == null) return null;
            return new PowerTimeouts(monitorAc.Value, monitorDc.Value, standbyAc.Value, standbyDc.Value);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (scheme != IntPtr.Zero) LocalFree(scheme);
        }
    }

    public void SetAcTimeouts(int monitorMinutes, int standbyMinutes)
    {
        Powercfg("/change monitor-timeout-ac " + monitorMinutes.ToString(CultureInfo.InvariantCulture));
        Powercfg("/change standby-timeout-ac " + standbyMinutes.ToString(CultureInfo.InvariantCulture));
    }

    // ---- 内部 ----

    private static void Powercfg(string arguments)
    {
        var psi = new ProcessStartInfo("powercfg.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 powercfg");
        var err = p.StandardError.ReadToEnd();
        var outp = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } throw new TimeoutException("powercfg 超时"); }
        if (p.ExitCode != 0)
            throw new Win32Exception(p.ExitCode, "powercfg 退出码 " + p.ExitCode + ": " + (err.Length > 0 ? err : outp).Trim());
    }

    private static int? ReadAc(Guid scheme, Guid subgroup, Guid setting)
    {
        uint value = 0;
        return PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, ref value) == 0 ? (int)value : (int?)null;
    }

    private static int? ReadDc(Guid scheme, Guid subgroup, Guid setting)
    {
        uint value = 0;
        return PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroup, ref setting, ref value) == 0 ? (int)value : (int?)null;
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, ref IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid powerSettingGuid, ref uint valueIndex);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid, ref Guid powerSettingGuid, ref uint valueIndex);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
