using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinHomestead.Core.Models;

namespace WinHomestead.App.ViewModels;

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
    /// <summary>不适用的原因，单独一行显示，不塞进徽标。</summary>
    [ObservableProperty] private string _reasonText = string.Empty;
    [ObservableProperty] private bool _hasReason;
    [ObservableProperty] private string _currentValue = string.Empty;
    [ObservableProperty] private string _targetValue = string.Empty;
    /// <summary>原始键值，悬停在“当前/目标”上时显示。</summary>
    [ObservableProperty] private string _detail = string.Empty;
    [ObservableProperty] private bool _hasDetail;
    [ObservableProperty] private bool _showValues;
    [ObservableProperty] private bool _showRisk;
    [ObservableProperty] private bool _isPlanned;
    [ObservableProperty] private bool _isSkipped;
    [ObservableProperty] private bool _isNotApplicable;
    /// <summary>不适用项的辅助动作，如桌面被 OneDrive 接管时的“打开 OneDrive 设置”。</summary>
    [ObservableProperty] private string _auxLabel = string.Empty;
    [ObservableProperty] private bool _hasAux;
    private Action? _aux;
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
        Detail = item.Detail ?? string.Empty;
        HasDetail = Detail.Length > 0;
        IsPlanned = item.State == PlanState.Planned;
        IsSkipped = item.State == PlanState.Skipped;
        IsNotApplicable = item.State == PlanState.NotApplicable;
        ShowValues = IsPlanned && (CurrentValue.Length > 0 || TargetValue.Length > 0);
        // 已满足的项不再显示风险标签，那是给“要不要点”看的
        ShowRisk = IsPlanned && RiskLabel.Length > 0;
        StatusLabel = item.State switch
        {
            PlanState.Skipped => "已满足",
            PlanState.NotApplicable => "不适用",
            _ => Recommended ? "推荐" : "可选",
        };
        ReasonText = IsNotApplicable ? Reason(item.Reason) : string.Empty;
        HasReason = ReasonText.Length > 0;
        var aux = _owner.AuxActionFor(item);
        _aux = aux?.Action;
        AuxLabel = aux?.Label ?? string.Empty;
        HasAux = _aux != null;
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
        return i >= 0 ? reason.Substring(i + 1).Trim() : reason;
    }

    [RelayCommand]
    private Task Run() => _owner.RunAsync(this);

    [RelayCommand]
    private void Aux() => _aux?.Invoke();
}

/// <summary>条目卡片上的辅助动作：不改系统，只是把用户带到该去的地方。</summary>
public sealed record AuxAction(string Label, Action Action);

/// <summary>一条黄色提示。用户点 × 后本次运行不再出现，重新探测也不会再冒出来。</summary>
public sealed partial class WarningViewModel : ObservableObject
{
    private readonly Action<string> _onDismiss;

    public WarningViewModel(string text, Action<string> onDismiss)
    {
        Text = text; _onDismiss = onDismiss;
    }

    public string Text { get; }
    [ObservableProperty] private bool _isOpen = true;

    partial void OnIsOpenChanged(bool value)
    {
        if (!value) _onDismiss(Text);
    }
}

public sealed partial class CategoryViewModel : ObservableObject
{
    public const string MachineKey = "machine";

    public CategoryViewModel(string key)
    {
        Key = key;
        IsMachineInfo = key == MachineKey;
        Title = key switch
        {
            MachineKey => "这台电脑",
            "disk" => "分区",
            "path" => "磁盘与路径",
            "env" => "开发缓存",
            "ui" => "界面与交互",
            "ime" => "中文输入法",
            "promo" => "去推送",
            "gpu" => "显卡",
            "storage" => "空间清理",
            _ => key,
        };
    }
    public string Key { get; }
    public string Title { get; }
    public bool IsMachineInfo { get; }
    public bool IsTaskList => !IsMachineInfo;
    public ObservableCollection<TaskItemViewModel> Items { get; } = new();
    [ObservableProperty] private string _subtitle = string.Empty;

