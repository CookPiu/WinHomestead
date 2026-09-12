using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using WinHomestead.Core.Abstractions;
using WinHomestead.Core.Infrastructure;
using WinHomestead.Core.Models;

namespace WinHomestead.Native;

/// <summary>
/// 《01》4.5 的"只提示不执行"检查项：一次只读采集，不改动任何设置。
/// 不进启动探测（WMI 查询较慢且这些项与主列表无关），由检查页按需调用。
/// </summary>
public sealed record SystemCheck(string Title, string Status, Severity Severity, string Detail, string Advice, string? SettingsUri, string? SettingsLabel);

public enum Severity { Ok, Info, Warning }

public sealed class SystemChecks
{
    /// <summary>
    /// 已知 OEM 的发行商名。只拿它匹配 Uninstall 键的 Publisher，不匹配 DisplayName——
    /// 用名字匹配会把 MSI Afterburner、vs_*msi 这类无关条目一起圈进来。
    /// </summary>
    private static readonly string[] OemPublishers =
    {
        "Lenovo", "Hewlett-Packard", "HP Inc", "Dell", "ASUSTeK", "Acer", "Huawei", "Micro-Star",
        "Samsung", "Toshiba", "Alienware", "Gigabyte", "联想", "华为",
    };

    private readonly IRegistry _reg;
    private readonly InstalledPrograms _installed;
    private readonly ILogger _log;

    public SystemChecks(IRegistry reg, InstalledPrograms installed, ILogger log)
    {
        _reg = reg; _installed = installed; _log = log;
    }

    /// <summary>按展示顺序返回全部检查项。任何一项失败只影响它自己。snapshot 为 null 时与机器相关的几项降级为“未知”。</summary>
    public IReadOnlyList<SystemCheck> Run(EnvironmentSnapshot? snapshot)
        => new[]
        {
            BitLocker(), AntiVirus(), WindowsUpdate(), PointInTimeRestore(snapshot),
            OemSoftware(snapshot?.Manufacturer ?? string.Empty), Battery(snapshot),
            DefaultApps(), RegionAndTimeZone(),
        };

    // ---- BitLocker ----

    private SystemCheck BitLocker()
    {
        var title = L.S("BitLocker 恢复密钥", "BitLocker recovery key");
        const string uri = "ms-settings:deviceencryption";
        var openEncryption = L.S("打开设备加密设置", "Open device encryption");
        try
        {
            var scope = new ManagementScope(@"\\.\root\CIMV2\Security\MicrosoftVolumeEncryption");
            var drive = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd('\\');
            using var s = new ManagementObjectSearcher(scope,
                new ObjectQuery($"SELECT ProtectionStatus, ConversionStatus FROM Win32_EncryptableVolume WHERE DriveLetter = '{drive}'"));
            foreach (ManagementObject o in s.Get())
                using (o)
                {
                    var protection = Convert.ToInt32(o["ProtectionStatus"], CultureInfo.InvariantCulture);
                    if (protection == 1)
                        return new SystemCheck(title, L.S("已加密", "encrypted"), Severity.Warning,
                            L.S($"{drive} 已开启 BitLocker 保护。", $"{drive} is protected by BitLocker."),
                            L.S("确认恢复密钥已经备份：没有密钥时，主板更换、固件更新或忘记 PIN 都会导致数据无法找回。可备份到微软账户、打印或存到另一台设备上的文件，不要只存在本机。",
                                "Make sure the recovery key is backed up somewhere else. Without it, a board swap, a firmware update or a forgotten PIN all mean the data is gone. Save it to your Microsoft account, print it, or put it in a file on another device — never only on this machine."),
                            uri, openEncryption);
                    return new SystemCheck(title, L.S("未加密", "not encrypted"), Severity.Ok,
                        L.S($"{drive} 未开启 BitLocker。", $"BitLocker is off for {drive}."),
                        L.S("无需备份恢复密钥。若之后开启加密，记得先把恢复密钥存到本机以外的地方。",
                            "Nothing to back up. If you turn encryption on later, put the recovery key somewhere off this machine first."), uri, openEncryption);
                }
            return new SystemCheck(title, L.S("不适用", "n/a"), Severity.Info,
                L.S("系统未提供 BitLocker 接口（多见于未启用设备加密的家庭版）。",
                    "Windows isn't exposing the BitLocker interface here — common on Home editions without device encryption."),
                L.S("如果之后开启了设备加密，请到设置里备份恢复密钥。",
                    "If you turn on device encryption later, back up the recovery key from Settings."), uri, openEncryption);
        }
        catch (Exception ex)
        {
            _log.Warn("BitLocker 检查失败: " + ex.Message);
            return new SystemCheck(title, L.S("未知", "unknown"), Severity.Info,
                L.S("读取加密状态失败：", "Couldn't read the encryption state: ") + ex.Message.Trim(),
                L.S("可在设置中自行确认设备加密状态与恢复密钥备份情况。",
                    "Check device encryption and the recovery key backup yourself in Settings."), uri, openEncryption);
        }
    }

