using System;
using System.Collections.Generic;
using NewPcSetup.Core.Abstractions;

namespace NewPcSetup.Native;

/// <summary>读取三处 Uninstall 键的 DisplayName，用于软件入口页的"已安装"标记。</summary>
public sealed class InstalledPrograms
{
    private static readonly (RegRoot Root, string Key)[] Sources =
    {
        (RegRoot.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegRoot.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
    };

    private readonly IRegistry _reg;
    public InstalledPrograms(IRegistry reg) { _reg = reg; }

    public IReadOnlyList<string> DisplayNames()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (root, key) in Sources)
        {
            IReadOnlyList<string> subs;
            try { subs = _reg.GetSubKeyNames(root, key); } catch { continue; }
            foreach (var sub in subs)
            {
                try
                {
                    var name = _reg.GetValue(root, key + "\\" + sub, "DisplayName").Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(name)) set.Add(name!);
                }
                catch { }
            }
        }
        var list = new List<string>(set);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }
}