    public void RefreshSubtitle()
    {
        if (IsMachineInfo) return;
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
        _ = services.Session.Snapshot != null ? PopulateAsync(services.Session.Snapshot) : DetectAsync();
    }

    public ObservableCollection<CategoryViewModel> Categories { get; } = new();
    public ObservableCollection<WarningViewModel> Warnings { get; } = new();
    /// <summary>本次运行里被关掉的提示，重新探测后不再重复弹。</summary>
    private readonly HashSet<string> _dismissedWarnings = new(StringComparer.Ordinal);
    /// <summary>“这台电脑”栏位的内容；容量一律给精确值，不做四舍五入。</summary>
    public ObservableCollection<MachineRow> MachineInfo { get; } = new();
    public List<OptionItem> DataDriveOptions { get; private set; } = new();

    [ObservableProperty] private CategoryViewModel? _selectedCategory;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isExecuting;
    [ObservableProperty] private string _status = "正在探测这台电脑…";
    [ObservableProperty] private string _dataDrive = string.Empty;
    [ObservableProperty] private bool _hasDataDrive;
    [ObservableProperty] private bool _explorerRestartPending;
    [ObservableProperty] private bool _rebootPending;
    [ObservableProperty] private bool _canStop;
    [ObservableProperty] private string _runningLabel = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;

    public event Action? RequestShowReport;
    /// <summary>列表重新生成后触发（探测、换盘、执行完），主窗口据此刷新“执行记录”按钮上的手动项数量。</summary>
    public event Action? Refreshed;

    /// <summary>桌面被 OneDrive 接管时，“打开 OneDrive 设置”直接放在桌面条目的卡片上，不再另起一条头部提示。</summary>
    public AuxAction? AuxActionFor(PlanItem item)
    {
        var s = _services.Session.Snapshot;
        if (item.State == PlanState.NotApplicable && s != null && s.OneDrive.DesktopProtected
            && string.Equals(item.TaskId, "path.known_folder.desktop", StringComparison.OrdinalIgnoreCase))
            return new AuxAction("打开 OneDrive 设置", OpenOneDriveSettings);
        return null;
    }

    // ---- 探测与列表 ----

    [RelayCommand]
    private async Task DetectAsync()
    {
        if (IsBusy) return;
        IsBusy = true; Status = "正在探测这台电脑…";
        RefreshAllCanRun();
        try
        {
            var snapshot = await Task.Run(() =>
            {
                var s = _services.Collector.Collect();
                try { _services.Store.Save("snapshot-latest.json", s); } catch (Exception ex) { _services.Logger.Warn("保存快照失败: " + ex.Message); }
                return s;
            });
            _services.Session.Snapshot = snapshot;
            await PopulateAsync(snapshot);
        }
        catch (Exception ex)
        {
            _services.Logger.Error("探测失败", ex);
            Status = "探测失败：" + ex.Message;
        }
        finally { IsBusy = false; RefreshAllCanRun(); }
    }

    private async Task PopulateAsync(EnvironmentSnapshot s)
    {
        BuildMachineInfo(s);

        var options = s.Volumes.Where(v => !v.IsSystem)
            .Select(v => new OptionItem($"{v.DriveLetter} {v.Label}（{v.SizeGb:F2} GB，剩 {v.FreeGb:F2} GB）", v.DriveLetter)).ToList();
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
        if (!s.IsWindows11) AddWarning("当前不是 Windows 11，部分设置项可能不适用。");
        if (s.IsMdmEnrolled || s.IsDomainJoined) AddWarning("检测到此电脑受组织管理（MDM/域），系统级改动不会列出或只给步骤。");
        if (s.DataDrive == null) AddWarning("未检测到数据盘：路径与缓存迁移不会出现；单盘可先看“分区”分类。");
        var sys = s.Volumes.FirstOrDefault(v => v.IsSystem);
        if (sys != null && sys.FreeGb < 40) AddWarning($"系统盘剩余仅 {sys.FreeGb:F2} GB，建议优先执行“磁盘与路径”里的迁移项。");

        await RebuildPlanAsync(s, answers);
    }