    // ---- 杀毒软件 ----

    private SystemCheck AntiVirus()
    {
        var title = L.S("杀毒软件", "Antivirus");
        var openApps = L.S("打开应用列表", "Open installed apps");
        try
        {
            var names = new List<string>();
            using var s = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\SecurityCenter2"),
                new ObjectQuery("SELECT displayName FROM AntiVirusProduct"));
            foreach (ManagementObject o in s.Get())
                using (o)
                {
                    var n = o["displayName"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(n)) names.Add(n!);
                }

            var thirdParty = names.Where(n => n.IndexOf("Windows Defender", StringComparison.OrdinalIgnoreCase) < 0
                                              && n.IndexOf("Microsoft Defender", StringComparison.OrdinalIgnoreCase) < 0).ToList();
            var detail = names.Count == 0
                ? L.S("安全中心里没有注册任何杀毒软件。", "No antivirus product is registered with the Security Center.")
                : L.S("已注册：", "Registered: ") + L.Join(names.ToArray());
            if (thirdParty.Count > 1)
                return new SystemCheck(title, L.S($"{thirdParty.Count} 套并存", $"{thirdParty.Count} installed"), Severity.Warning, detail,
                    L.S("多套实时防护同时驻留会互相扫描对方的文件读写，拖慢开机与编译，还可能互相误杀。建议只保留一套，其余在应用列表里卸载。",
                        "Several real-time scanners resident at once end up scanning each other's file access, which slows down boot and builds and can lead to mutual false positives. Keep one and uninstall the rest from the app list."),
                    "ms-settings:appsfeatures", openApps);
            if (thirdParty.Count == 1)
                return new SystemCheck(title, L.S("第三方一套", "one third-party"), Severity.Ok, detail,
                    L.S("只有一套第三方杀软，Defender 会自动退到被动模式，无需处理。",
                        "With a single third-party scanner, Defender steps back into passive mode on its own. Nothing to do."), null, null);
            return new SystemCheck(title, L.S("仅 Defender", "Defender only"), Severity.Ok, detail,
                L.S("系统自带防护即可，不必额外安装。", "The built-in protection is enough; no need to add another."), null, null);
        }
        catch (Exception ex)
        {
            _log.Warn("杀毒软件检查失败: " + ex.Message);
            return new SystemCheck(title, L.S("未知", "unknown"), Severity.Info,
                L.S("读取安全中心失败：", "Couldn't read the Security Center: ") + ex.Message,
                L.S("可在 Windows 安全中心里自行确认有几套实时防护。",
                    "Check how many real-time scanners are active yourself in Windows Security."), null, null);
        }
    }

    // ---- Windows Update ----

    /// <summary>只读本机已装补丁的时间，不联网检查更新（《03》9：工具全程不联网）。</summary>
    private SystemCheck WindowsUpdate()
    {
        var title = L.S("Windows 更新", "Windows Update");
        const string uri = "ms-settings:windowsupdate";
        var openUpdate = L.S("打开 Windows 更新", "Open Windows Update");
        var last = LastHotfix() ?? LegacyLastSuccess();
        if (last == null)
            return new SystemCheck(title, L.S("未知", "unknown"), Severity.Info,
                L.S("读不到本机已装补丁的时间。", "Can't read when updates were last installed."),
                L.S("本工具不联网，不会替你检查更新。请自行到设置里点一次“检查更新”。",
                    "This tool never goes online and won't check for updates for you. Hit Check for updates in Settings yourself."), uri, openUpdate);

        var days = (int)(DateTime.Now - last.Value).TotalDays;
        var detail = L.S($"最近安装的补丁：{last.Value:yyyy-MM-dd}（{days} 天前）。仅统计累积更新与安全补丁，不含驱动和功能更新。",
            $"Most recent patch: {last.Value:yyyy-MM-dd} ({days} days ago). Counts cumulative and security updates only — not drivers or feature updates.");
        return days >= 30
            ? new SystemCheck(title, L.S($"{days} 天未打补丁", $"{days} days behind"), Severity.Warning, detail,
                L.S("建议先把累积更新装完再装软件，免得装完驱动又被更新覆盖。本工具不联网，请自行到设置里检查更新。",
                    "Install the pending cumulative updates before you start installing software, so a fresh driver doesn't get overwritten right after. This tool never goes online — check for updates in Settings yourself."), uri, openUpdate)
            : new SystemCheck(title, L.S("较新", "recent"), Severity.Ok, detail,
                L.S("近期装过补丁，无需特别处理。", "Patched recently; nothing to do."), uri, openUpdate);
    }

    private DateTime? LastHotfix()
    {
        try
        {
            DateTime? max = null;
            using var s = new ManagementObjectSearcher("SELECT InstalledOn FROM Win32_QuickFixEngineering");
            foreach (ManagementObject o in s.Get())
                using (o)
                {
                    var raw = o["InstalledOn"]?.ToString();
                    // WMI 这里给的是字符串，格式随区域变化，两种解析都试一次
                    if (string.IsNullOrEmpty(raw)) continue;
                    if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                        && !DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out d)) continue;
                    if (max == null || d > max) max = d;
                }
            return max;
        }
        catch (Exception ex) { _log.Warn("Win32_QuickFixEngineering 失败: " + ex.Message); return null; }
    }

    /// <summary>Win10 时代的记录位置，Win11 上多半不存在，仅作兜底。</summary>
    private DateTime? LegacyLastSuccess()
    {
        var raw = Str(RegRoot.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\Results\Install", "LastSuccessTime");
        return raw != null && DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc) ? utc.ToLocalTime() : (DateTime?)null;
    }

    // ---- OEM 预装软件 ----

    private SystemCheck OemSoftware(string manufacturer)
    {
        var title = L.S("OEM 预装软件", "OEM preinstalled software");
        const string uri = "ms-settings:appsfeatures";
        var openApps = L.S("打开应用列表", "Open installed apps");
        try
        {
            var keywords = OemPublishers.ToList();
            var brand = (manufacturer ?? string.Empty).Split(' ')[0].Trim();
            if (brand.Length >= 2) keywords.Add(brand);

            var hits = _installed.Entries()
                .Where(e => keywords.Any(k => Contains(e.Publisher, k)))
                .Select(e => e.DisplayName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (hits.Count == 0)
                return new SystemCheck(title, L.S("未发现", "none found"), Severity.Ok,
                    L.S("没有匹配到整机厂商的预装软件。", "Nothing published by the PC maker turned up."),
                    L.S("无需处理。", "Nothing to do."), uri, openApps);

            return new SystemCheck(title, L.S($"{hits.Count} 个", $"{hits.Count} found"), Severity.Info,
                L.S("匹配到：", "Matched: ") + L.Join(hits.Take(12).ToArray()) + (hits.Count > 12 ? L.S(" 等", " and more") : string.Empty),
                L.S("这些是整机厂商预装的管家、驱动助手一类程序。驱动更新工具可以留着，弹广告和加速器一类的用不上就卸载。本工具不代为卸载，请自行在应用列表里处理；只是不想让它们开机自启的话，去\"启动项\"页逐项关掉即可。",
                    "These are the vendor's own utilities and driver helpers. Keep the driver updater if you want it; uninstall the ad-serving and \"booster\" ones you won't use. This tool never uninstalls anything — do it from the app list. If you only want to stop them starting with Windows, switch them off on the Startup items page."),
                uri, openApps);
        }
        catch (Exception ex)
        {
            _log.Warn("OEM 预装软件检查失败: " + ex.Message);
            return new SystemCheck(title, L.S("未知", "unknown"), Severity.Info,
                L.S("读取已安装程序失败：", "Couldn't read installed programs: ") + ex.Message,
                L.S("可自行在应用列表里查看。", "Have a look in the app list yourself."), uri, openApps);
        }
    }

    private static bool Contains(string? text, string keyword)
        => text != null && text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;

    // ---- 还原点占用 ----

    /// <summary>
    /// 26H2（Build 26300）起默认开启的 Point-in-time restore：还原点存在本机，最多占 50 GB，
    /// 每 24 小时建一个、保留 72 小时。没有公开接口能读它的开关和实际占用，所以这里只按版本与
    /// 系统卷容量说明它会不会被自动打开，具体状态由用户到设置里确认。
    /// </summary>
    private static SystemCheck PointInTimeRestore(EnvironmentSnapshot? s)
    {
        var title = L.S("还原点占用", "Restore point footprint");
        const string uri = "ms-settings:recovery";
        var openRecovery = L.S("打开恢复设置", "Open recovery settings");
        var advice = L.S(
            "它最多会在系统盘上占掉 50 GB，设置页里的滑块可以调小上限。空间紧张就调低或关掉，想留一手回滚就保持开启。" +
            "本工具不代改，也不会去删它建的还原点——工具收尾时自己创建的那个还原点是另一套机制（系统保护），两者互不影响。",
            "It can take up to 50 GB on the system drive; the slider on that settings page lowers the cap. Turn it down or off when space is tight, " +
            "or leave it on if you want the rollback option. This tool changes nothing here and never deletes its restore points \u2014 the one this tool " +
            "creates before running comes from a different mechanism (System Protection), and the two don't interfere.");
        if (s == null)
            return new SystemCheck(title, L.S("未知", "unknown"), Severity.Info,
                L.S("还没拿到系统信息。", "System information isn't available yet."), advice, uri, openRecovery);

        var sys = s.Volumes.FirstOrDefault(v => v.IsSystem);
        var detail = L.S($"系统版本 {s.OsCaption.Trim()}（Build {s.Build}）", $"Windows {s.OsCaption.Trim()} (build {s.Build})")
                     + (sys == null ? L.S("。", ".")
                         : L.S($"；{sys.DriveLetter} 容量 {sys.SizeGb:F2} GB，剩余 {sys.FreeGb:F2} GB。",
                               $"; {sys.DriveLetter} is {sys.SizeGb:F2} GB with {sys.FreeGb:F2} GB free."));

        if (s.Build < 26300)
            return new SystemCheck(title, L.S("本版本未默认开启", "not on by default here"), Severity.Ok,
                detail + L.S("Point-in-time restore 从 26H2（Build 26300）起才默认开启，当前版本上要自己到设置里打开。",
                    " Point-in-time restore only becomes a default in 26H2 (build 26300); on this build you would have to turn it on yourself."),
                L.S("升级到 26H2 之后它会自动开启并开始占用系统盘空间，到时候可以在同一个页面调上限。",
                    "After upgrading to 26H2 it switches itself on and starts using system drive space; the cap is adjustable on the same page."),
                uri, openRecovery);

        var bigEnough = sys != null && sys.SizeBytes >= 200L * (1L << 30);
        return new SystemCheck(title,
            bigEnough ? L.S("多半已默认开启", "probably on by default") : L.S("需自行确认", "check it yourself"), Severity.Info,
            detail + L.S("26H2 起 Point-in-time restore 默认开启，系统卷 200 GB 以上会自动启用。",
                " From 26H2 on, point-in-time restore defaults to on and enables itself automatically when the system volume is 200 GB or larger."),
            advice, uri, openRecovery);
    }

    // ---- 电池健康 ----

    private SystemCheck Battery(EnvironmentSnapshot? s)
    {
        var title = L.S("电池健康", "Battery health");
        var fresh = L.S("新机刚开箱或刚重装时，电量统计还没有足够样本，满充容量会偏离真实值，正常使用几天之后再看更准。",
            "On a machine that was just unboxed or reinstalled the battery statistics have too few samples, so the full-charge figure drifts from reality. Give it a few days of normal use before reading anything into it.");
        if (s != null && !s.IsLaptop)
            return new SystemCheck(title, L.S("不适用", "n/a"), Severity.Info,
                L.S("这是台式机，没有内置电池。", "This is a desktop; there is no built-in battery."),
                L.S("无需处理。", "Nothing to do."), null, null);

        var b = BatteryProbe.Read(_log);
        var cycles = b.CycleCount is > 0
            ? L.S($"，循环次数 {b.CycleCount}。", $", {b.CycleCount} cycles.")
            : L.S("；未报告循环次数。", "; cycle count not reported.");
        if (!b.HasAny)
            return new SystemCheck(title, L.S("未知", "unknown"), Severity.Info,
                L.S("读不到电池容量数据。", "No battery capacity data available."),
                L.S("可以自己跑一次 powercfg /batteryreport 看详细报告。",
                    "Run powercfg /batteryreport yourself for the full report. ") + fresh, null, null);

        if (b.DesignCapacity == null || b.DesignCapacity.Value <= 0 || b.FullChargeCapacity == null)
            return new SystemCheck(title, L.S("部分数据", "partial data"), Severity.Info,
                (b.FullChargeCapacity != null
                    ? L.S($"当前满充 {b.FullChargeCapacity} mWh", $"full charge {b.FullChargeCapacity} mWh")
                    : L.S($"设计容量 {b.DesignCapacity} mWh", $"design capacity {b.DesignCapacity} mWh")) + cycles,
                L.S("缺另一半容量数据，算不出衰减比例。需要完整数据可以自己跑一次 powercfg /batteryreport。",
                    "The other half of the capacity data is missing, so wear can't be calculated. Run powercfg /batteryreport for the complete picture. ") + fresh, null, null);

        var health = b.FullChargeCapacity.Value * 100.0 / b.DesignCapacity.Value;
        var detail = L.S($"设计容量 {b.DesignCapacity} mWh，当前满充 {b.FullChargeCapacity} mWh，健康度 {health:F1}%",
            $"design capacity {b.DesignCapacity} mWh, full charge {b.FullChargeCapacity} mWh, health {health:F1}%") + cycles;
        return health < 80
            ? new SystemCheck(title, $"{health:F0}%", Severity.Warning, detail,
                L.S("满充容量明显低于设计容量。刚买的新机先排除电量统计不准（正常用几天再看），仍然偏低就联系售后。",
                    "Full-charge capacity is noticeably below design. On a new machine rule out unsettled statistics first (use it normally for a few days); if it stays low, talk to support."), null, null)
            : new SystemCheck(title, $"{health:F0}%", Severity.Ok, detail, L.S("容量正常。", "Capacity looks fine. ") + fresh, null, null);
    }

    // ---- 默认应用 ----

    private SystemCheck DefaultApps()
    {
        var browser = Str(RegRoot.CurrentUser, @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice", "ProgId");
        var detail = browser == null
            ? L.S("未读到默认浏览器设置。", "Couldn't read the default browser setting.")
            : L.S("默认浏览器的关联标识：", "Default browser association: ") + browser;
        return new SystemCheck(L.S("默认应用", "Default apps"), L.S("需自行确认", "check it yourself"), Severity.Info, detail,
            L.S("Windows 11 的默认应用按文件类型逐项设置，没有公开接口可以整体替换，本工具不代改。装完浏览器、看图和播放器之后，到设置里把常用类型一次设完。",
                "Windows 11 assigns default apps per file type and offers no public API to set them in bulk, so this tool leaves them alone. Once your browser, image viewer and media player are installed, set the common types in one pass from Settings."),
            "ms-settings:defaultapps", L.S("打开默认应用", "Open default apps"));
    }

    // ---- 区域与时区 ----

    private static SystemCheck RegionAndTimeZone()
    {
        string region;
        try { region = RegionInfo.CurrentRegion.DisplayName; } catch { region = L.S("未知", "unknown"); }
        var tz = TimeZoneInfo.Local;
        return new SystemCheck(L.S("区域与时区", "Region and time zone"), L.S("仅展示", "informational"), Severity.Info,
            L.S($"区域：{region}；区域格式：{CultureInfo.CurrentCulture.Name}；时区：{tz.DisplayName}；当前时间：{DateTime.Now:yyyy-MM-dd HH:mm}",
                $"Region: {region}; format: {CultureInfo.CurrentCulture.Name}; time zone: {tz.DisplayName}; local time {DateTime.Now:yyyy-MM-dd HH:mm}"),
            L.S("区域影响商店内容与部分应用的默认语言，时区影响日志与文件时间。工具不做任何假设，只把当前值列出来，与实际不符时自行到设置里改。",
                "Region drives Store content and the default language of some apps; the time zone drives log and file timestamps. This tool assumes nothing and only shows the current values — fix them in Settings if they don't match where you are."),
            "ms-settings:dateandtime", L.S("打开日期和时间", "Open date and time"));
    }

    private string? Str(RegRoot root, string key, string name)
    {
        try { return _reg.GetValue(root, key, name).Value?.ToString(); } catch { return null; }
    }
}
