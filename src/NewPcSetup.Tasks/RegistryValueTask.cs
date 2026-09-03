using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Engine;
using NewPcSetup.Core.Models;

namespace NewPcSetup.Tasks;

public sealed record RegistryEntry(RegRoot Root, string Key, string Name, RegKind Kind, object Target)
{
    public static RegistryEntry Dword(string key, string name, int target) => new(RegRoot.CurrentUser, key, name, RegKind.DWord, target);
    public static RegistryEntry Str(string key, string name, string target) => new(RegRoot.CurrentUser, key, name, RegKind.String, target);
}

/// <summary>把一组注册表值设为目标值的通用任务。绝大多数界面/推送类设置都是它的实例。</summary>
public sealed class RegistryValueTask : TaskBase
{
    private readonly IReadOnlyList<RegistryEntry> _entries;
    private readonly Func<EnvironmentSnapshot, Answers, bool>? _applicable;
    private readonly Func<EnvironmentSnapshot, Answers, bool>? _defaultChecked;

    public RegistryValueTask(TaskMetadata metadata, IReadOnlyList<RegistryEntry> entries,
        Func<EnvironmentSnapshot, Answers, bool>? applicable = null,
        Func<EnvironmentSnapshot, Answers, bool>? defaultChecked = null)
    {
        Metadata = metadata; _entries = entries; _applicable = applicable; _defaultChecked = defaultChecked;
    }

    public override TaskMetadata Metadata { get; }
    public IReadOnlyList<RegistryEntry> Entries => _entries;

    public override bool IsApplicable(EnvironmentSnapshot s, Answers a) => _applicable?.Invoke(s, a) ?? true;
    public override bool DefaultChecked(EnvironmentSnapshot s, Answers a) => _defaultChecked?.Invoke(s, a) ?? true;

    public override DetectResult Detect(TaskContext ctx)
    {
        var current = new List<string>();
        var target = new List<string>();
        var satisfied = true;
        foreach (var e in _entries)
        {
            var (v, _) = ctx.Registry.GetValue(e.Root, e.Key, e.Name);
            var label = Label(e);
            current.Add($"{label}={(v == null ? "未设置" : v.ToString())}");
            target.Add($"{label}={e.Target}");
            if (!Matches(v, e)) satisfied = false;
        }
        return new DetectResult(satisfied, string.Join("; ", current), string.Join("; ", target));
    }

    public override void Apply(TaskContext ctx)
    {
        foreach (var e in _entries)
        {
            var (v, _) = ctx.Registry.GetValue(e.Root, e.Key, e.Name);
            if (Matches(v, e)) continue;
            ctx.Registry.SetValue(e.Root, e.Key, e.Name, e.Target, e.Kind);
        }
    }

    /// <summary>多个条目同名时（如 StickyKeys 与 ToggleKeys 的 Flags）带上键的最后一段以示区分。</summary>
    private string Label(RegistryEntry e)
    {
        var name = e.Name.Length == 0 ? "(默认)" : e.Name;
        var dup = false;
        foreach (var o in _entries) if (!ReferenceEquals(o, e) && string.Equals(o.Name, e.Name, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
        if (!dup) return name;
        var i = e.Key.LastIndexOf('\\');
        return (i >= 0 ? e.Key.Substring(i + 1) : e.Key) + "." + name;
    }

    private static bool Matches(object? current, RegistryEntry e)
    {
        if (current == null) return false;
        switch (e.Kind)
        {
            case RegKind.DWord:
            case RegKind.QWord:
                try { return Convert.ToInt64(current, CultureInfo.InvariantCulture) == Convert.ToInt64(e.Target, CultureInfo.InvariantCulture); }
                catch { return false; }
            default:
                return string.Equals(current.ToString(), e.Target.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