    private void AddWarning(string text)
    {
        if (_dismissedWarnings.Contains(text)) return;
        Warnings.Add(new WarningViewModel(text, key => _dismissedWarnings.Add(key)));
    }

    /// <summary>硬件信息单独成栏。GB 一律按 GiB 折算并保留两位，另附字节原值，避免"四舍五入后看起来不对"。</summary>
    private void BuildMachineInfo(EnvironmentSnapshot s)
    {
        MachineInfo.Clear();
        MachineInfo.Add(new MachineRow("系统", $"{s.OsCaption}（内部版本 {s.Build}）", $"安装于 {s.InstallDate:yyyy-MM-dd HH:mm}"));
        MachineInfo.Add(new MachineRow("机型", $"{s.Manufacturer} {s.Model}".Trim(), s.IsLaptop ? "笔记本" : "台式机"));
        if (s.Cpu.Name.Length > 0 || s.Cpu.Cores > 0)
            MachineInfo.Add(new MachineRow("处理器", s.Cpu.Name.Length > 0 ? s.Cpu.Name : "未知",
                s.Cpu.Cores > 0 ? $"{s.Cpu.Cores} 核 {s.Cpu.LogicalProcessors} 线程" : string.Empty));
        MachineInfo.Add(new MachineRow("内存", Gb(s.RamBytes), Bytes(s.RamBytes)));

        foreach (var d in s.Disks)
            MachineInfo.Add(new MachineRow($"磁盘 {d.Number}", $"{d.Model}（{d.InterfaceType}） {Gb(d.SizeBytes)}", Bytes(d.SizeBytes)));

        foreach (var v in s.Volumes)
        {
            var tag = v.IsSystem ? "系统盘" : string.Equals(v.DriveLetter, s.DataDrive, StringComparison.OrdinalIgnoreCase) ? "数据盘" : "数据卷";
            var label = string.IsNullOrWhiteSpace(v.Label) ? string.Empty : $"{v.Label} · ";
            MachineInfo.Add(new MachineRow($"{v.DriveLetter} {tag}",
                $"{label}总 {Gb(v.SizeBytes)} · 已用 {Gb(v.UsedBytes)} · 剩余 {Gb(v.FreeBytes)}",
                $"总 {Bytes(v.SizeBytes)}，剩余 {Bytes(v.FreeBytes)}"));
        }

        var managed = new List<string>();
        if (s.IsMdmEnrolled) managed.Add("已注册 MDM");
        if (s.IsDomainJoined) managed.Add("已加入域");
        if (s.ProxyEnabled) managed.Add("系统代理已开启");
        MachineInfo.Add(new MachineRow("管理与网络", managed.Count == 0 ? "未受管理，未开系统代理" : string.Join("；", managed),
            s.IsMdmEnrolled || s.IsDomainJoined ? "系统级改动会被跳过或只给步骤" : string.Empty));

        var od = !s.OneDrive.Installed ? "未安装" : s.OneDrive.SignedIn ? "已登录" : "已安装未登录";
        MachineInfo.Add(new MachineRow("OneDrive", od, s.OneDrive.DesktopProtected ? "桌面已被备份接管" : string.Empty));
        MachineInfo.Add(new MachineRow("已识别的开发工具", s.Tools.Count(x => x.Installed) + " 个",
            string.Join("、", s.Tools.Where(x => x.Installed).Select(x => x.Name))));
        MachineInfo.Add(new MachineRow("探测时间", s.TakenAt.ToString("yyyy-MM-dd HH:mm:ss"), string.Empty));
    }

    private static string Gb(long bytes) => (bytes / 1073741824d).ToString("F2", CultureInfo.InvariantCulture) + " GB";
    private static string Bytes(long bytes) => bytes.ToString("N0", CultureInfo.InvariantCulture) + " 字节";

