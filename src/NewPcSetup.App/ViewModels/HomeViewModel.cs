using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewPcSetup.Core.Models;

namespace NewPcSetup.App.ViewModels;

/// <summary>列表中的一项：状态 + 立即执行按钮。</summary>
public sealed partial class TaskItemViewModel : ObservableObject
{
    private readonly HomeViewModel _owner;

    public TaskItemViewModel(HomeViewModel owner, PlanItem item, TaskResult? result)
    {
        _owner = owner;
        TaskId = item.TaskId;
        DisplayName = item.DisplayName;
        Description = item.Description;
        Recommended = item.Checked;
        IsIrreversible = (item.Risk & (RiskFlags.Reversible | RiskFlags.PromptOnly)) == 0;
        NeedsConfirm = IsIrreversible || (item.Risk & RiskFlags.AdminOnly) != 0;
        var risks = new List<string>();
        if ((item.Risk & RiskFlags.Reversible) != 0) risks.Add("可撤销");
        if ((item.Risk & RiskFlags.NeedsExplorerRestart) != 0) risks.Add("重启资源管理器生效");
        if ((item.Risk & RiskFlags.NeedsSignOut) != 0) risks.Add("注销后生效");
        if ((item.Risk & RiskFlags.NeedsReboot) != 0) risks.Add("需重启");
        if ((item.Risk & RiskFlags.AdminOnly) != 0) risks.Add("系统级");
        if ((item.Risk & RiskFlags.PromptOnly) != 0) risks.Add("只给步骤");
        if (IsIrreversible) risks.Add("不可撤销");
        RiskLabel = string.Join(" · ", risks);
        Update(item, result);
    }

    public string TaskId { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string RiskLabel { get; }
    public bool Recommended { get; }
    public bool IsIrreversible { get; }
    public bool NeedsConfirm { get; }

    [ObservableProperty] private string _statusLabel = string.Empty;
    [ObservableProperty] private string _currentValue = string.Empty;
    [ObservableProperty] private string _targetValue = string.Empty;
    [ObservableProperty] private bool _showValues;
    [ObservableProperty] private bool _isPlanned;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _canRun;
    [ObservableProperty] private string _resultMessage = string.Empty;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private bool _resultOk;
    [ObservableProperty] private string _buttonText = "执行";

    public void Update(PlanItem item, TaskResult? result)
    {
        CurrentValue = item.CurrentValue ?? string.Empty;
        TargetValue = item.TargetValue ?? string.Empty;
        IsPlanned = item.State == PlanState.Planned;
        ShowValues = IsPlanned && (CurrentValue.Length > 0 || TargetValue.Length > 0);
        StatusLabel = item.State switch
        {
            PlanState.Skipped => "已满足",
            PlanState.NotApplicable => "不适用" + Reason(item.Reason),
            _ => Recommended ? "推荐" : "可选",
        };
        ButtonText = IsPlanned && result != null && result.Outcome is TaskOutcome.Failed or TaskOutcome.RolledBack ? "重试" : "执行";
        SetResult(result);
        RefreshCanRun();
    }

    public void SetResult(TaskResult? result)
    {
        HasResult = result != null;
        if (result == null) { ResultMessage = string.Empty; ResultOk = false; return; }
        ResultOk = result.Outcome is TaskOutcome.Done or TaskOutcome.NeedsReboot;
        var label = Core.Infrastructure.ReportHtml.OutcomeLabel(result.Outcome);
        ResultMessage = string.IsNullOrEmpty(result.Message) ? label : $"{label}：{result.Message}";
        if (result.ManualSteps.Count > 0) ResultMessage += Environment.NewLine + string.Join(Environment.NewLine, result.ManualSteps);
    }

    public void RefreshCanRun() => CanRun = IsPlanned && !IsRunning && !_owner.IsExecuting && !_owner.IsBusy;

    private static string Reason(string? reason)
    {
        if (reason == null) return string.Empty;
        var i = reason.IndexOf(':');
        return i >= 0 ? "：" + reason.Substring(i + 1).Trim() : string.Empty;
    }

    [RelayCommand]
    private Task Run() => _owner.RunAsync(this);
}

public sealed partial class CategoryViewModel : ObservableObject
{
    public CategoryViewModel(string key)
    {
        Key = key;
        Title = key switch
        {
            "disk" => "分区",
            "path" => "磁盘与路径",
            "env" => "开发缓存",
            "ui" => "界面与交互",
            "ime" => "中文输入法",
            "promo" => "去推送",
            "gpu" => "显卡",
            "storage" => "C 盘治理",
            _ => key,
        };
    }
    public string Key { get; }
    public string Title { get; }
    public ObservableCollection<TaskItemViewModel> Items { get; } = new();
    [ObservableProperty] private string _subtitle = string.Empty;

