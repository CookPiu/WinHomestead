using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using NewPcSetup.Core.Abstractions;

namespace NewPcSetup.Native;

public sealed class WindowsShell : IShell
{
    private static readonly Dictionary<KnownFolder, Guid> Ids = new()
    {
        [KnownFolder.Desktop] = new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"),
        [KnownFolder.Documents] = new Guid("FDD39AD0-238F-46AF-ADB4-6C85480369C7"),
        [KnownFolder.Downloads] = new Guid("374DE290-123F-4565-9164-39C4925E467B"),
        [KnownFolder.Pictures] = new Guid("33E28130-4E1E-4676-835A-98395C3BC3BB"),
        [KnownFolder.Videos] = new Guid("18989B1D-99B5-455B-841C-AB7C74E4DDFC"),
        [KnownFolder.Music] = new Guid("4BD8D571-6D19-48D3-BE97-422220080E43"),
    };

    public string? GetKnownFolderPath(KnownFolder folder)
    {
        var id = Ids[folder];
        var hr = SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var p);
        if (hr != 0 || p == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    public void SetKnownFolderPath(KnownFolder folder, string path)
    {
        Directory.CreateDirectory(path);
        var id = Ids[folder];
        var hr = SHSetKnownFolderPath(ref id, 0, IntPtr.Zero, path);
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero); // SHCNE_ASSOCCHANGED
    }

    public MoveResult MoveContents(string source, string target)
    {
        if (!Directory.Exists(source)) return new MoveResult(0, Array.Empty<string>());
        Directory.CreateDirectory(target);
        var skipped = new List<string>();
        var moved = 0;
        var sameVolume = string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase);

        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var name = Path.GetFileName(entry);
            if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            var dest = Path.Combine(target, name);
            if (File.Exists(dest) || Directory.Exists(dest)) { skipped.Add(entry); continue; }

            if (Directory.Exists(entry))
            {
                if (sameVolume) Directory.Move(entry, dest);
                else { CopyDirectory(entry, dest); Directory.Delete(entry, true); }
            }
            else
            {
                File.Move(entry, dest);
            }
            moved++;
        }
        return new MoveResult(moved, skipped);
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), false);
        foreach (var d in Directory.GetDirectories(src)) CopyDirectory(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    public void RestartExplorer()
    {
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            try { p.Kill(); p.WaitForExit(5000); } catch { /* 忽略已退出的进程 */ }
            finally { p.Dispose(); }
        }
        Thread.Sleep(500);
        if (Process.GetProcessesByName("explorer").Length == 0)
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
    }

    // ---- 快速访问：通过 Shell.Application 的 IDispatch 调用，不引入互操作程序集，也不用 dynamic ----

    private const string QuickAccessNamespace = "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}";

    public bool IsPinnedToQuickAccess(string path)
    {
        var item = FindQuickAccessItem(path, out var shell);
        try { return item != null; }
        finally { Release(item); Release(shell); }
    }

    public void PinToQuickAccess(string path)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
        object? shell = null, folder = null, self = null;
        try
        {
            shell = CreateShell();
            folder = Invoke(shell, "NameSpace", path) ?? throw new InvalidOperationException("Shell.NameSpace 返回空: " + path);
            self = Get(folder, "Self") ?? throw new InvalidOperationException("Folder.Self 返回空");
            Invoke(self, "InvokeVerb", "pintohome");
        }
        finally { Release(self); Release(folder); Release(shell); }
    }

    public void UnpinFromQuickAccess(string path)
    {
        var item = FindQuickAccessItem(path, out var shell);
        try { if (item != null) Invoke(item, "InvokeVerb", "unpinfromhome"); }
        finally { Release(item); Release(shell); }
    }

    /// <summary>在“主文件夹”命名空间中查找已固定（System.Home.IsPinned）的目录项；“常用文件夹”不算固定。</summary>
    private static object? FindQuickAccessItem(string path, out object? shell)
    {
        shell = null;
        object? folder = null, items = null;
        try
        {
            shell = CreateShell();
            folder = Invoke(shell, "NameSpace", QuickAccessNamespace);
            if (folder == null) return null;
            items = Invoke(folder, "Items");
            if (items == null) return null;
            var count = Convert.ToInt32(Get(items, "Count"));
            var full = Path.GetFullPath(path).TrimEnd('\\');
            for (var i = 0; i < count; i++)
            {
                var item = Invoke(items, "Item", i);
                if (item == null) continue;
                var isFolder = Get(item, "IsFolder") as bool? ?? false;
                var itemPath = (Get(item, "Path") as string ?? string.Empty).TrimEnd('\\');
                if (isFolder && string.Equals(itemPath, full, StringComparison.OrdinalIgnoreCase))
                {
                    var pinned = true;
                    try { if (Invoke(item, "ExtendedProperty", "System.Home.IsPinned") is bool b) pinned = b; } catch { }
                    if (pinned) return item;
                }
                Release(item);
            }
            return null;
        }
        finally { Release(items); Release(folder); }
    }

    private static object CreateShell()
    {
        var type = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("Shell.Application 不可用");
        return Activator.CreateInstance(type) ?? throw new InvalidOperationException("无法创建 Shell.Application");
    }

    private static object? Invoke(object target, string method, params object[] args)
        => target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);

    private static object? Get(object target, string property)
        => target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null);

    private static void Release(object? com)
    {
        if (com != null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHSetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, string pszPath);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}

public sealed class WindowsFileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public bool IsDirectoryEmpty(string path)
    {
        using var e = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
        return !e.MoveNext();
    }
    public void DeleteEmptyDirectory(string path) => Directory.Delete(path, false);

    public IReadOnlyList<FileEntry> FilesOlderThan(string dir, DateTime before)
    {
        var list = new List<FileEntry>();
        if (!Directory.Exists(dir)) return list;
        Walk(new DirectoryInfo(dir), before, list);
        return list;
    }

    private static void Walk(DirectoryInfo dir, DateTime before, List<FileEntry> acc)
    {
        try
        {
            foreach (var f in dir.EnumerateFiles())
            {
                try { if (f.LastWriteTime < before) acc.Add(new FileEntry(f.FullName, f.Length)); } catch { }
            }
            foreach (var d in dir.EnumerateDirectories())
            {
                try { if ((d.Attributes & FileAttributes.ReparsePoint) == 0) Walk(d, before, acc); } catch { }
            }
        }
        catch { /* 无权限的子目录跳过 */ }
    }

    public bool TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return true;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            return true;
        }
        catch { return false; }
    }

}
