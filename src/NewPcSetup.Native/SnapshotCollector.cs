using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Models;

namespace NewPcSetup.Native;

/// <summary>只读探测。每一部分独立 try/catch，单项失败不影响整体。</summary>
public sealed class SnapshotCollector
{
    private static readonly Regex Sensitive = new("KEY|TOKEN|SECRET|PASS|PWD|CREDENTIAL|AUTH|COOKIE", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private const string CurrentVersion = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    private readonly IRegistry _reg;
    private readonly IShell _shell;
    private readonly ILogger _log;

    public SnapshotCollector(IRegistry reg, IShell shell, ILogger log) { _reg = reg; _shell = shell; _log = log; }

    public EnvironmentSnapshot Collect()
    {
        var systemDrive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');
        // 大目录扫描最慢，与 WMI/注册表探测并行；自带时间预算，超时返回已完成部分
        var largeItems = Task.Run(() => LargeItems(systemDrive));

        var displayVersion = Str(RegRoot.LocalMachine, CurrentVersion, "DisplayVersion") ?? string.Empty;
        var build = int.TryParse(Str(RegRoot.LocalMachine, CurrentVersion, "CurrentBuildNumber"), out var b) ? b : 0;
        var edition = Str(RegRoot.LocalMachine, CurrentVersion, "EditionID") ?? string.Empty;
        var caption = $"Windows {(build >= 22000 ? "11" : "10")} {edition} {displayVersion}".Trim();
        var installDate = DateTimeOffset.FromUnixTimeSeconds(Int(RegRoot.LocalMachine, CurrentVersion, "InstallDate")).LocalDateTime;

        var (manufacturer, model, ramGb, isLaptop, domain) = ComputerSystem();
        var volumes = Volumes(systemDrive);
        var dataDrive = volumes.Where(v => !v.IsSystem).OrderByDescending(v => v.SizeBytes).Select(v => v.DriveLetter).FirstOrDefault();

        return new EnvironmentSnapshot(
            OsCaption: caption,
            DisplayVersion: displayVersion,
            Build: build,
            IsLaptop: isLaptop,
            RamGb: ramGb,
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
            LargeItems: largeItems.Result,
            TakenAt: DateTime.Now);
    }

    // ---- 各部分 ----

    private (string, string, int, bool, bool) ComputerSystem()
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Manufacturer, Model, TotalPhysicalMemory, PCSystemType, PartOfDomain FROM Win32_ComputerSystem");
            foreach (ManagementObject o in s.Get())
            {
                using (o)
                {
                    var ram = (int)Math.Ceiling(Convert.ToDouble(o["TotalPhysicalMemory"]) / 1073741824d); // 标称 32 GB 机器实际略少于 32 GiB，向上取整
                    var laptop = Convert.ToInt32(o["PCSystemType"]) == 2 || IsPortableChassis();
                    return (o["Manufacturer"]?.ToString() ?? "", o["Model"]?.ToString() ?? "", ram, laptop, Convert.ToBoolean(o["PartOfDomain"]));
                }
            }
        }
        catch (Exception ex) { _log.Warn("Win32_ComputerSystem 失败: " + ex.Message); }
        return ("", "", 0, false, false);
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

    /// <summary>目录体积扫描的共享截止时间；超过后 DirSize 停止下探，结果偏小并标记 Truncated。</summary>
    private sealed class ScanBudget
    {
        public ScanBudget(TimeSpan budget) { Deadline = DateTime.UtcNow + budget; }
        public DateTime Deadline { get; }
        public bool Truncated { get; set; }
        public bool Expired => DateTime.UtcNow > Deadline;
    }

    private static readonly TimeSpan LargeItemsBudget = TimeSpan.FromSeconds(15);
    private const long AppDataThreshold = 1L << 30;

    private List<LargeItem> LargeItems(string systemDrive)
    {
        var list = new List<LargeItem>();
        var budget = new ScanBudget(LargeItemsBudget);
        var root = systemDrive + "\\";
        AddFile(list, Path.Combine(root, "hiberfil.sys"), "hiberfil");
        AddFile(list, Path.Combine(root, "pagefile.sys"), "pagefile");

        // 候选目录：固定项 + AppData 一级子目录；体积并行计算（NVMe 上并行枚举明显更快）
        var candidates = new List<(string Path, string Category, long Threshold)>
        {
            (Path.GetTempPath().TrimEnd('\\'), "temp", 0),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "CrossDevice"), "phonelink", 0),
        };
        candidates.AddRange(AppDataCandidates().Select(p => (p, "appdata", AppDataThreshold)));

        var gate = new object();
        Parallel.ForEach(candidates, new ParallelOptions { MaxDegreeOfParallelism = 4 }, c =>
        {
            try
            {
                if (!Directory.Exists(c.Path)) return;
                var size = DirSize(new DirectoryInfo(c.Path), budget);
                if (size >= c.Threshold)
                    lock (gate) list.Add(new LargeItem(c.Path, size, c.Category));
            }
            catch { }
        });
        if (budget.Truncated) _log.Warn("大目录扫描超出时间预算，部分体积偏小或缺失");
        return list.OrderByDescending(i => i.SizeBytes).ToList();
    }

    /// <summary>AppData\Local、AppData\Roaming 的一级子目录，以及用户目录下的点目录。跳过 Temp/Microsoft/Packages 等系统目录与重解析点。</summary>
    private static List<string> AppDataCandidates()
    {
        var result = new List<string>();
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<(string Dir, bool DotOnly)>
        {
            (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), false),
            (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), false),
            (profile, true),
        };
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Temp", "Microsoft", "Packages", "CrossDevice" };
        foreach (var (root, dotOnly) in roots)
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<DirectoryInfo> subs;
            try { subs = new DirectoryInfo(root).EnumerateDirectories(); } catch { continue; }
            foreach (var d in subs)
            {
                try
                {
                    if (dotOnly && !d.Name.StartsWith(".", StringComparison.Ordinal)) continue;
                    if (!dotOnly && skip.Contains(d.Name)) continue;
                    if ((d.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    result.Add(d.FullName);
                }
                catch { }
            }
        }
        return result;
    }

    private static void AddFile(List<LargeItem> list, string path, string category)
    {
        try { if (File.Exists(path)) list.Add(new LargeItem(path, new FileInfo(path).Length, category)); } catch { }
    }

    private static long DirSize(DirectoryInfo dir, ScanBudget budget)
    {
        long total = 0;
        try
        {
            foreach (var f in dir.EnumerateFiles()) { try { total += f.Length; } catch { } }
            foreach (var d in dir.EnumerateDirectories())
            {
                if (budget.Expired) { budget.Truncated = true; break; }
                try { if ((d.Attributes & FileAttributes.ReparsePoint) == 0) total += DirSize(d, budget); } catch { }
            }
        }
        catch { }
        return total;
    }

    // ---- 注册表小工具 ----

    private string? Str(RegRoot root, string key, string name) { try { return _reg.GetValue(root, key, name).Value?.ToString(); } catch { return null; } }
    private long Int(RegRoot root, string key, string name)
    {
        try { var v = _reg.GetValue(root, key, name).Value; return v == null ? 0 : Convert.ToInt64(v); } catch { return 0; }
    }
}