    public void RefreshSubtitle()
    {
        var runnable = Items.Count(i => i.IsPlanned);
        Subtitle = runnable == 0 ? "全部已满足" : $"{runnable} 项可执行";
    }
}

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Dictionary<string, TaskItemViewModel> _byId = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cancel;
    private bool _suppressDriveChange;

    public HomeViewModel(AppServices services)
    {
        _services = services;
        if (services.Session.Snapshot != null) Populate(services.Session.Snapshot);
        else _ = DetectAsync();
    }

    public ObservableCollection<CategoryViewModel> Categories { get; } = new();
    public ObservableCollection<string> Warnings { get; } = new();
    public List<OptionItem> DataDriveOptions { get; private set; } = new();

    [ObservableProperty] private CategoryViewModel? _selectedCategory;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isExecuting;
    [ObservableProperty] private string _status = "正在探测这台电脑…";
    [ObservableProperty] private string _machineSummary = string.Empty;
    [ObservableProperty] private string _dataDrive = string.Empty;
    [ObservableProperty] private bool _hasDataDrive;
    [ObservableProperty] private bool _explorerRestartPending;
    [ObservableProperty] private bool _rebootPending;
    [ObservableProperty] private bool _canStop;
    [ObservableProperty] private bool _desktopProtected;
    [ObservableProperty] private string _runningLabel = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;

    public event Action? RequestShowReport;

    // ---- 探测与列表 ----

    [RelayCommand]
    private async Task DetectAsync()
    {
        if (IsBusy) return;
        IsBusy = true; Status = "正在探测这台电脑…";
        RefreshAllCanRun();
        try
        {
            var snapshot = await Task.Run(() => _services.Collector.Collect());
            _services.Session.Snapshot = snapshot;
            _services.Store.Save("snapshot-latest.json", snapshot);
            Populate(snapshot);
        }
        catch (Exception ex)
        {
            _services.Logger.Error("探测失败", ex);
            Status = "探测失败：" + ex.Message;
        }
        finally { IsBusy = false; RefreshAllCanRun(); }
    }

    private void Populate(EnvironmentSnapshot s)
    {
        MachineSummary = $"{s.OsCaption} · {s.Manufacturer} {s.Model} · {(s.IsLaptop ? "笔记本" : "台式机")} · {s.RamGb} GB · " +
                         string.Join("；", s.Volumes.Select(v => $"{v.DriveLetter} {v.SizeGb:F0} GB（剩 {v.FreeGb:F0} GB）"));

        var options = s.Volumes.Where(v => !v.IsSystem)
            .Select(v => new OptionItem($"{v.DriveLetter} {v.Label}（{v.SizeGb:F0} GB，剩 {v.FreeGb:F0} GB）", v.DriveLetter)).ToList();
        HasDataDrive = options.Count > 0;
        if (options.Count == 0) options.Add(new OptionItem("无数据盘", string.Empty));
        DataDriveOptions = options;
        OnPropertyChanged(nameof(DataDriveOptions));

        var answers = _services.Session.Answers ?? Answers.Default(s);
        if (answers.DataDrive != null && options.All(o => !string.Equals(o.Value as string, answers.DataDrive, StringComparison.OrdinalIgnoreCase)))
            answers = answers with { DataDrive = s.DataDrive };
        _services.Session.Answers = answers;
        _suppressDriveChange = true;
        DataDrive = answers.DataDrive ?? string.Empty;
        _suppressDriveChange = false;

        Warnings.Clear();
        if (!s.IsWindows11) Warnings.Add("当前不是 Windows 11，部分设置项可能不适用。");
        if (s.IsMdmEnrolled || s.IsDomainJoined) Warnings.Add("检测到此电脑受组织管理（MDM/域），系统级改动不会列出或只给步骤。");
        if (s.DataDrive == null) Warnings.Add("未检测到数据盘：路径与缓存迁移不会出现；单盘可先看“分区”分类。");
        DesktopProtected = s.OneDrive.DesktopProtected;
        var sys = s.Volumes.FirstOrDefault(v => v.IsSystem);
        if (sys != null && sys.FreeGb < 40) Warnings.Add($"系统盘剩余仅 {sys.FreeGb:F0} GB，建议优先执行路径迁移并查看“C 盘”页。");

        RebuildPlan(s, answers);
        Status = "探测完成";
    }

