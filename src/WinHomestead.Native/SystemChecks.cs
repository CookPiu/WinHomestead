using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using WinHomestead.Core.Abstractions;

namespace WinHomestead.Native;

/// <summary>
/// 《01》4.4 的"只提示不执行"检查项：一次只读采集，不改动任何设置。
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

    /// <summary>按展示顺序返回全部检查项。任何一项失败只影响它自己。</summary>
    public IReadOnlyList<SystemCheck> Run(string manufacturer)
        => new[] { BitLocker(), AntiVirus(), WindowsUpdate(), OemSoftware(manufacturer), DefaultApps(), RegionAndTimeZone() };

    // ---- BitLocker ----

    private SystemCheck BitLocker()
    {
        const string title = "BitLocker 恢复密钥";
        const string uri = "ms-settings:deviceencryption";
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
                        return new SystemCheck(title, "已加密", Severity.Warning,
                            $"{drive} 已开启 BitLocker 保护。",
                            "确认恢复密钥已经备份：没有密钥时，主板更换、固件更新或忘记 PIN 都会导致数据无法找回。可备份到微软账户、打印或存到另一台设备上的文件，不要只存在本机。",
                            uri, "打开设备加密设置");
                    return new SystemCheck(title, "未加密", Severity.Ok,
                        $"{drive} 未开启 BitLocker。", "无需备份恢复密钥。若之后开启加密，记得先把恢复密钥存到本机以外的地方。", uri, "打开设备加密设置");
                }
            return new SystemCheck(title, "不适用", Severity.Info, "系统未提供 BitLocker 接口（多见于未启用设备加密的家庭版）。",
                "如果之后开启了设备加密，请到设置里备份恢复密钥。", uri, "打开设备加密设置");
        }
        catch (Exception ex)
        {
            _log.Warn("BitLocker 检查失败: " + ex.Message);
            return new SystemCheck(title, "未知", Severity.Info, "读取加密状态失败：" + ex.Message.Trim(),
                "可在设置中自行确认设备加密状态与恢复密钥备份情况。", uri, "打开设备加密设置");
        }
    }

    // ---- 杀毒软件 ----

    private SystemCheck AntiVirus()
    {
        const string title = "杀毒软件";
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
            var detail = names.Count == 0 ? "安全中心里没有注册任何杀毒软件。" : "已注册：" + string.Join("、", names);
            if (thirdParty.Count > 1)
                return new SystemCheck(title, $"{thirdParty.Count} 套并存", Severity.Warning, detail,
                    "多套实时防护同时驻留会互相扫描对方的文件读写，拖慢开机与编译，还可能互相误杀。建议只保留一套，其余在应用列表里卸载。",
                    "ms-settings:appsfeatures", "打开应用列表");
            if (thirdParty.Count == 1)
                return new SystemCheck(title, "第三方一套", Severity.Ok, detail,
                    "只有一套第三方杀软，Defender 会自动退到被动模式，无需处理。", null, null);
            return new SystemCheck(title, "仅 Defender", Severity.Ok, detail, "系统自带防护即可，不必额外安装。", null, null);
        }
        catch (Exception ex)
        {
            _log.Warn("杀毒软件检查失败: " + ex.Message);
            return new SystemCheck(title, "未知", Severity.Info, "读取安全中心失败：" + ex.Message,
                "可在 Windows 安全中心里自行确认有几套实时防护。", null, null);
        }
    }

    // ---- Windows Update ----

    /// <summary>只读本机已装补丁的时间，不联网检查更新（《03》9：工具全程不联网）。</summary>
    private SystemCheck WindowsUpdate()
    {
        const string title = "Windows 更新";
        const string uri = "ms-settings:windowsupdate";
        var last = LastHotfix() ?? LegacyLastSuccess();
        if (last == null)
            return new SystemCheck(title, "未知", Severity.Info, "读不到本机已装补丁的时间。",
                "本工具不联网，不会替你检查更新。请自行到设置里点一次“检查更新”。", uri, "打开 Windows 更新");

        var days = (int)(DateTime.Now - last.Value).TotalDays;
        var detail = $"最近安装的补丁：{last.Value:yyyy-MM-dd}（{days} 天前）。仅统计累积更新与安全补丁，不含驱动和功能更新。";
        return days >= 30
            ? new SystemCheck(title, $"{days} 天未打补丁", Severity.Warning, detail,
                "建议先把累积更新装完再装软件，免得装完驱动又被更新覆盖。本工具不联网，请自行到设置里检查更新。", uri, "打开 Windows 更新")
            : new SystemCheck(title, "较新", Severity.Ok, detail, "近期装过补丁，无需特别处理。", uri, "打开 Windows 更新");
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
        const string title = "OEM 预装软件";
        const string uri = "ms-settings:appsfeatures";
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
                return new SystemCheck(title, "未发现", Severity.Ok, "没有匹配到整机厂商的预装软件。", "无需处理。", uri, "打开应用列表");

            return new SystemCheck(title, $"{hits.Count} 个", Severity.Info,
                "匹配到：" + string.Join("、", hits.Take(12)) + (hits.Count > 12 ? " 等" : string.Empty),
                "这些是整机厂商预装的管家、驱动助手一类程序。驱动更新工具可以留着，弹广告和加速器一类的用不上就卸载。本工具不代为卸载，请自行在应用列表里处理。",
                uri, "打开应用列表");
        }
        catch (Exception ex)
        {
            _log.Warn("OEM 预装软件检查失败: " + ex.Message);
            return new SystemCheck(title, "未知", Severity.Info, "读取已安装程序失败：" + ex.Message, "可自行在应用列表里查看。", uri, "打开应用列表");
        }
    }

    private static bool Contains(string? text, string keyword)
        => text != null && text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;

    // ---- 默认应用 ----

    private SystemCheck DefaultApps()
    {
        var browser = Str(RegRoot.CurrentUser, @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice", "ProgId");
        var detail = browser == null ? "未读到默认浏览器设置。" : "默认浏览器的关联标识：" + browser;
        return new SystemCheck("默认应用", "需自行确认", Severity.Info, detail,
            "Windows 11 的默认应用按文件类型逐项设置，没有公开接口可以整体替换，本工具不代改。装完浏览器、看图和播放器之后，到设置里把常用类型一次设完。",
            "ms-settings:defaultapps", "打开默认应用");
    }

    // ---- 区域与时区 ----

    private static SystemCheck RegionAndTimeZone()
    {
        string region;
        try { region = RegionInfo.CurrentRegion.DisplayName; } catch { region = "未知"; }
        var tz = TimeZoneInfo.Local;
        return new SystemCheck("区域与时区", "仅展示", Severity.Info,
            $"区域：{region}；区域格式：{CultureInfo.CurrentCulture.Name}；时区：{tz.DisplayName}；当前时间：{DateTime.Now:yyyy-MM-dd HH:mm}",
            "区域影响商店内容与部分应用的默认语言，时区影响日志与文件时间。工具不做任何假设，只把当前值列出来，与实际不符时自行到设置里改。",
            "ms-settings:dateandtime", "打开日期和时间");
    }

    private string? Str(RegRoot root, string key, string name)
    {
        try { return _reg.GetValue(root, key, name).Value?.ToString(); } catch { return null; }
    }
}
