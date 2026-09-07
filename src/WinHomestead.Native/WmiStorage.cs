using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using WinHomestead.Core.Abstractions;

namespace WinHomestead.Native;

/// <summary>
/// Storage Management API（root\Microsoft\Windows\Storage）的最小封装：
/// MSFT_Partition.GetSupportedSize / Resize、MSFT_Disk.CreatePartition、MSFT_Volume.Format。
/// 所有调用检查 ReturnValue 与 ExtendedStatus，非 0 抛 StorageException。
/// </summary>
public sealed class WmiStorage : IStorage
{
    private static readonly ManagementScope Scope = new(@"\\.\root\Microsoft\Windows\Storage");
    private readonly ILogger _log;

    public WmiStorage(ILogger log) { _log = log; }

    public PartitionInfo? GetPartition(string driveLetter)
    {
        var letter = driveLetter.TrimEnd(':', '\\').ToUpperInvariant();
        if (letter.Length != 1) return null;
        using var obj = FindPartitionByLetter(letter[0]);
        return obj == null ? null : Read(obj);
    }

    public ShrinkSupport GetSupportedSize(PartitionInfo partition)
    {
        using var obj = Find(partition);
        using var outp = obj.InvokeMethod("GetSupportedSize", null, null);
        Check(outp, "GetSupportedSize");
        return new ShrinkSupport(Convert.ToInt64(outp["SizeMin"]), Convert.ToInt64(outp["SizeMax"]));
    }

    public void Resize(PartitionInfo partition, long newSizeBytes)
    {
        using var obj = Find(partition);
        using var inp = obj.GetMethodParameters("Resize");
        inp["Size"] = (ulong)newSizeBytes;
        _log.Info($"Resize 分区 {partition.DiskNumber}/{partition.PartitionNumber} → {newSizeBytes >> 30} GB");
        using var outp = obj.InvokeMethod("Resize", inp, null);
        Check(outp, "Resize");
    }

    public PartitionInfo CreatePartitionUsingMaximumSize(int diskNumber, char driveLetter)
    {
        using var disk = FindDisk(diskNumber);
        using var inp = disk.GetMethodParameters("CreatePartition");
        inp["UseMaximumSize"] = true;
        inp["DriveLetter"] = char.ToUpperInvariant(driveLetter);
        _log.Info($"CreatePartition 磁盘 {diskNumber} 盘符 {driveLetter}");
        using var outp = disk.InvokeMethod("CreatePartition", inp, null);
        Check(outp, "CreatePartition");
        var created = outp["CreatedPartition"] as ManagementBaseObject ?? throw new StorageException("CreatePartition", 0, "未返回 CreatedPartition");
        return Read(created);
    }

    public void FormatNtfs(PartitionInfo partition, string label)
    {
        using var obj = Find(partition);
        ManagementObject? volume = null;
        try
        {
            foreach (ManagementObject v in obj.GetRelated("MSFT_Volume")) { volume = v; break; }
            if (volume == null) throw new StorageException("Format", 0, "新分区没有关联的卷");
            using var inp = volume.GetMethodParameters("Format");
            inp["FileSystem"] = "NTFS";
            inp["FileSystemLabel"] = label;
            inp["Full"] = false;
            _log.Info($"Format {partition.DriveLetter} NTFS 卷标 {label}");
            using var outp = volume.InvokeMethod("Format", inp, null);
            Check(outp, "Format");
        }
        finally { volume?.Dispose(); }
    }

    public IReadOnlyList<string> UsedDriveLetters()
        => DriveInfo.GetDrives().Select(d => d.Name.TrimEnd('\\')).ToList();

    // ---- 内部 ----

    private static PartitionInfo Read(ManagementBaseObject o)
    {
        var letter = o["DriveLetter"];
        var ch = letter == null ? '\0' : Convert.ToChar(letter);
        return new PartitionInfo(
            Convert.ToInt32(o["DiskNumber"]), Convert.ToInt32(o["PartitionNumber"]),
            ch == '\0' ? null : ch + ":", Convert.ToInt64(o["Size"]), Convert.ToInt64(o["Offset"]));
    }

    private static ManagementObject? FindPartitionByLetter(char letter)
    {
        using var s = new ManagementObjectSearcher(Scope, new ObjectQuery($"SELECT * FROM MSFT_Partition WHERE DriveLetter = '{letter}'"));
        foreach (ManagementObject o in s.Get()) return o;
        return null;
    }

    private static ManagementObject Find(PartitionInfo p)
    {
        using var s = new ManagementObjectSearcher(Scope, new ObjectQuery($"SELECT * FROM MSFT_Partition WHERE DiskNumber = {p.DiskNumber} AND PartitionNumber = {p.PartitionNumber}"));
        foreach (ManagementObject o in s.Get()) return o;
        throw new StorageException("FindPartition", 0, $"找不到分区 {p.DiskNumber}/{p.PartitionNumber}");
    }

    private static ManagementObject FindDisk(int number)
    {
        using var s = new ManagementObjectSearcher(Scope, new ObjectQuery($"SELECT * FROM MSFT_Disk WHERE Number = {number}"));
        foreach (ManagementObject o in s.Get()) return o;
        throw new StorageException("FindDisk", 0, $"找不到磁盘 {number}");
    }

    private static void Check(ManagementBaseObject outp, string operation)
    {
        var rv = Convert.ToUInt32(outp["ReturnValue"]);
        if (rv == 0) return;
        string? message = null;
        try
        {
            if (outp["ExtendedStatus"] is ManagementBaseObject ext)
                message = ext["Message"]?.ToString() ?? ext["MessageDescription"]?.ToString();
        }
        catch { }
        throw new StorageException(operation, rv, message);
    }
}