    /// <summary>重新 Detect 全部条目并就地刷新列表，保留已有执行结果。</summary>
    private void RebuildPlan(EnvironmentSnapshot s, Answers answers)
    {
        var plan = _services.Planner.Build(_services.Catalog, s, answers);
        _services.Session.Plan = plan;
        try { _services.Runner.SavePlan(plan, s); } catch (Exception ex) { _services.Logger.Warn("保存列表失败: " + ex.Message); }
        ApplyPlan(plan);
    }

    private void ApplyPlan(Plan plan)
    {
        var results = _services.Runner.Results.ToDictionary(r => r.TaskId, StringComparer.OrdinalIgnoreCase);
        var selectedKey = SelectedCategory?.Key;
        var order = plan.Items.Select(i => i.Module).Distinct().ToList();
        var existing = Categories.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);

        // 分类集合尽量就地更新，避免列表闪动
        foreach (var key in order)
        {
            if (!existing.TryGetValue(key, out var cat))
            {
                cat = new CategoryViewModel(key);
                Categories.Insert(Math.Min(order.IndexOf(key), Categories.Count), cat);
                existing[key] = cat;
            }
            var items = plan.Items.Where(i => i.Module == key).ToList();
            var known = cat.Items.ToDictionary(i => i.TaskId, StringComparer.OrdinalIgnoreCase);
            for (var idx = 0; idx < items.Count; idx++)
            {
                var item = items[idx];
                results.TryGetValue(item.TaskId, out var r);
                if (known.TryGetValue(item.TaskId, out var vm)) { vm.Update(item, r); continue; }
                vm = new TaskItemViewModel(this, item, r);
                cat.Items.Insert(Math.Min(idx, cat.Items.Count), vm);
                _byId[item.TaskId] = vm;
            }
            foreach (var stale in cat.Items.Where(i => items.All(p => !string.Equals(p.TaskId, i.TaskId, StringComparison.OrdinalIgnoreCase))).ToList())
            {
                cat.Items.Remove(stale); _byId.Remove(stale.TaskId);
            }
            cat.RefreshSubtitle();
        }
        foreach (var stale in Categories.Where(c => !order.Contains(c.Key)).ToList()) Categories.Remove(stale);

