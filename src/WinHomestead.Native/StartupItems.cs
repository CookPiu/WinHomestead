using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WinHomestead.Core.Abstractions;

namespace WinHomestead.Native;

/// <summary>自启项的来源。决定它记在哪个 StartupApproved 子键下，以及要不要管理员权限。</summary>
public enum StartupSource { UserRun, MachineRun, UserStartupFolder }

/// <summary>
/// 一条自启项。Name 是 Run 键里的值名或快捷方式文件名，Command 是它启动的东西。
/// </summary>
public sealed record StartupItem(string Name, string Command, StartupSource Source, bool Enabled)
{
    public bool NeedsAdmin => Source == StartupSource.MachineRun;

    public string SourceName => Source switch
    {
        StartupSource.UserRun => "当前用户",
        StartupSource.MachineRun => "所有用户",
        _ => "启动文件夹",
    };
}

/// <summary>
/// 开机自启项的枚举与开关。
///
/// 启用状态不看 Run 键本身，而看 StartupApproved 下的同名值：任务管理器禁用一项时并不删除 Run 键，
/// 而是在这里写一个 12 字节的值，首字节 bit0 置位表示已禁用（02/06 启用，03/07 禁用），
/// 后 8 字节是禁用时刻的 FILETIME。StartupApproved 里没有记录的项按启用算。
///
/// 这也是 Win32_StartupCommand 不能直接用来数"有几个自启"的原因——它只列 Run 键与启动文件夹里的
/// 原始条目，被任务管理器禁用的项照列不误。
/// </summary>
public sealed class StartupItems
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedFolder = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    private static readonly byte[] EnabledBytes = { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    private readonly IRegistry _reg;
    private readonly ILogger _log;

    public StartupItems(IRegistry reg, ILogger log) { _reg = reg; _log = log; }

    public IReadOnlyList<StartupItem> Enumerate()
    {
        var list = new List<StartupItem>();
        list.AddRange(FromRun(RegRoot.CurrentUser, StartupSource.UserRun));
        list.AddRange(FromRun(RegRoot.LocalMachine, StartupSource.MachineRun));
        list.AddRange(FromStartupFolder());
        return list.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>开或关一项。写的是 StartupApproved，Run 键与快捷方式本身一动不动，随时可以开回来。</summary>
    public void SetEnabled(IRegistry registry, StartupItem item, bool enabled)
    {
        var root = item.Source == StartupSource.MachineRun ? RegRoot.LocalMachine : RegRoot.CurrentUser;
        var key = item.Source == StartupSource.UserStartupFolder ? ApprovedFolder : ApprovedRun;
        registry.SetValue(root, key, item.Name, enabled ? EnabledBytes : DisabledBytes(), RegKind.Binary);
    }

    private static byte[] DisabledBytes()
    {
        var bytes = new byte[12];
        bytes[0] = 0x03;
        BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(bytes, 4);
        return bytes;
    }

    // ---- 内部 ----

    private IEnumerable<StartupItem> FromRun(RegRoot root, StartupSource source)
    {
        var items = new List<StartupItem>();
        try
        {
            foreach (var name in _reg.GetValueNames(root, RunKey))
            {
                var (v, _) = _reg.GetValue(root, RunKey, name);
                items.Add(new StartupItem(name, v?.ToString() ?? string.Empty, source, IsEnabled(root, ApprovedRun, name)));
            }
        }
        catch (Exception ex) { _log.Warn($"读取 {root} Run 键失败: " + ex.Message); }
        return items;
    }

    private IEnumerable<StartupItem> FromStartupFolder()
    {
        var items = new List<StartupItem>();
        try
        {
            var dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (!Directory.Exists(dir)) return items;
            foreach (var path in Directory.GetFiles(dir))
            {
                var name = Path.GetFileName(path);
                if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(new StartupItem(name, path, StartupSource.UserStartupFolder,
                    IsEnabled(RegRoot.CurrentUser, ApprovedFolder, name)));
            }
        }
        catch (Exception ex) { _log.Warn("读取启动文件夹失败: " + ex.Message); }
        return items;
    }

    /// <summary>StartupApproved 里没有记录就是启用；有记录时看首字节的 bit0。</summary>
    private bool IsEnabled(RegRoot root, string approvedKey, string name)
    {
        try
        {
            var (v, _) = _reg.GetValue(root, approvedKey, name);
            if (v is byte[] bytes && bytes.Length > 0) return (bytes[0] & 0x01) == 0;
        }
        catch (Exception ex) { _log.Warn($"读取 {name} 的自启状态失败: " + ex.Message); }
        return true;
    }
}
