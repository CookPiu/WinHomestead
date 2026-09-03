using System;
using System.ComponentModel;
using System.Diagnostics;
using NewPcSetup.Core.Abstractions;

namespace NewPcSetup.Native;

/// <summary>休眠开关：读注册表 HibernateEnabled，写走 powercfg /hibernate（唯一受支持的方式，需管理员）。</summary>
public sealed class WindowsPower : IPower
{
    private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Power";
    private readonly IRegistry _registry;

    public WindowsPower(IRegistry registry) { _registry = registry; }

    public bool IsHibernateEnabled()
    {
        var (v, _) = _registry.GetValue(RegRoot.LocalMachine, PowerKey, "HibernateEnabled");
        return v != null && Convert.ToInt64(v) != 0;
    }

    public void SetHibernate(bool enabled)
    {
        var psi = new ProcessStartInfo("powercfg.exe", "/hibernate " + (enabled ? "on" : "off"))
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
}
