using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewPcSetup.Core.Engine;
using NewPcSetup.Core.Models;

namespace NewPcSetup.App.ViewModels;

public sealed class OptionItem
{
    public OptionItem(string label, object value) { Label = label; Value = value; }
    public string Label { get; }
    public object Value { get; }
}

public sealed partial class QuestionnaireViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Action _next;
    private readonly Action _back;

    public QuestionnaireViewModel(AppServices services, Action next, Action back)
    {
        _services = services; _next = next; _back = back;
        var s = services.Session.Snapshot!;
        var a = services.Session.Answers ?? Answers.Default(s);

        UsageOptions = new List<OptionItem>
        {
            new("日常家用", Usage.Home), new("办公", Usage.Office), new("开发", Usage.Dev), new("游戏", Usage.Gaming),
        };
        DataDriveOptions = s.Volumes.Where(v => !v.IsSystem).Select(v => new OptionItem($"{v.DriveLetter} {v.Label}（{v.SizeGb:F0} GB，剩 {v.FreeGb:F0} GB）", v.DriveLetter)).ToList();
        if (DataDriveOptions.Count == 0) DataDriveOptions.Add(new OptionItem("无数据盘（不迁移路径）", string.Empty));

        // 单盘无数据分区：Q2 改问“是否按建议分区”（UC-05）
        var advice = DiskAdvisor.Advise(s);
        OfferPartition = s.DataDrive == null && advice.NeedsPartition;
        ShowDriveList = !OfferPartition;
        _newLetter = OfferPartition ? DiskAdvisor.NextFreeDriveLetter(s.Volumes.Select(v => v.DriveLetter)) : 'D';
        PartitionText = OfferPartition
            ? $"这台电脑只有一个分区。建议把 C 压缩到约 {advice.SuggestedSystemGb:F0} GB，其余约 {advice.SuggestedDataGb:F0} GB 新建为数据盘 {_newLetter}:。" +
              (advice.Automatable ? "满足一键执行条件，勾选后会在方案里出现“压缩 C 盘并新建数据分区”，执行前不会删除任何数据。"
                                  : $"当前不满足自动执行条件（{advice.Reason}），只会在报告里给出手动步骤。")
            : string.Empty;
        _createPartition = a.CreatePartition && advice.Automatable;
        UiStyleOptions = new List<OptionItem> { new("Windows 11 默认", UiStyle.Win11Default), new("偏 Windows 10（左对齐、经典右键、隐藏小组件）", UiStyle.Win10Like) };
        ImeOptions = new List<OptionItem> { new("默认中文", ImeMode.ChineseDefault), new("默认英文（以英文/代码输入为主）", ImeMode.EnglishDefault) };

        _usage = a.Usage;
        _dataDrive = a.DataDrive ?? (DataDriveOptions.Count > 0 ? DataDriveOptions[0].Value as string ?? string.Empty : string.Empty);
        _uiStyle = a.UiStyle;
        _darkMode = a.DarkMode;
        _imeMode = a.ImeMode;
        _keepShiftSwitch = a.KeepShiftSwitch;
        _disablePromotions = a.DisablePromotions;
    }

    public List<OptionItem> UsageOptions { get; }
    public List<OptionItem> DataDriveOptions { get; }
    public List<OptionItem> UiStyleOptions { get; }
    public List<OptionItem> ImeOptions { get; }

    [ObservableProperty] private Usage _usage;
    [ObservableProperty] private string _dataDrive;
    [ObservableProperty] private UiStyle _uiStyle;
    [ObservableProperty] private bool _darkMode;
    [ObservableProperty] private ImeMode _imeMode;
    [ObservableProperty] private bool _keepShiftSwitch;
    [ObservableProperty] private bool _disablePromotions;
    [ObservableProperty] private bool _createPartition;
    private readonly char _newLetter;
    public bool OfferPartition { get; }
    public bool ShowDriveList { get; }
    public string PartitionText { get; }

    [RelayCommand]
    private void Next()
    {
        var s = _services.Session.Snapshot!;
        var createPartition = OfferPartition && CreatePartition;
        var dataDrive = createPartition ? _newLetter + ":" : (string.IsNullOrEmpty(DataDrive) ? null : DataDrive);
        var answers = new Answers(Usage, dataDrive, createPartition, UiStyle, DarkMode, ImeMode, KeepShiftSwitch, DisablePromotions);
        _services.Session.Answers = answers;
        _services.Session.Plan = _services.Planner.Build(_services.Catalog, s, answers);
        _next();
    }

    [RelayCommand]
    private void UseDefaults()
    {
        var a = Answers.Default(_services.Session.Snapshot!);
        Usage = a.Usage; UiStyle = a.UiStyle; DarkMode = a.DarkMode; ImeMode = a.ImeMode; KeepShiftSwitch = a.KeepShiftSwitch; DisablePromotions = a.DisablePromotions; CreatePartition = false;
    }

    [RelayCommand]
    private void Back() => _back();
}
