using System;
using System.Runtime.InteropServices;
using NewPcSetup.Core.Abstractions;

namespace NewPcSetup.Native;

/// <summary>直接读写注册表中的环境变量（不经 .NET 的 Environment 类，以便统一走 journal 与类型控制），写后广播 WM_SETTINGCHANGE。</summary>
public sealed class WindowsEnvironment : IEnvironment
{
    private const string UserKey = "Environment";
    private const string MachineKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    private readonly IRegistry _registry;

    public WindowsEnvironment(IRegistry registry) { _registry = registry; }

    public string? Get(EnvScope scope, string name)
    {
        var (v, _) = _registry.GetValue(Root(scope), Key(scope), name);
        return v?.ToString();
    }

    public void Set(EnvScope scope, string name, string? value)
    {
        if (value == null) _registry.DeleteValue(Root(scope), Key(scope), name);
        else _registry.SetValue(Root(scope), Key(scope), name, value, value.IndexOf('%') >= 0 ? RegKind.ExpandString : RegKind.String);
        Broadcast();
    }

    private static RegRoot Root(EnvScope s) => s == EnvScope.User ? RegRoot.CurrentUser : RegRoot.LocalMachine;
    private static string Key(EnvScope s) => s == EnvScope.User ? UserKey : MachineKey;

    private static void Broadcast()
    {
        try
        {
            SendMessageTimeout(new IntPtr(0xffff), 0x001A, IntPtr.Zero, "Environment", 0x0002, 3000, out _);
        }
        catch { /* 广播失败不影响已写入的值 */ }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
}
