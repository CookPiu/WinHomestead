using System;
using System.Management;
using WinHomestead.Core.Abstractions;

namespace WinHomestead.Native;

public sealed class WmiSystemRestore : ISystemRestore
{
    private readonly ILogger _log;
    public WmiSystemRestore(ILogger log) { _log = log; }

    public bool CreateRestorePoint(string description)
    {
        using var cls = new ManagementClass(new ManagementScope(@"\\.\root\default"), new ManagementPath("SystemRestore"), null);
        using var inParams = cls.GetMethodParameters("CreateRestorePoint");
        inParams["Description"] = description;
        inParams["RestorePointType"] = 0;   // APPLICATION_INSTALL
        inParams["EventType"] = 100;         // BEGIN_SYSTEM_CHANGE
        using var outParams = cls.InvokeMethod("CreateRestorePoint", inParams, null);
        var rc = Convert.ToInt32(outParams["ReturnValue"]);
        if (rc == 0) return true;
        if (rc == 1440) { _log.Info("24 小时内已有还原点，系统跳过本次创建"); return true; }
        _log.Warn("CreateRestorePoint 返回 " + rc);
        return false;
    }
}
