using System;
using System.Collections.Generic;
using WinHomestead.Core.Abstractions;
using WinHomestead.Core.Models;
using static WinHomestead.Tasks.RegistryEntry;

namespace WinHomestead.Tasks;

/// <summary>全部任务的静态注册表。不做反射扫描，便于审计与裁剪。</summary>
public static class TaskCatalog
{
    private const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string Search = @"Software\Microsoft\Windows\CurrentVersion\Search";
    private const string Cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DesktopIcons = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
    private const string ImeChs = @"Software\Microsoft\InputMethod\Settings\CHS";
    private const string StoragePolicy = @"Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy";

    private static readonly RiskFlags UiRisk = RiskFlags.Reversible | RiskFlags.NeedsExplorerRestart;
    private static readonly RiskFlags SignOutRisk = RiskFlags.Reversible | RiskFlags.NeedsSignOut;
    private static readonly string[] NoDeps = Array.Empty<string>();

    /// <summary>风格类、去推送类条目不再由问卷开关，全部列出，但默认不标“推荐”。</summary>
    private static bool Optional(EnvironmentSnapshot s, Answers a) => false;

    /// <summary>开关型的值：1 开启 / 0 关闭，未设置视为默认开启。</summary>
    private static RegistryEntry OnOff(RegistryEntry e, string label) => e.As(label, "未设置（默认开启）", (1, "开启"), (0, "关闭"));

    private static RegistryValueTask Reg(string id, string module, string name, string desc, RiskFlags risk, int order,
        Func<EnvironmentSnapshot, Answers, bool>? recommended, params RegistryEntry[] entries)
        => new(new TaskMetadata(id, module, name, desc, risk, NoDeps, order), entries, null, recommended);

    public static IReadOnlyList<ITask> All { get; } = Build();