        SelectedCategory = Categories.FirstOrDefault(c => c.Key == selectedKey) ?? Categories.FirstOrDefault();
        var runnable = plan.Items.Count(i => i.State == PlanState.Planned);
        Summary = $"共 {plan.Items.Count} 项 · 可执行 {runnable} 项 · 已满足 {plan.SkippedCount} 项";
        RefreshAllCanRun();
    }

    partial void OnDataDriveChanged(string value)
    {
        if (_suppressDriveChange || _services.Session.Snapshot == null) return;
        var answers = (_services.Session.Answers ?? Answers.Default(_services.Session.Snapshot)) with { DataDrive = string.IsNullOrEmpty(value) ? null : value };
        _services.Session.Answers = answers;
        RebuildPlan(_services.Session.Snapshot, answers);
    }

    private void RefreshAllCanRun()
    {
        foreach (var vm in _byId.Values) vm.RefreshCanRun();
    }

    // ---- 执行 ----

    public async Task RunAsync(TaskItemViewModel item)
    {
        if (IsExecuting || IsBusy || _services.Session.Snapshot == null || _services.Session.Plan == null) return;
        if (item.NeedsConfirm)
        {
            var text = item.IsIrreversible
                ? $"“{item.DisplayName}”不可撤销。\n\n{item.TargetValue}\n\n确定执行？"
                : $"“{item.DisplayName}”会改动系统级设置。\n\n{item.TargetValue}\n\n确定执行？";
            if (MessageBox.Show(text, "新机开荒", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        }

        IsExecuting = true;
        item.IsRunning = true;
        RunningLabel = "正在执行：" + item.DisplayName;
        _cancel = new CancellationTokenSource();
        // 取消只在任务边界生效：单项执行常常只有一个任务，带上依赖时才真正有得停
        CanStop = true;
        RefreshAllCanRun();
        var status = new Progress<string>(s => { if (s.Length > 0) RunningLabel = s; });
        var progress = new Progress<RunnerProgress>(p =>
        {
            if (p.LastResult == null) RunningLabel = "正在执行：" + p.CurrentDisplayName;
            else if (_byId.TryGetValue(p.LastResult.TaskId, out var vm)) vm.SetResult(p.LastResult);
        });
        try
        {
            var snapshot = _services.Session.Snapshot;
            var plan = _services.Session.Plan;
            var token = _cancel.Token;
            await Task.Run(() => _services.Runner.Run(item.TaskId, plan, _services.Catalog, snapshot, progress, status, token), token);
        }
        catch (Exception ex)
        {
            _services.Logger.Error("执行异常", ex);
            var failure = new TaskResult(item.TaskId, item.DisplayName, TaskOutcome.Failed, ex.Message, 0, Array.Empty<string>());
            _services.Runner.RecordFailure(failure);
            item.SetResult(failure);
        }
        finally
        {
            item.IsRunning = false;
            CanStop = false;
            _cancel?.Dispose();
            _cancel = null;
            ExplorerRestartPending = _services.Runner.ExplorerRestartPending;
            RebootPending = _services.Runner.RebootPending;
            RunningLabel = "正在刷新状态…";
            try
            {
                var s = _services.Session.Snapshot;
                var a = _services.Session.Answers ?? Answers.Default(s);
                if (item.TaskId.StartsWith("disk.", StringComparison.Ordinal))
                {
                    // 分区后卷列表变了，重新探测
                    s = await Task.Run(() => _services.Collector.Collect());
                    _services.Session.Snapshot = s;
                    _services.Session.Answers = null;
                    Populate(s);
                }
                else
                {
                    var plan = await Task.Run(() => _services.Planner.Build(_services.Catalog, s, a));
                    _services.Session.Plan = plan;
                    ApplyPlan(plan);
                }
            }
            catch (Exception ex) { _services.Logger.Warn("刷新状态失败: " + ex.Message); }
            RunningLabel = string.Empty;
            IsExecuting = false;
            RefreshAllCanRun();
        }
    }

    /// <summary>请求在当前任务完成后停止；不打断正在写入的任务。</summary>
    [RelayCommand]
    private void Stop()
    {
        if (_cancel == null || _cancel.IsCancellationRequested) return;
        _cancel.Cancel();
        CanStop = false;
        RunningLabel = "已请求停止，等当前这项做完…";
    }

    /// <summary>OneDrive 只提供 odopen 协议，协议不可用时退回直接启动 OneDrive.exe /settings。</summary>
    [RelayCommand]
    private void OpenOneDriveSettings()
    {
        try { Process.Start(new ProcessStartInfo("odopen://launch/settings") { UseShellExecute = true }); return; }
        catch (Exception ex) { _services.Logger.Warn("odopen 打开失败: " + ex.Message); }
        foreach (var exe in new[]
                 {
                     System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\OneDrive\OneDrive.exe"),
                     System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft OneDrive\OneDrive.exe"),
                 })
        {
            try
            {
                if (!System.IO.File.Exists(exe)) continue;
                Process.Start(new ProcessStartInfo(exe, "/settings") { UseShellExecute = true });
                return;
            }
            catch (Exception ex) { _services.Logger.Warn("启动 OneDrive 设置失败: " + ex.Message); }
        }
        MessageBox.Show("没能自动打开 OneDrive 设置。请在任务栏右下角右键 OneDrive 图标 → 设置 → 同步和备份 → 管理备份，关掉桌面备份后回来点“重新探测”。",
            "新机开荒", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void RestartExplorer()
    {
        try { _services.Runner.RestartExplorer(); }
        catch (Exception ex) { _services.Logger.Warn("重启 Explorer 失败: " + ex.Message); }
        ExplorerRestartPending = _services.Runner.ExplorerRestartPending;
    }

    [RelayCommand]
    private void RebootNow() => RebootHelper.RebootWithResume(_services);

    [RelayCommand]
    private void ShowReport() => RequestShowReport?.Invoke();
}

public sealed class OptionItem
{
    public OptionItem(string label, object value) { Label = label; Value = value; }
    public string Label { get; }
    public object Value { get; }
}
