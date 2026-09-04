using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NewPcSetup.App.ViewModels;

/// <summary>软件页记住的目录设置：根目录 + 逐项覆盖，存 %ProgramData%\NewPcSetup\software-paths.json。</summary>
public sealed class SoftwarePathSettings
{
    public string? Root { get; set; }
    public Dictionary<string, string> Overrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed partial class SoftwareItemViewModel : ObservableObject
{
    private readonly Action<SoftwareItemViewModel> _pathChanged;
    private bool _suppress;

    public SoftwareItemViewModel(SoftwareEntry e, Action<SoftwareItemViewModel> pathChanged)
    {
        Entry = e;
        _pathChanged = pathChanged;
        Name = e.Name;
        Url = e.Url;
        Hint = SoftwareCatalog.InstallerHint(e) + (e.Note != null ? "。" + e.Note : string.Empty);
        CanHavePath = e.CustomPath && e.InstallDir.Length > 0;
    }

    public SoftwareEntry Entry { get; }
    public string Name { get; }
    public string Url { get; }
    public string Hint { get; }
    /// <summary>安装器不支持自定义路径的项没有路径框，只给说明。</summary>
    public bool CanHavePath { get; }

    [ObservableProperty] private bool _installed;
    [ObservableProperty] private string _recommendedPath = string.Empty;
    [ObservableProperty] private bool _isCustomPath;

    /// <summary>由 SoftwareViewModel 推算默认路径时调用，不触发保存。</summary>
    public void SetPath(string path, bool custom)
    {
        _suppress = true;
        RecommendedPath = path;
        IsCustomPath = custom;
        _suppress = false;
    }

    partial void OnRecommendedPathChanged(string value)
    {
        if (_suppress) return;
        _pathChanged(this);
    }

    [RelayCommand]
    private void OpenSite()
    {
        try { Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true }); }
        catch { Clipboard.SetText(Url); }
    }

    [RelayCommand]
    private void CopyPath()
    {
        if (RecommendedPath.Length > 0) Clipboard.SetText(RecommendedPath);
    }
}

public sealed class SoftwareGroupViewModel
{
    public SoftwareGroupViewModel(string category, IEnumerable<SoftwareItemViewModel> items)
    {
        Title = SoftwareCatalog.CategoryLabel(category);
        Hint = SoftwareCatalog.CategoryHint(category);
        Items = new ObservableCollection<SoftwareItemViewModel>(items);
    }
    public string Title { get; }
    public string Hint { get; }
    public ObservableCollection<SoftwareItemViewModel> Items { get; }
}

/// <summary>
/// 软件推荐页：给的是"常见选择 + 建议装到哪"，不是待办清单。
/// 目录先规划再推荐：根目录可改，分类各占一个子目录，单项路径也能自己改，改完记住。
/// </summary>
public sealed partial class SoftwareViewModel : ObservableObject
{
    private const string SettingsFile = "software-paths.json";
    private static readonly string[] CategoryOrder = { "tools", "browser", "im", "office", "media", "dev", "game", "runtime" };

    private readonly AppServices _services;
    private readonly Action _back;
    private readonly SoftwarePathSettings _settings;
    private bool _loading;

    public SoftwareViewModel(AppServices services, Action back)
    {
        _services = services; _back = back;
        _settings = LoadSettings(services);

        var s = services.Session.Snapshot;
        var drive = services.Session.Answers?.DataDrive ?? s?.DataDrive;
        DefaultRoot = drive != null ? drive + @"\Applications" : @"C:\Applications";
        _loading = true;
        Root = _settings.Root ?? DefaultRoot;
        _loading = false;

        Groups = new ObservableCollection<SoftwareGroupViewModel>(
            services.Software.Entries
                .OrderBy(e => Array.IndexOf(CategoryOrder, e.Category) is var i && i >= 0 ? i : 99)
                .GroupBy(e => e.Category)
                .Select(g => new SoftwareGroupViewModel(g.Key, g.Select(e => new SoftwareItemViewModel(e, OnItemPathChanged)))));

        ApplyPaths();
        Refresh();
    }

