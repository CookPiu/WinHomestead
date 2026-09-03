using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewPcSetup.Core.Models;

namespace NewPcSetup.App.ViewModels;

public sealed partial class SoftwareItemViewModel : ObservableObject
{
    public SoftwareItemViewModel(SoftwareEntry e, string? appsRoot)
    {
        Entry = e;
        Name = e.Name;
        Url = e.Url;
        Hint = SoftwareCatalog.InstallerHint(e) + (e.Note != null ? "。" + e.Note : string.Empty);
        RecommendedPath = appsRoot != null && e.CustomPath && e.InstallDir.Length > 0 ? Path.Combine(appsRoot, e.InstallDir) : string.Empty;
        HasPath = RecommendedPath.Length > 0;
    }

    public SoftwareEntry Entry { get; }
    public string Name { get; }
    public string Url { get; }
    public string Hint { get; }
    public string RecommendedPath { get; }
    public bool HasPath { get; }
    [ObservableProperty] private bool _installed;

    [RelayCommand]
    private void OpenSite()
    {
        try { Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true }); }
        catch { Clipboard.SetText(Url); }
    }

    [RelayCommand]
    private void CopyPath() { if (HasPath) Clipboard.SetText(RecommendedPath); }
}

public sealed class SoftwareGroupViewModel
{
    public SoftwareGroupViewModel(string category, IEnumerable<SoftwareItemViewModel> items)
    {
        Title = SoftwareCatalog.CategoryLabel(category);
        Items = new ObservableCollection<SoftwareItemViewModel>(items);
    }
    public string Title { get; }
    public ObservableCollection<SoftwareItemViewModel> Items { get; }
}

public sealed partial class SoftwareViewModel : ObservableObject
{
    private static readonly string[] CategoryOrder = { "tools", "browser", "im", "office", "media", "dev", "game", "runtime" };
    private readonly AppServices _services;
    private readonly Action _back;

    public SoftwareViewModel(AppServices services, Action back)
    {
        _services = services; _back = back;
        var s = services.Session.Snapshot;
        var usage = services.Session.Answers?.Usage ?? Usage.Home;
        var drive = services.Session.Answers?.DataDrive ?? s?.DataDrive;
        var appsRoot = drive != null ? drive + @"\Applications" : null;
        AppsRoot = appsRoot ?? "（未检测到数据盘，建议先完成路径设置）";

        var usageKey = usage.ToString().ToLowerInvariant();
        var entries = services.Software.Entries
            .OrderBy(e => Array.IndexOf(CategoryOrder, e.Category) is var i && i >= 0 ? i : 99)
            .ThenByDescending(e => e.Usages.Contains(usageKey))
            .ToList();
        Groups = new ObservableCollection<SoftwareGroupViewModel>(entries.GroupBy(e => e.Category)
            .Select(g => new SoftwareGroupViewModel(g.Key, g.Select(e => new SoftwareItemViewModel(e, appsRoot)))));
        Refresh();
    }

    public string AppsRoot { get; }
    public ObservableCollection<SoftwareGroupViewModel> Groups { get; }

    [RelayCommand]
    private void Refresh()
    {
        IReadOnlyList<string> names;
        try { names = _services.Installed.DisplayNames(); } catch { names = Array.Empty<string>(); }
        foreach (var g in Groups)
            foreach (var i in g.Items)
                i.Installed = i.Entry.Detect.Length > 0 && names.Any(n => n.IndexOf(i.Entry.Detect, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [RelayCommand]
    private void Back() => _back();
}
