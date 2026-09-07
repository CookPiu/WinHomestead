using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using WinHomestead.Core.Abstractions;
using WinHomestead.Core.Models;

namespace WinHomestead.Native;

/// <summary>只读探测。每一部分独立 try/catch，单项失败不影响整体。</summary>
public sealed class SnapshotCollector
{
    private static readonly Regex Sensitive = new("KEY|TOKEN|SECRET|PASS|PWD|CREDENTIAL|AUTH|COOKIE", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private const string CurrentVersion = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    private readonly IRegistry _reg;
    private readonly IShell _shell;
    private readonly ILogger _log;

    public SnapshotCollector(IRegistry reg, IShell shell, ILogger log) { _reg = reg; _shell = shell; _log = log; }

    /// <summary>只做快的部分：注册表、WMI、卷、已知文件夹、工具探测，通常一秒内完成。不做任何目录体积扫描。</summary>
    public EnvironmentSnapshot Collect()
    {
        var systemDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');
        var displayVersion = Str(RegRoot.LocalMachine, CurrentVersion, "DisplayVersion") ?? string.Empty;
        var build = int.TryParse(Str(RegRoot.LocalMachine, CurrentVersion, "CurrentBuildNumber"), out var b) ? b : 0;
        var edition = Str(RegRoot.LocalMachine, CurrentVersion, "EditionID") ?? string.Empty;
        var caption = $"Windows {(build >= 22000 ? "11" : "10")} {edition} {displayVersion}".Trim();
        var installDate = DateTimeOffset.FromUnixTimeSeconds(Int(RegRoot.LocalMachine, CurrentVersion, "InstallDate")).LocalDateTime;

        var (manufacturer, model, ramBytes, isLaptop, domain) = ComputerSystem();
        var volumes = Volumes(systemDrive);
        var dataDrive = volumes.Where(v => !v.IsSystem).OrderByDescending(v => v.SizeBytes).Select(v => v.DriveLetter).FirstOrDefault();

        return new EnvironmentSnapshot(
            OsCaption: caption,
            DisplayVersion: displayVersion,
            Build: build,
            IsLaptop: isLaptop,
            RamBytes: ramBytes,
            Cpu: Cpu(),
            Manufacturer: manufacturer,
            Model: model,
            IsMdmEnrolled: MdmEnrolled(),
            IsDomainJoined: domain,
            ProxyEnabled: Int(RegRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyEnable") == 1,
            Disks: Disks(),
            Volumes: volumes,
            SystemDrive: systemDrive,
            DataDrive: dataDrive,
            InstallDate: installDate,
            OneDrive: OneDrive(),
            Gpu: Gpu(),
            KnownFolders: KnownFolders(),
            UserEnvironment: UserEnvironment(),
            Tools: Tools(),
            TakenAt: DateTime.Now);
    }

    // ---- 各部分 ----

    /// <summary>内存返回原始字节，展示层自己决定精度；这里不做任何取整。</summary>
    private (string, string, long, bool, bool) ComputerSystem()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Manufacturer, Model, TotalPhysicalMemory, PCSystemType, PartOfDomain FROM Win32_ComputerSystem");
            foreach (ManagementObject o in s.Get())
            {
                using (o)
                {
                    var ram = Convert.ToInt64(o["TotalPhysicalMemory"]);
                    var laptop = Convert.ToInt32(o["PCSystemType"]) == 2 || IsPortableChassis();
                    return (o["Manufacturer"]?.ToString() ?? "", o["Model"]?.ToString() ?? "", ram, laptop, Convert.ToBoolean(o["PartOfDomain"]));
                }
            }
        }
        catch (Exception ex) { _log.Warn("Win32_ComputerSystem 失败: " + ex.Message); }
        return ("", "", 0L, false, false);
    }

    /// <summary>多路 CPU 少见，取第一颗即可；核心数取全部之和。</summary>
    private CpuInfo Cpu()
    {
        try
        {
            var name = string.Empty;
            var cores = 0;
            var logical = 0;
            using var s = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            foreach (ManagementObject o in s.Get())
                using (o)
                {
                    if (name.Length == 0) name = (o["Name"]?.ToString() ?? string.Empty).Trim();
                    cores += Convert.ToInt32(o["NumberOfCores"] ?? 0);
                    logical += Convert.ToInt32(o["NumberOfLogicalProcessors"] ?? 0);
                }
            return new CpuInfo(name, cores, logical);
        }
        catch (Exception ex) { _log.Warn("Win32_Processor 失败: " + ex.Message); return new CpuInfo(string.Empty, 0, 0); }
    }