    private static IReadOnlyList<ITask> Build()
    {
        var list = new List<ITask>
        {
            // ---- 磁盘 ----
            new DiskSuggestTask(),
            new ShrinkAndCreateTask(),

            // ---- 路径 ----
            new PathSkeletonTask(),
            new KnownFolderTask(KnownFolder.Documents, "Documents", "文档", 110),
            new KnownFolderTask(KnownFolder.Downloads, "Downloads", "下载", 111),
            new KnownFolderTask(KnownFolder.Pictures, "Pictures", "图片", 112),
            new KnownFolderTask(KnownFolder.Videos, "Videos", "视频", 113),
            new KnownFolderTask(KnownFolder.Music, "Music", "音乐", 114),
            new KnownFolderTask(KnownFolder.Desktop, "Desktop", "桌面", 115),
            new QuickAccessPinTask(),
            new UserTempTask(),
            new MachineTempTask(),

            // ---- 开发缓存：只做"装之前先预设"，不迁移使用中的工具 ----
            new DevCachePresetTask(),

            // ---- 界面：默认执行 ----
            Reg("ui.file_ext", "ui", "显示文件扩展名", "资源管理器显示所有文件的扩展名，避免双扩展名伪装。", UiRisk, 200, null,
                Dword(Adv, "HideFileExt", 0).As("文件扩展名", "未设置（默认隐藏）", (0, "显示"), (1, "隐藏"))),
            Reg("ui.hidden_files", "ui", "显示隐藏文件", "资源管理器显示隐藏的文件和文件夹（不含受保护的系统文件）。", UiRisk, 201, null,
                Dword(Adv, "Hidden", 1).As("隐藏文件", "未设置（默认不显示）", (1, "显示"), (2, "不显示"), (0, "不显示"))),
            Reg("ui.launch_to_pc", "ui", "资源管理器打开到“此电脑”", "新开资源管理器窗口时直接显示磁盘列表，而不是主页。", UiRisk, 202, null,
                Dword(Adv, "LaunchTo", 1).As("新窗口打开到", "未设置（默认主页）", (1, "此电脑"), (2, "主页"), (3, "下载"), (4, "OneDrive"))),
            Reg("ui.desktop_this_pc", "ui", "桌面显示“此电脑”图标", "在桌面上显示“此电脑”。", UiRisk, 203, null,
                Dword(DesktopIcons, "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 0).As("桌面“此电脑”图标", "未设置（默认隐藏）", (0, "显示"), (1, "隐藏"))),
            Reg("ui.end_task", "ui", "任务栏右键“结束任务”", "在任务栏应用图标的右键菜单中增加“结束任务”，无需打开任务管理器。", UiRisk, 204, null,
                Dword(Adv + @"\TaskbarDeveloperSettings", "TaskbarEndTask", 1).As("右键“结束任务”", "未设置（默认关闭）", (1, "开启"), (0, "关闭"))),
            Reg("ui.search_icon", "ui", "任务栏搜索改为图标", "把任务栏的搜索框缩成一个图标，节省空间。", UiRisk, 205, null,
                Dword(Search, "SearchboxTaskbarMode", 1).As("任务栏搜索样式", "未设置（默认搜索框）", (0, "隐藏"), (1, "仅图标"), (2, "搜索框"), (3, "搜索框+标签"))),
            Reg("ui.lang_hotkey", "ui", "关闭 Alt+Shift 切换输入语言", "禁用 Alt+Shift 与 Ctrl+Shift 切换输入语言/键盘布局的快捷键，避免误触。保留 Win+空格。注销后生效。", SignOutRisk, 206, null,
                Str(@"Keyboard Layout\Toggle", "Language Hotkey", "3").As("切换输入语言", "未设置（默认 Alt+Shift）", ("1", "Alt+Shift"), ("2", "Ctrl+Shift"), ("3", "关闭"), ("4", "` 键")),
                Str(@"Keyboard Layout\Toggle", "Hotkey", "3").As("切换输入语言", "未设置（默认 Alt+Shift）", ("1", "Alt+Shift"), ("2", "Ctrl+Shift"), ("3", "关闭"), ("4", "` 键")),
                Str(@"Keyboard Layout\Toggle", "Layout Hotkey", "3").As("切换键盘布局", "未设置（默认 Ctrl+Shift）", ("1", "Alt+Shift"), ("2", "Ctrl+Shift"), ("3", "关闭"), ("4", "` 键"))),
            Reg("ui.mouse_accel", "ui", "关闭鼠标加速", "关闭“提高指针精确度”，指针移动距离与手部移动成正比。注销后生效。", SignOutRisk, 207, null,
                Str(@"Control Panel\Mouse", "MouseSpeed", "0").As("指针加速", "未设置（默认开启）", ("0", "关闭"), ("1", "开启"), ("2", "开启")),
                Str(@"Control Panel\Mouse", "MouseThreshold1", "0"), Str(@"Control Panel\Mouse", "MouseThreshold2", "0")),
            Reg("ui.sticky_keys", "ui", "关闭粘滞键/切换键快捷键", "连按五次 Shift 不再弹出粘滞键，按住 Num Lock 不再弹出切换键。注销后生效。", SignOutRisk, 208, null,
                Str(@"Control Panel\Accessibility\StickyKeys", "Flags", "506").As("粘滞键快捷键", "未设置（默认开启）", ("506", "关闭"), ("510", "开启")),
                Str(@"Control Panel\Accessibility\ToggleKeys", "Flags", "58").As("切换键快捷键", "未设置（默认开启）", ("58", "关闭"), ("62", "开启"))),

            // ---- 界面：风格偏好（可选，按需执行） ----
            Reg("ui.taskbar_left", "ui", "任务栏左对齐", "任务栏图标靠左排列（Windows 10 风格）。", UiRisk, 220, Optional,
                Dword(Adv, "TaskbarAl", 0).As("任务栏对齐", "未设置（默认居中）", (0, "左对齐"), (1, "居中"))),
            Reg("ui.taskview_widgets", "ui", "隐藏任务视图与小组件按钮", "从任务栏移除“任务视图”和“小组件”按钮，功能仍可通过快捷键使用。", UiRisk, 221, Optional,
                Dword(Adv, "ShowTaskViewButton", 0).As("任务视图按钮", "未设置（默认显示）", (0, "隐藏"), (1, "显示")),
                Dword(Adv, "TaskbarDa", 0).As("小组件按钮", "未设置（默认显示）", (0, "隐藏"), (1, "显示"))),
            Reg("ui.classic_menu", "ui", "恢复经典右键菜单", "右键直接显示完整菜单，不再需要点“显示更多选项”。", UiRisk, 222, Optional,
                Str(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "", "").As("右键菜单", "Windows 11 精简菜单", ("", "经典完整菜单"))),
            Reg("ui.start_layout", "ui", "开始菜单显示更多固定项", "开始菜单缩小“推荐”区域，给固定的应用更多空间。", UiRisk, 223, Optional,
                Dword(Adv, "Start_Layout", 1).As("开始菜单布局", "未设置（默认）", (0, "默认"), (1, "更多固定项"), (2, "更多推荐"))),
            Reg("ui.dark_mode", "ui", "深色模式", "系统与应用均使用深色主题。", RiskFlags.Reversible, 224, Optional,
                Dword(Personalize, "AppsUseLightTheme", 0).As("应用主题", "未设置（默认浅色）", (0, "深色"), (1, "浅色")),
                Dword(Personalize, "SystemUsesLightTheme", 0).As("系统主题", "未设置（默认浅色）", (0, "深色"), (1, "浅色"))),

            // ---- 中文输入法（值含义来自社区整理：Default Mode 0 中文/1 英文；English Switch Key 0 Shift/1 Ctrl/2 关闭；
            //      EnableChineseEnglishPunctuationSwitch 1 开/0 关，对应 Ctrl+. 标点切换）----
            Reg("ime.default_english", "ime", "微软拼音默认英文模式", "微软拼音启动时处于英文输入状态，需要中文时再切换。适合以英文/代码输入为主的用户。", SignOutRisk, 300, Optional,
                Dword(ImeChs, "Default Mode", 1).As("启动时输入模式", "未设置（默认中文）", (0, "中文"), (1, "英文"))),
            Reg("ime.punct_hotkey", "ime", "关闭微软拼音 Ctrl+. 标点切换", "Ctrl+. 在多数编辑器里是常用快捷键，关闭输入法对它的占用，避免误切中英文标点。", SignOutRisk, 301, Optional,
                Dword(ImeChs, "EnableChineseEnglishPunctuationSwitch", 0).As("Ctrl+. 切换标点", "未设置（默认开启）", (1, "开启"), (0, "关闭"))),
            Reg("ime.shift_switch", "ime", "关闭微软拼音 Shift 键切换中英文", "单击 Shift 不再切换中英文，只用 Win+空格 或 Ctrl+空格 切换，避免误触。", SignOutRisk, 302, Optional,
                Dword(ImeChs, "English Switch Key", 2).As("中英文切换键", "未设置（默认 Shift）", (0, "Shift"), (1, "Ctrl"), (2, "关闭"))),

            // ---- 可选：去推送（不标推荐，按需执行） ----
            Reg("promo.lockscreen", "promo", "关闭锁屏聚焦与趣味信息", "锁屏不再轮播 Windows 聚焦图片与提示。", RiskFlags.Reversible, 400, Optional,
                Dword(Cdm, "RotatingLockScreenEnabled", 0).As("锁屏聚焦图片", "未设置（默认开启）", (1, "开启"), (0, "关闭")),
                Dword(Cdm, "RotatingLockScreenOverlayEnabled", 0).As("锁屏趣味信息", "未设置（默认开启）", (1, "开启"), (0, "关闭"))),
            Reg("promo.suggestions", "promo", "关闭系统建议与推荐安装", "关闭开始菜单/设置页的建议内容、提示通知，以及自动静默安装推荐应用。", UiRisk, 401, Optional,
                OnOff(Dword(Cdm, "SubscribedContent-338388Enabled", 0), "建议与推荐"), OnOff(Dword(Cdm, "SubscribedContent-338389Enabled", 0), "建议与推荐"),
                OnOff(Dword(Cdm, "SubscribedContent-338393Enabled", 0), "建议与推荐"), OnOff(Dword(Cdm, "SubscribedContent-353694Enabled", 0), "建议与推荐"),
                OnOff(Dword(Cdm, "SubscribedContent-353696Enabled", 0), "建议与推荐"), OnOff(Dword(Cdm, "SilentInstalledAppsEnabled", 0), "静默推荐安装"),
                OnOff(Dword(Cdm, "SystemPaneSuggestionsEnabled", 0), "建议与推荐"), OnOff(Dword(Cdm, "SoftLandingEnabled", 0), "提示与技巧")),
            Reg("promo.start_recommend", "promo", "关闭开始菜单推荐区", "开始菜单不再显示“推荐的项目”。", UiRisk, 402, Optional,
                Dword(Adv, "Start_IrisRecommendations", 0).As("开始菜单推荐区", "未设置（默认显示）", (1, "显示"), (0, "隐藏"))),
            Reg("promo.bing_search", "promo", "关闭任务栏搜索联网结果", "任务栏搜索只搜本机，不再显示必应结果和搜索高亮。", UiRisk, 403, Optional,
                OnOff(Dword(Search, "BingSearchEnabled", 0), "必应联网结果"),
                OnOff(Dword(@"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", 0), "搜索高亮")),
            Reg("promo.ad_id", "promo", "关闭广告 ID 与定制体验", "应用不再通过广告 ID 投放个性化广告，系统不再基于诊断数据推送定制内容。", RiskFlags.Reversible, 404, Optional,
                OnOff(Dword(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0), "广告 ID"),
                OnOff(Dword(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0), "定制体验")),

            // ---- GPU ----
            new HagsTask(),

            // ---- 空间清理：三条可执行项，不做大文件扫描 ----
            new TempCleanupTask(),
            new HibernateOffTask(),
            Reg("storage.sense", "storage", "开启存储感知", "开启存储感知，并把运行频率设为“磁盘空间不足时”，自动清理临时文件与回收站。", RiskFlags.Reversible, 502, null,
                Dword(StoragePolicy, "01", 1).As("存储感知", "未设置（默认关闭）", (1, "开启"), (0, "关闭")),
                Dword(StoragePolicy, "2048", 0).As("运行频率", "未设置（默认磁盘空间不足时）", (0, "磁盘空间不足时"), (1, "每天"), (7, "每周"), (30, "每月"))),
        };
        return list;
    }
}
