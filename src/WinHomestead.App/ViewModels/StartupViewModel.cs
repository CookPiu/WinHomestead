using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinHomestead.Core.Models;
using WinHomestead.Native;

namespace WinHomestead.App.ViewModels;

/// <summary>一条自启项。开关只写 StartupApproved，Run 键与快捷方式本身不动，随时能开回来。</summary>
public sealed partial class StartupRowViewModel : ObservableObject
{
    private readonly Func<StartupRowViewModel, bool, bool> _toggle;

    /// <summary>注册表里当前的状态。Item.Enabled 是刚读出来时的值，切换成功后不会变，不能拿它做比较。</summary>
    private bool _current;
    private bool _suppress;

    public StartupRowViewModel(StartupItem item, Func<StartupRowViewModel, bool, bool> toggle)
    {
        Item = item; _toggle = toggle;
        _current = item.Enabled;
        _enabled = item.Enabled;
    }

    public StartupItem Item { get; }
    public string Name => Item.Name;
    public string Command => Item.Command;
    public string SourceName => Item.SourceName;
    public bool NeedsAdmin => Item.NeedsAdmin;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private string _error = string.Empty;
    public bool HasError => Error.Length > 0;

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    /// <summary>开关由界面直接绑定；写失败时把值弹回去并显示原因。</summary>
    partial void OnEnabledChanged(bool value)
    {
        if (_suppress || _current == value) { Error = string.Empty; return; }
        if (_toggle(this, value)) { _current = value; Error = string.Empty; return; }

        _suppress = true;
        Enabled = _current;
        _suppress = false;
    }

    [RelayCommand]
    private void OpenLocation()
    {
        try
        {
            var path = Command.Trim('"');
            var quote = path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (quote > 0) path = path.Substring(0, quote + 4);
            if (System.IO.File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
        }
        catch { }
    }
}

/// <summary>
/// 启动项页：列出 Run 键与启动文件夹里的自启项，逐项开关。
/// 用 StartupApproved 判断真实状态——被任务管理器禁用过的项仍留在 Run 键里，只看 Run 键会把它们也数进来。
/// </summary>
public sealed partial class StartupViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _back;
    private readonly StartupItems _startup;

    public StartupViewModel(AppServices services, Action back)
    {
        _services = services; _back = back;
        _startup = new StartupItems(services.Execution.Registry, services.Logger);
        _ = LoadAsync();
    }

    public ObservableCollection<StartupRowViewModel> Rows { get; } = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "正在读取…";

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true; Status = "正在读取…";
        try
        {
            var items = await Task.Run(() => _startup.Enumerate());
            Rows.Clear();
            foreach (var i in items) Rows.Add(new StartupRowViewModel(i, Toggle));
            var on = items.Count(i => i.Enabled);
            Status = items.Count == 0
                ? "没有读到自启项。"
                : $"{items.Count} 项，其中 {on} 项会随开机启动。关掉只是不再自动启动，程序本身还在。";
        }
        catch (Exception ex)
        {
            _services.Logger.Error("读取启动项失败", ex);
            Status = "读取失败：" + ex.Message;
        }
        finally { IsBusy = false; }
    }

    /// <summary>写 StartupApproved，经会话 journal 记账，失败返回 false 由行自己弹回开关。</summary>
    private bool Toggle(StartupRowViewModel row, bool enabled)
    {
        var snapshot = _services.Session.Snapshot;
        if (snapshot == null) { row.Error = "还没探测完，稍后再试"; return false; }
        try
        {
            var answers = _services.Session.Answers ?? new Answers(snapshot.DataDrive, false);
            var ctx = _services.Runner.CreateJournaledContext("startup." + row.Name, snapshot, answers);
            _startup.SetEnabled(ctx.Registry, row.Item, enabled);
            _services.Logger.Info($"[startup] {row.Name} → {(enabled ? "启用" : "禁用")}");
            return true;
        }
        catch (Exception ex)
        {
            _services.Logger.Error($"切换自启项 {row.Name} 失败", ex);
            row.Error = row.NeedsAdmin ? "写入失败（这一项属于所有用户，需要管理员权限）：" + ex.Message : "写入失败：" + ex.Message;
            return false;
        }
    }

    [RelayCommand]
    private void Back() => _back();
}