    private bool IsPortableChassis()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure");
            foreach (ManagementObject o in s.Get())
                using (o)
                    if (o["ChassisTypes"] is ushort[] types && types.Any(t => t is 8 or 9 or 10 or 14 or 30 or 31 or 32)) return true;
        }
        catch { }
        return false;
    }

    private List<VolumeInfo> Volumes(string systemDrive)
    {
        var list = new List<VolumeInfo>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                var letter = d.Name.TrimEnd('\\');
                list.Add(new VolumeInfo(letter, d.VolumeLabel, d.TotalSize, d.AvailableFreeSpace,
                    string.Equals(letter, systemDrive, StringComparison.OrdinalIgnoreCase)));
            }
            catch { }
        }
        return list;
    }

    private List<DiskInfo> Disks()
    {
        var list = new List<DiskInfo>();
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Index, Model, Size, InterfaceType FROM Win32_DiskDrive");
            foreach (ManagementObject o in s.Get())
                using (o)
                    list.Add(new DiskInfo(Convert.ToInt32(o["Index"]), o["Model"]?.ToString() ?? "", Convert.ToInt64(o["Size"] ?? 0L), o["InterfaceType"]?.ToString() ?? ""));
        }
        catch (Exception ex) { _log.Warn("Win32_DiskDrive 失败: " + ex.Message); }
        return list.OrderBy(d => d.Number).ToList();
    }

    private bool MdmEnrolled()
    {
        try
        {
            const string key = @"SOFTWARE\Microsoft\Enrollments";
            foreach (var sub in _reg.GetSubKeyNames(RegRoot.LocalMachine, key))
                if (!string.IsNullOrEmpty(Str(RegRoot.LocalMachine, key + "\\" + sub, "ProviderID"))) return true;
        }
        catch { }
        return false;
    }

    private OneDriveInfo OneDrive()
    {
        var installed = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft OneDrive", "OneDrive.exe"))
                        || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "OneDrive", "OneDrive.exe"));
        const string personal = @"Software\Microsoft\OneDrive\Accounts\Personal";
        var signedIn = _reg.KeyExists(RegRoot.CurrentUser, personal) || _reg.KeyExists(RegRoot.CurrentUser, @"Software\Microsoft\OneDrive\Accounts\Business1");
        var mask = (int)Int(RegRoot.CurrentUser, personal, "KfmFoldersProtectedNow");
        return new OneDriveInfo(installed, signedIn, mask);
    }

    private GpuInfo Gpu()
    {
        var names = new List<string>();
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (ManagementObject o in s.Get()) using (o) { var n = o["Name"]?.ToString(); if (!string.IsNullOrEmpty(n)) names.Add(n!); }
        }
        catch { }
        var hags = Int(RegRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode") == 2;
        return new GpuInfo(names, hags);
    }

    private Dictionary<string, string> KnownFolders()
    {
        var d = new Dictionary<string, string>();
        foreach (KnownFolder f in Enum.GetValues(typeof(KnownFolder)))
        {
            try { var p = _shell.GetKnownFolderPath(f); if (p != null) d[f.ToString()] = p; } catch { }
        }
        return d;
    }

    private Dictionary<string, string> UserEnvironment()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var name in _reg.GetValueNames(RegRoot.CurrentUser, "Environment"))
            {
                if (Sensitive.IsMatch(name)) continue;
                var (v, _) = _reg.GetValue(RegRoot.CurrentUser, "Environment", name);
                if (v != null) d[name] = v.ToString() ?? string.Empty;
            }
        }
        catch { }
        return d;
    }

    private List<DetectedTool> Tools()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(';').Where(p => p.Length > 0).ToList();
        bool OnPath(params string[] exes) => exes.Any(e => pathDirs.Any(dir => { try { return File.Exists(Path.Combine(dir.Trim('"'), e)); } catch { return false; } }));
        bool Dir(params string[] parts) => Directory.Exists(Path.Combine(new[] { home }.Concat(parts).ToArray()));
        string? Env(string n) => Environment.GetEnvironmentVariable(n, EnvironmentVariableTarget.User);
        bool Set(string n) => !string.IsNullOrEmpty(Env(n));

        var list = new List<DetectedTool>
        {
            new("pip", "Python / pip", OnPath("python.exe", "py.exe") || Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python")) || Set("PIP_CACHE_DIR"), Env("PIP_CACHE_DIR")),
            new("uv", "uv", OnPath("uv.exe") || Set("UV_CACHE_DIR"), Env("UV_CACHE_DIR")),
            new("npm", "npm", OnPath("npm.cmd") || Set("npm_config_cache"), Env("npm_config_cache")),
            new("pnpm", "pnpm", OnPath("pnpm.cmd", "pnpm.exe") || Set("PNPM_HOME"), Env("PNPM_HOME")),
            new("yarn", "Yarn", OnPath("yarn.cmd") || Set("YARN_CACHE_FOLDER"), Env("YARN_CACHE_FOLDER")),
            new("gradle", "Gradle", Dir(".gradle") || OnPath("gradle.bat") || Set("GRADLE_USER_HOME"), Env("GRADLE_USER_HOME")),
            new("maven", "Maven", Dir(".m2") || OnPath("mvn.cmd"), null),
            new("nuget", "NuGet", Dir(".nuget") || Set("NUGET_PACKAGES"), Env("NUGET_PACKAGES")),
            new("cargo", "Cargo / Rust", Dir(".cargo") || OnPath("cargo.exe") || Set("CARGO_HOME"), Env("CARGO_HOME")),
            new("go", "Go", OnPath("go.exe") || Set("GOPATH"), Env("GOPATH")),
            new("pub", "Flutter / Dart", OnPath("flutter.bat", "dart.exe") || Set("PUB_CACHE"), Env("PUB_CACHE")),
            new("hf", "Hugging Face", Dir(".cache", "huggingface") || Set("HF_HOME"), Env("HF_HOME")),
            new("ollama", "Ollama", OnPath("ollama.exe") || Set("OLLAMA_MODELS"), Env("OLLAMA_MODELS")),
            new("android", "Android SDK / AVD", Set("ANDROID_HOME") || Set("ANDROID_SDK_ROOT") || OnPath("adb.exe")
                || Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk")), Env("ANDROID_HOME")),
            new("conda", "Conda", OnPath("conda.exe", "conda.bat") || File.Exists(Path.Combine(home, ".condarc")), null),
            new("docker", "Docker Desktop", File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "Docker Desktop.exe")), null),
        };
        return list;
    }

    // ---- 注册表小工具 ----

    private string? Str(RegRoot root, string key, string name) { try { return _reg.GetValue(root, key, name).Value?.ToString(); } catch { return null; } }
    private long Int(RegRoot root, string key, string name)
    {
        try { var v = _reg.GetValue(root, key, name).Value; return v == null ? 0 : Convert.ToInt64(v); } catch { return 0; }
    }
}