    /// <summary>
    /// 重新 Detect 全部条目并就地刷新列表，保留已有执行结果。
    /// Detect 会读注册表、枚举目录，几十项加起来是秒级，必须在后台线程跑，否则窗口整段无响应。
    /// </summary>
    private async Task RebuildPlanAsync(EnvironmentSnapshot s, Answers answers)
    {
        IsBusy = true;
        Status = "正在检查各项当前状态…";
        RefreshAllCanRun();
        try
        {
            var plan = await Task.Run(() =>
            {
                var p = _services.Planner.Build(_services.Catalog, s, answers);
                try { _services.Runner.SavePlan(p, s); } catch (Exception ex) { _services.Logger.Warn("保存列表失败: " + ex.Message); }
                return p;
            });
            _services.Session.Plan = plan;
            ApplyPlan(plan);
            // 探测做完后“探测完成”四个字没有信息量，改成时间，和旁边的“重新探测”对得上
            Status = "探测于 " + s.TakenAt.ToString("HH:mm:ss");
        }
        catch (Exception ex)
        {
            _services.Logger.Error("生成列表失败", ex);
            Status = "生成列表失败：" + ex.Message;
        }
        finally { IsBusy = false; RefreshAllCanRun(); }
    }

    private void ApplyPlan(Plan plan)
    {
        var results = _services.Runner.Results.ToDictionary(r => r.TaskId, StringComparer.OrdinalIgnoreCase);
        var selectedKey = SelectedCategory?.Key;
        var order = plan.Items.Select(i => i.Module).Distinct().ToList();
        if (Categories.Count == 0 || !Categories[0].IsMachineInfo)
            Categories.Insert(0, new CategoryViewModel(CategoryViewModel.MachineKey) { Subtitle = "硬件与系统信息" });
        var existing = Categories.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);

        // 分类集合尽量就地更新，避免列表闪动
        foreach (var key in order)
        {
            if (!existing.TryGetValue(key, out var cat))
            {
                cat = new CategoryViewModel(key);
                Categories.Insert(Math.Min(order.IndexOf(key) + 1, Categories.Count), cat);
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
        foreach (var stale in Categories.Where(c => !c.IsMachineInfo && !order.Contains(c.Key)).ToList()) Categories.Remove(stale);

        // 首次落到第一个有可执行项的分类：工具的核心动作是执行条目，首屏不该停在只读的硬件信息上
        SelectedCategory = Categories.FirstOrDefault(c => c.Key == selectedKey)
                           ?? Categories.FirstOrDefault(c => c.IsTaskList && c.Items.Any(i => i.IsPlanned))
                           ?? Categories.FirstOrDefault();
        var runnable = plan.Items.Count(i => i.State == PlanState.Planned);
        Summary = $"共 {plan.Items.Count} 项 · 可执行 {runnable} 项 · 已满足 {plan.SkippedCount} 项";
        RefreshAllCanRun();
        Refreshed?.Invoke();
    }

    partial void OnDataDriveChanged(string value)
    {
        if (_suppressDriveChange || _services.Session.Snapshot == null) return;
        var answers = (_services.Session.Answers ?? Answers.Default(_services.Session.Snapshot)) with { DataDrive = string.IsNullOrEmpty(value) ? null : value };
        _services.Session.Answers = answers;
        _ = RebuildPlanAsync(_services.Session.Snapshot, answers);
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
            if (MessageBox.Show(text, "开荒", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
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
                    await PopulateAsync(s);
                }
                else
                {
                    await RebuildPlanAsync(s, a);
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
            "开荒", MessageBoxButton.OK, MessageBoxImage.Information);
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

/// <summary>“这台电脑”栏位的一行：标签、主值、补充说明（精确字节数等）。</summary>
public sealed class MachineRow
{
    public MachineRow(string label, string value, string? detail)
    {
        Label = label; Value = value; Detail = detail ?? string.Empty;
        HasDetail = Detail.Length > 0;
    }
    public string Label { get; }
    public string Value { get; }
    public string Detail { get; }
    public bool HasDetail { get; }
}

public sealed class OptionItem
{
    public OptionItem(string label, object value) { Label = label; Value = value; }
    public string Label { get; }
    public object Value { get; }
}
