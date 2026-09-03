using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    [RelayCommand]
    private void Next()
    {
        var s = _services.Session.Snapshot!;
        var answers = new Answers(Usage, string.IsNullOrEmpty(DataDrive) ? null : DataDrive, false, UiStyle, DarkMode, ImeMode, KeepShiftSwitch, DisablePromotions);
        _services.Session.Answers = answers;
        _services.Session.Plan = _services.Planner.Build(_services.Catalog, s, answers);
        _next();
    }

    [RelayCommand]
    private void UseDefaults()
    {
        var a = Answers.Default(_services.Session.Snapshot!);
        Usage = a.Usage; UiStyle = a.UiStyle; DarkMode = a.DarkMode; ImeMode = a.ImeMode; KeepShiftSwitch = a.KeepShiftSwitch; DisablePromotions = a.DisablePromotions;
    }

    [RelayCommand]
    private void Back() => _back();
}
