using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Xml.Linq;
using WinHomestead.Core.Abstractions;

namespace WinHomestead.Native;

/// <summary>电池容量，单位 mWh；读不到的项为 null。</summary>
public sealed record BatteryHealth(long? DesignCapacity, long? FullChargeCapacity, long? CycleCount)
{
    public bool HasAny => DesignCapacity != null || FullChargeCapacity != null;
}

/// <summary>
/// 先读 root\WMI 的电池类，够快且没有副作用。部分机型的固件不响应 BatteryStaticData（WMI 直接报"常规故障"），
/// 缺设计容量时回退到 powercfg /batteryreport /xml：它不需要管理员，数据与设备自己报告的一致，代价是多花一两秒并写一个临时文件。
/// </summary>
public static class BatteryProbe
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/battery/2012";

    public static BatteryHealth Read(ILogger log)
    {
        var design = Wmi(log, "SELECT DesignedCapacity FROM BatteryStaticData", "DesignedCapacity");
        var full = Wmi(log, "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", "FullChargedCapacity");
        var cycles = Wmi(log, "SELECT CycleCount FROM BatteryCycleCount", "CycleCount");
        if (design != null && full != null) return new BatteryHealth(design, full, cycles);

        var report = FromReport(log);
        return report == null
            ? new BatteryHealth(design, full, cycles)
            : new BatteryHealth(design ?? report.DesignCapacity, full ?? report.FullChargeCapacity, cycles ?? report.CycleCount);
    }

    private static long? Wmi(ILogger log, string query, string property)
    {
        try
        {
            using var s = new ManagementObjectSearcher(new ManagementScope(@"\\.\root\WMI"), new ObjectQuery(query));
            foreach (ManagementObject o in s.Get())
                using (o)
                {
                    var v = o[property];
                    if (v != null) return Convert.ToInt64(v, CultureInfo.InvariantCulture);
                }
        }
        catch (Exception ex) { log.Warn($"{property} 读取失败: " + ex.Message); }
        return null;
    }

    private static BatteryHealth? FromReport(ILogger log)
    {
        var path = Path.Combine(Path.GetTempPath(), "winhomestead-battery.xml");
        try
        {
            var psi = new ProcessStartInfo("powercfg", $"/batteryreport /xml /output \"{path}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var p = Process.Start(psi))
            {
                if (p == null) return null;
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                if (!p.WaitForExit(15000)) return null;
            }
            if (!File.Exists(path)) return null;

            // RuntimeEstimates 下也有同名的 DesignCapacity，必须落到 Batteries/Battery 里取
            var battery = XDocument.Load(path).Root?.Element(Ns + "Batteries")?.Element(Ns + "Battery");
            if (battery == null) return null;
            return new BatteryHealth(Num(battery, "DesignCapacity"), Num(battery, "FullChargeCapacity"), Num(battery, "CycleCount"));
        }
        catch (Exception ex)
        {
            log.Warn("powercfg 电池报告读取失败: " + ex.Message);
            return null;
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    private static long? Num(XElement battery, string name)
        => long.TryParse((string?)battery.Element(Ns + name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (long?)null;
}
