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
        foreach (var e in Entries()) set.Add(e.DisplayName);
        var list = new List<string>(set);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    /// <summary>带发行商的条目，供 OEM 预装软件检查使用；同名不同发行商的都保留。</summary>
    public IReadOnlyList<InstalledProgram> Entries()
    {
        var list = new List<InstalledProgram>();
        foreach (var (root, key) in Sources)
        {
            IReadOnlyList<string> subs;
            try { subs = _reg.GetSubKeyNames(root, key); } catch { continue; }
            foreach (var sub in subs)
            {
                try
                {
                    var path = key + "\\" + sub;
                    var name = _reg.GetValue(root, path, "DisplayName").Value?.ToString();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var publisher = _reg.GetValue(root, path, "Publisher").Value?.ToString();
                    list.Add(new InstalledProgram(name!, publisher));
                }
                catch { }
            }
        }
        return list;
    }
}

public sealed record InstalledProgram(string DisplayName, string? Publisher);
