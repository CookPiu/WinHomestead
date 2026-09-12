using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;
using WinHomestead.Core.Abstractions;

namespace WinHomestead.Native;

public sealed class WindowsRegistry : IRegistry
{
    private static RegistryKey Base(RegRoot root)
        => RegistryKey.OpenBaseKey(root == RegRoot.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64);

    public bool KeyExists(RegRoot root, string key)
    {
        using var b = Base(root);
        using var k = b.OpenSubKey(key, false);
        return k != null;
    }

    public (object? Value, RegKind? Kind) GetValue(RegRoot root, string key, string name)
    {
        using var b = Base(root);
        using var k = b.OpenSubKey(key, false);
        if (k == null) return (null, null);
        var exists = false;
        foreach (var n in k.GetValueNames())
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
        if (!exists) return (null, null);
        var v = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return (v, Map(k.GetValueKind(name)));
    }

    public void SetValue(RegRoot root, string key, string name, object value, RegKind kind)
    {
        using var b = Base(root);
        using var k = b.CreateSubKey(key, true) ?? throw new InvalidOperationException("无法创建注册表键: " + key);
        k.SetValue(name, Convert(value, kind), MapBack(kind));
    }

    public void DeleteValue(RegRoot root, string key, string name)
    {
        using var b = Base(root);
        using var k = b.OpenSubKey(key, true);
        k?.DeleteValue(name, false);
    }

    public IReadOnlyList<string> GetSubKeyNames(RegRoot root, string key)
    {
        using var b = Base(root);
        using var k = b.OpenSubKey(key, false);
        return k?.GetSubKeyNames() ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> GetValueNames(RegRoot root, string key)
    {
        using var b = Base(root);
        using var k = b.OpenSubKey(key, false);
        return k?.GetValueNames() ?? Array.Empty<string>();
    }

    private static object Convert(object value, RegKind kind) => kind switch
    {
        RegKind.DWord => unchecked((int)System.Convert.ToInt64(value, CultureInfo.InvariantCulture)),
        RegKind.QWord => System.Convert.ToInt64(value, CultureInfo.InvariantCulture),
        RegKind.Binary => value as byte[] ?? Array.Empty<byte>(),
        _ => value.ToString() ?? string.Empty,
    };

    private static RegKind? Map(RegistryValueKind k) => k switch
    {
        RegistryValueKind.DWord => RegKind.DWord,
        RegistryValueKind.QWord => RegKind.QWord,
        RegistryValueKind.String => RegKind.String,
        RegistryValueKind.ExpandString => RegKind.ExpandString,
        RegistryValueKind.Binary => RegKind.Binary,
        _ => null,
    };

    private static RegistryValueKind MapBack(RegKind k) => k switch
    {
        RegKind.DWord => RegistryValueKind.DWord,
        RegKind.QWord => RegistryValueKind.QWord,
        RegKind.ExpandString => RegistryValueKind.ExpandString,
        RegKind.Binary => RegistryValueKind.Binary,
        _ => RegistryValueKind.String,
    };
}