    /// <summary>数据盘下的默认根目录，"恢复默认"用它。</summary>
    public string DefaultRoot { get; }
    public ObservableCollection<SoftwareGroupViewModel> Groups { get; }

    [ObservableProperty] private string _root = string.Empty;
    [ObservableProperty] private string _hint = string.Empty;

    private IEnumerable<SoftwareItemViewModel> AllItems => Groups.SelectMany(g => g.Items);

    partial void OnRootChanged(string value)
    {
        if (_loading) return;
        _settings.Root = string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('\\');
        ApplyPaths();
        Save();
    }

    /// <summary>按当前根目录与分类子目录推算每项路径；被用户改过的项保留用户的值。</summary>
    private void ApplyPaths()
    {
        var root = (string.IsNullOrWhiteSpace(Root) ? DefaultRoot : Root).Trim().TrimEnd('\\');
        foreach (var item in AllItems)
        {
            if (!item.CanHavePath) { item.SetPath(string.Empty, false); continue; }
            if (_settings.Overrides.TryGetValue(item.Entry.Id, out var custom) && custom.Length > 0)
            {
                item.SetPath(custom, true);
                continue;
            }
            item.SetPath(Path.Combine(root, SoftwareCatalog.CategoryDir(item.Entry.Category), item.Entry.InstallDir), false);
        }
        Hint = $"分类各占一个子目录，例如 {Path.Combine(root, "Dev", "VSCode")}。路径只是建议，想改就改。";
    }

    private void OnItemPathChanged(SoftwareItemViewModel item)
    {
        var root = (string.IsNullOrWhiteSpace(Root) ? DefaultRoot : Root).Trim().TrimEnd('\\');
        var fallback = Path.Combine(root, SoftwareCatalog.CategoryDir(item.Entry.Category), item.Entry.InstallDir);
        var value = item.RecommendedPath.Trim();
        if (value.Length == 0 || string.Equals(value, fallback, StringComparison.OrdinalIgnoreCase))
        {
            _settings.Overrides.Remove(item.Entry.Id);
            item.SetPath(fallback, false);
        }
        else
        {
            _settings.Overrides[item.Entry.Id] = value;
            item.SetPath(value, true);
        }
        Save();
    }

    private static SoftwarePathSettings LoadSettings(AppServices services)
    {
        try { return services.Store.Load<SoftwarePathSettings>(SettingsFile) ?? new SoftwarePathSettings(); }
        catch (Exception ex) { services.Logger.Warn("读取软件目录设置失败: " + ex.Message); return new SoftwarePathSettings(); }
    }

    private void Save()
    {
        try { _services.Store.Save(SettingsFile, _settings); }
        catch (Exception ex) { _services.Logger.Warn("保存软件目录设置失败: " + ex.Message); }
    }

    [RelayCommand]
    private void ResetPaths()
    {
        _settings.Overrides.Clear();
        _settings.Root = null;
        _loading = true;
        Root = DefaultRoot;
        _loading = false;
        ApplyPaths();
        Save();
    }

    [RelayCommand]
    private void Refresh()
    {
        IReadOnlyList<string> names;
        try { names = _services.Installed.DisplayNames(); } catch { names = Array.Empty<string>(); }
        foreach (var i in AllItems)
            i.Installed = i.Entry.Detect.Length > 0 && names.Any(n => n.IndexOf(i.Entry.Detect, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>商店"新应用保存位置"无公开接口，只能把用户带到设置页自己改。</summary>
    [RelayCommand]
    private void OpenSaveLocations()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:savelocations") { UseShellExecute = true }); }
        catch (Exception ex) { _services.Logger.Warn("打开保存位置设置失败: " + ex.Message); }
    }

    [RelayCommand]
    private void Back() => _back();
}
