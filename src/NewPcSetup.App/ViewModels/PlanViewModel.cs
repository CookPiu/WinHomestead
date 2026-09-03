using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewPcSetup.Core.Engine;
using NewPcSetup.Core.Models;

namespace NewPcSetup.App.ViewModels;

public sealed partial class PlanItemViewModel : ObservableObject
{
    private readonly Action<string, bool> _onToggle;
    private bool _suppress;

    public PlanItemViewModel(PlanItem item, Action<string, bool> onToggle)
    {
        _onToggle = onToggle;
        Update(item);
    }

    public string TaskId { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string CurrentValue { get; private set; } = string.Empty;
    public string TargetValue { get; private set; } = string.Empty;
    public string StateLabel { get; private set; } = string.Empty;
    public string RiskLabel { get; private set; } = string.Empty;
    public bool IsPlanned { get; private set; }
    public bool ShowValues => IsPlanned;

    [ObservableProperty] private bool _isChecked;

    partial void OnIsCheckedChanged(bool value)
    {
        if (!_suppress) _onToggle(TaskId, value);
    }

    public void Update(PlanItem item)
    {
        TaskId = item.TaskId;
        DisplayName = item.DisplayName;
        Description = item.Description;
        CurrentValue = item.CurrentValue ?? string.Empty;
        TargetValue = item.TargetValue ?? string.Empty;
        IsPlanned = item.State == PlanState.Planned;
        StateLabel = item.State switch
        {
            PlanState.Skipped => "已满足，跳过",
            PlanState.NotApplicable => "不适用" + (item.Reason != null && item.Reason.Contains(":") ? "：" + item.Reason.Substring(item.Reason.IndexOf(':') + 1).Trim() : string.Empty),
            _ => string.Empty,
        };
        var risks = new List<string>();
        if ((item.Risk & RiskFlags.Reversible) != 0) risks.Add("可撤销");
        if ((item.Risk & RiskFlags.NeedsExplorerRestart) != 0) risks.Add("重启资源管理器");
        if ((item.Risk & RiskFlags.NeedsSignOut) != 0) risks.Add("注销后生效");
        if ((item.Risk & RiskFlags.NeedsReboot) != 0) risks.Add("需重启");
        if ((item.Risk & RiskFlags.AdminOnly) != 0) risks.Add("系统级");
        if ((item.Risk & RiskFlags.PromptOnly) != 0) risks.Add("只提示");
        if ((item.Risk & (RiskFlags.Reversible | RiskFlags.PromptOnly)) == 0) risks.Add("不可撤销");
        RiskLabel = string.Join(" · ", risks);
        _suppress = true;
        IsChecked = item.Checked;
        _suppress = false;
        OnPropertyChanged(string.Empty);
    }
}

public sealed class PlanGroupViewModel
{
    public PlanGroupViewModel(string module, IEnumerable<PlanItemViewModel> items)
    {
        Module = module;
        Title = module switch
        {
            "disk" => "分区",
            "path" => "磁盘与路径",
            "gpu" => "显卡",
            "env" => "开发缓存迁移",
            "ui" => "界面与交互",
            "ime" => "中文输入法",
            "promo" => "去推送（可选）",
            "storage" => "C 盘治理",
            _ => module,
        };
        Items = new ObservableCollection<PlanItemViewModel>(items);
    }
    public string Module { get; }
    public string Title { get; }
    public ObservableCollection<PlanItemViewModel> Items { get; }
}

public sealed partial class PlanViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _next;
    private readonly Action _back;
    private readonly Dictionary<string, PlanItemViewModel> _byId = new(StringComparer.OrdinalIgnoreCase);

    public PlanViewModel(AppServices services, Action next, Action back)
    {
        _services = services; _next = next; _back = back;
        var plan = services.Session.Plan!;
        var groups = plan.Items.GroupBy(i => i.Module)
            .Select(g => new PlanGroupViewModel(g.Key, g.Select(i => new PlanItemViewModel(i, Toggle))))
            .ToList();
        Groups = new ObservableCollection<PlanGroupViewModel>(groups);
        foreach (var g in Groups) foreach (var i in g.Items) _byId[i.TaskId] = i;
        Refresh();
    }

    public ObservableCollection<PlanGroupViewModel> Groups { get; }

    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private bool _canExecute;

    private void Toggle(string taskId, bool value)
    {
        var plan = PlanEditor.SetChecked(_services.Session.Plan!, taskId, value);
        _services.Session.Plan = plan;
        foreach (var item in plan.Items)
            if (_byId.TryGetValue(item.TaskId, out var vm) && vm.IsChecked != item.Checked) vm.Update(item);
        Refresh();
    }

    private void Refresh()
    {
        var plan = _services.Session.Plan!;
        var notApplicable = plan.Items.Count(i => i.State == PlanState.NotApplicable);
        var signOut = plan.Items.Count(i => i.State == PlanState.Planned && i.Checked && (i.Risk & RiskFlags.NeedsSignOut) != 0);
        Summary = $"将修改 {plan.PlannedCount} 项 · 已满足跳过 {plan.SkippedCount} 项 · 不适用 {notApplicable} 项" +
                  (signOut > 0 ? $" · {signOut} 项注销后生效" : string.Empty) +
                  (plan.RebootCount > 0 ? $" · {plan.RebootCount} 项需重启" : string.Empty);
        CanExecute = plan.PlannedCount > 0;
        ExecuteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanExecute))]
    private void Execute() => _next();

    [RelayCommand]
    private void Back() => _back();
}
