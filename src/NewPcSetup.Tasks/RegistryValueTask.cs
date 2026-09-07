using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Engine;
using NewPcSetup.Core.Models;

namespace NewPcSetup.Tasks;

/// <summary>
/// 一个注册表值及其目标。Label / ValueNames / UnsetName 是给人看的说明：
/// 带了 Label 的条目在列表里显示为“搜索框样式：图标+标签”，而不是 "SearchboxTaskbarMode=3"。
/// </summary>
public sealed record RegistryEntry(RegRoot Root, string Key, string Name, RegKind Kind, object Target)
{
    public static RegistryEntry Dword(string key, string name, int target) => new(RegRoot.CurrentUser, key, name, RegKind.DWord, target);
    public static RegistryEntry Str(string key, string name, string target) => new(RegRoot.CurrentUser, key, name, RegKind.String, target);

    /// <summary>这一项设置的名字，如“文件扩展名”。为空则界面只显示原始键值。</summary>
    public string? Label { get; init; }
    /// <summary>值 → 含义，键为值的字符串形式（数值按十进制）。</summary>
    public IReadOnlyDictionary<string, string>? ValueNames { get; init; }
    /// <summary>值不存在时的含义，如“未设置（默认居中）”。为空时显示“未设置”。</summary>
    public string? UnsetName { get; init; }

    /// <summary>给条目加上人话说明。names 为 (值, 含义) 对。</summary>
    public RegistryEntry As(string label, string? unset, params (object Value, string Name)[] names)
        => this with
        {
            Label = label,
            UnsetName = unset,
            ValueNames = names.ToDictionary(n => ValueKey(n.Value), n => n.Name, StringComparer.OrdinalIgnoreCase),
        };

    public string NameOf(object? value)
    {
        if (value == null) return UnsetName ?? "未设置";
        if (ValueNames != null && ValueNames.TryGetValue(ValueKey(value), out var n)) return n;
        return value.ToString() ?? string.Empty;
    }

    private static string ValueKey(object v) => Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty;
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

    /// <summary>
    /// 有 Label 的条目输出人话（当前：图标+标签 → 目标：仅图标），原始键值放进 Detail；
    /// 一个 Label 都没有时保持原样，键值直接作为当前/目标。
    /// </summary>
    public override DetectResult Detect(TaskContext ctx)
    {
        var rawCurrent = new List<string>();
        var rawTarget = new List<string>();
        var values = new List<(RegistryEntry Entry, object? Value)>();
        var satisfied = true;
        foreach (var e in _entries)
        {
            var (v, _) = ctx.Registry.GetValue(e.Root, e.Key, e.Name);
            var label = Label(e);
            rawCurrent.Add($"{label}={(v == null ? "未设置" : v.ToString())}");
            rawTarget.Add($"{label}={e.Target}");
            values.Add((e, v));
            if (!Matches(v, e)) satisfied = false;
        }
        var raw = new DetectResult(satisfied, string.Join("; ", rawCurrent), string.Join("; ", rawTarget));
        if (_entries.All(e => e.Label == null)) return raw;

        return new DetectResult(satisfied,
            Friendly(values.Select(x => (x.Entry, x.Entry.NameOf(x.Value)))),
            Friendly(values.Select(x => (x.Entry, x.Entry.NameOf(x.Entry.Target)))),
            null,
            raw.CurrentValue + " → " + raw.TargetValue);
    }

    /// <summary>同一 Label 下多个条目含义一致时合并成一句；不一致时把各含义都列出来。</summary>
    private static string Friendly(IEnumerable<(RegistryEntry Entry, string Name)> items)
    {
        var parts = new List<string>();
        foreach (var g in items.Where(i => i.Entry.Label != null).GroupBy(i => i.Entry.Label!))
        {
            var names = g.Select(i => i.Name).Distinct().ToList();
            parts.Add(names.Count == 1 ? $"{g.Key}：{names[0]}" : $"{g.Key}：{string.Join(" / ", names)}（各项不一致）");
        }
        return string.Join("；", parts);
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
