using System;
using System.Collections.Generic;
using WinHomestead.Core.Abstractions;
using WinHomestead.Core.Infrastructure;
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
    private const string CabinetState = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CabinetState";

    private static readonly RiskFlags UiRisk = RiskFlags.Reversible | RiskFlags.NeedsExplorerRestart;
    private static readonly RiskFlags SignOutRisk = RiskFlags.Reversible | RiskFlags.NeedsSignOut;
    private static readonly string[] NoDeps = Array.Empty<string>();

    /// <summary>风格类、去推送类条目不再由问卷开关，全部列出，但默认不标“推荐”。</summary>
    private static bool Optional(EnvironmentSnapshot s, Answers a) => false;

    private static string S(string zh, string en) => L.S(zh, en);

    /// <summary>“未设置”一类的兜底说明，两种语言各一份。</summary>
    private static string Unset(string zh, string en) => L.S("未设置（" + zh + "）", "not set (" + en + ")");

    // 用属性而不是 static readonly 字段：字段在类型初始化时就定死了，BuildFresh 换语言后取不到新值
    private static string On => L.S("开启", "on");
    private static string Off => L.S("关闭", "off");
    private static string Shown => L.S("显示", "shown");
    private static string Hidden => L.S("隐藏", "hidden");

    /// <summary>开关型的值：1 开启 / 0 关闭，未设置视为默认开启。</summary>
    private static RegistryEntry OnOff(RegistryEntry e, string label) => e.As(label, Unset("默认开启", "on by default"), (1, On), (0, Off));

    private static RegistryValueTask Reg(string id, string module, string name, string desc, RiskFlags risk, int order,
        Func<EnvironmentSnapshot, Answers, bool>? recommended, params RegistryEntry[] entries)
        => new(new TaskMetadata(id, module, name, desc, risk, NoDeps, order), entries, null, recommended);

    /// <summary>
    /// 文案在构造时按当时的语言取好，所以这份目录跟着进程语言走。
    /// 要在运行中换语言，得用 <see cref="BuildFresh"/> 重新生成一份。
    /// </summary>
    public static IReadOnlyList<ITask> All { get; } = Build();

    /// <summary>重新构建一份目录，文案按调用时的语言取。</summary>
    public static IReadOnlyList<ITask> BuildFresh() => Build();

    private static IReadOnlyList<ITask> Build()
    {
        var list = new List<ITask>
        {
            // ---- 磁盘 ----
            new DiskSuggestTask(),
            new ShrinkAndCreateTask(),
            new DevDriveSuggestTask(),

            // ---- 路径 ----
            new PathSkeletonTask(),
            new KnownFolderTask(KnownFolder.Documents, "Documents", S("文档", "Documents"), 110),
            new KnownFolderTask(KnownFolder.Downloads, "Downloads", S("下载", "Downloads"), 111),
            new KnownFolderTask(KnownFolder.Pictures, "Pictures", S("图片", "Pictures"), 112),
            new KnownFolderTask(KnownFolder.Videos, "Videos", S("视频", "Videos"), 113),
            new KnownFolderTask(KnownFolder.Music, "Music", S("音乐", "Music"), 114),
            new KnownFolderTask(KnownFolder.Desktop, "Desktop", S("桌面", "Desktop"), 115),
            new QuickAccessPinTask(),
            new UserTempTask(),
            new MachineTempTask(),

            // ---- 开发缓存：只做"装之前先预设"，不迁移使用中的工具 ----
            new DevCachePresetTask(),

            // ---- 界面：默认执行 ----
            Reg("ui.file_ext", "ui", S("显示文件扩展名", "Show file extensions"),
                S("资源管理器显示所有文件的扩展名，避免双扩展名伪装。",
                  "File Explorer shows the extension of every file, so a double extension can't hide what something really is."),
                UiRisk, 200, null,
                Dword(Adv, "HideFileExt", 0).As(S("文件扩展名", "File extensions"), Unset("默认隐藏", "hidden by default"), (0, Shown), (1, Hidden))),
            Reg("ui.hidden_files", "ui", S("显示隐藏文件", "Show hidden files"),
                S("资源管理器显示隐藏的文件和文件夹（不含受保护的系统文件）。",
                  "File Explorer shows hidden files and folders (protected system files stay hidden)."),
                UiRisk, 201, null,
                Dword(Adv, "Hidden", 1).As(S("隐藏文件", "Hidden files"), Unset("默认不显示", "hidden by default"), (1, Shown), (2, Hidden), (0, Hidden))),
            Reg("ui.launch_to_pc", "ui", S("资源管理器打开到“此电脑”", "Open File Explorer to This PC"),
                S("新开资源管理器窗口时直接显示磁盘列表，而不是主页。",
                  "New Explorer windows land on the drive list instead of Home."),
                UiRisk, 202, null,
                Dword(Adv, "LaunchTo", 1).As(S("新窗口打开到", "New windows open to"), Unset("默认主页", "Home by default"),
                    (1, S("此电脑", "This PC")), (2, S("主页", "Home")), (3, S("下载", "Downloads")), (4, "OneDrive"))),
            Reg("ui.desktop_this_pc", "ui", S("桌面显示“此电脑”图标", "Show This PC on the desktop"),
                S("在桌面上显示“此电脑”。", "Puts the This PC icon back on the desktop."),
                UiRisk, 203, null,
                Dword(DesktopIcons, "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 0)
                    .As(S("桌面“此电脑”图标", "This PC desktop icon"), Unset("默认隐藏", "hidden by default"), (0, Shown), (1, Hidden))),
            Reg("ui.end_task", "ui", S("任务栏右键“结束任务”", "Add End task to the taskbar menu"),
                S("在任务栏应用图标的右键菜单中增加“结束任务”，无需打开任务管理器。",
                  "Adds End task to the right-click menu of taskbar icons, so you don't need Task Manager to kill a hung app."),
                UiRisk, 204, null,
                Dword(Adv + @"\TaskbarDeveloperSettings", "TaskbarEndTask", 1)
                    .As(S("右键“结束任务”", "End task in context menu"), Unset("默认关闭", "off by default"), (1, On), (0, Off))),
            Reg("ui.search_icon", "ui", S("任务栏搜索改为图标", "Shrink taskbar search to an icon"),
                S("把任务栏的搜索框缩成一个图标，节省空间。", "Collapses the taskbar search box into a single icon to save room."),
                UiRisk, 205, null,
                Dword(Search, "SearchboxTaskbarMode", 1).As(S("任务栏搜索样式", "Taskbar search style"), Unset("默认搜索框", "search box by default"),
                    (0, Hidden), (1, S("仅图标", "icon only")), (2, S("搜索框", "search box")), (3, S("搜索框+标签", "search box with label")))),
            Reg("ui.lang_hotkey", "ui", S("关闭 Alt+Shift 切换输入语言", "Disable the Alt+Shift language hotkey"),
                S("禁用 Alt+Shift 与 Ctrl+Shift 切换输入语言/键盘布局的快捷键，避免误触。保留 Win+空格。注销后生效。",
                  "Disables the Alt+Shift and Ctrl+Shift shortcuts that switch input language and keyboard layout — a common source of accidental switches. Win+Space still works. Takes effect after signing out."),
                SignOutRisk, 206, null,
                Str(@"Keyboard Layout\Toggle", "Language Hotkey", "3").As(S("切换输入语言", "Switch input language"), Unset("默认 Alt+Shift", "Alt+Shift by default"),
                    ("1", "Alt+Shift"), ("2", "Ctrl+Shift"), ("3", Off), ("4", S("` 键", "the ` key"))),
                Str(@"Keyboard Layout\Toggle", "Hotkey", "3").As(S("切换输入语言", "Switch input language"), Unset("默认 Alt+Shift", "Alt+Shift by default"),
                    ("1", "Alt+Shift"), ("2", "Ctrl+Shift"), ("3", Off), ("4", S("` 键", "the ` key"))),
                Str(@"Keyboard Layout\Toggle", "Layout Hotkey", "3").As(S("切换键盘布局", "Switch keyboard layout"), Unset("默认 Ctrl+Shift", "Ctrl+Shift by default"),
                    ("1", "Alt+Shift"), ("2", "Ctrl+Shift"), ("3", Off), ("4", S("` 键", "the ` key")))),
            Reg("ui.mouse_accel", "ui", S("关闭鼠标加速", "Turn off mouse acceleration"),
                S("关闭“提高指针精确度”，指针移动距离与手部移动成正比。注销后生效。",
                  "Turns off Enhance pointer precision, so pointer travel matches hand travel. Takes effect after signing out."),
                SignOutRisk, 207, null,
                Str(@"Control Panel\Mouse", "MouseSpeed", "0").As(S("指针加速", "Pointer acceleration"), Unset("默认开启", "on by default"),
                    ("0", Off), ("1", On), ("2", On)),
                Str(@"Control Panel\Mouse", "MouseThreshold1", "0"), Str(@"Control Panel\Mouse", "MouseThreshold2", "0")),
            Reg("ui.sticky_keys", "ui", S("关闭粘滞键/切换键快捷键", "Disable Sticky Keys and Toggle Keys shortcuts"),
                S("连按五次 Shift 不再弹出粘滞键，按住 Num Lock 不再弹出切换键。注销后生效。",
                  "Five taps of Shift no longer prompts for Sticky Keys, and holding Num Lock no longer prompts for Toggle Keys. Takes effect after signing out."),
                SignOutRisk, 208, null,
                Str(@"Control Panel\Accessibility\StickyKeys", "Flags", "506")
                    .As(S("粘滞键快捷键", "Sticky Keys shortcut"), Unset("默认开启", "on by default"), ("506", Off), ("510", On)),
                Str(@"Control Panel\Accessibility\ToggleKeys", "Flags", "58")
                    .As(S("切换键快捷键", "Toggle Keys shortcut"), Unset("默认开启", "on by default"), ("58", Off), ("62", On))),

            // ---- 界面：风格偏好（可选，按需执行） ----
            Reg("ui.taskbar_left", "ui", S("任务栏左对齐", "Align the taskbar left"),
                S("任务栏图标靠左排列（Windows 10 风格）。", "Taskbar icons line up on the left, Windows 10 style."),
                UiRisk, 220, Optional,
                Dword(Adv, "TaskbarAl", 0).As(S("任务栏对齐", "Taskbar alignment"), Unset("默认居中", "centered by default"),
                    (0, S("左对齐", "left")), (1, S("居中", "center")))),
            Reg("ui.taskview_widgets", "ui", S("隐藏任务视图与小组件按钮", "Hide Task view and Widgets buttons"),
                S("从任务栏移除“任务视图”和“小组件”按钮，功能仍可通过快捷键使用。",
                  "Removes the Task view and Widgets buttons from the taskbar. Both features still work via their shortcuts."),
                UiRisk, 221, Optional,
                Dword(Adv, "ShowTaskViewButton", 0).As(S("任务视图按钮", "Task view button"), Unset("默认显示", "shown by default"), (0, Hidden), (1, Shown)),
                Dword(Adv, "TaskbarDa", 0).As(S("小组件按钮", "Widgets button"), Unset("默认显示", "shown by default"), (0, Hidden), (1, Shown))),
            Reg("ui.classic_menu", "ui", S("恢复经典右键菜单", "Restore the classic context menu"),
                S("右键直接显示完整菜单，不再需要点“显示更多选项”。",
                  "Right-click shows the full menu directly, with no Show more options detour."),
                UiRisk, 222, Optional,
                Str(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "", "")
                    .As(S("右键菜单", "Context menu"), S("Windows 11 精简菜单", "Windows 11 compact menu"), ("", S("经典完整菜单", "classic full menu")))),
            Reg("ui.start_layout", "ui", S("开始菜单显示更多固定项", "More pins in the Start menu"),
                S("开始菜单缩小“推荐”区域，给固定的应用更多空间。",
                  "Shrinks the Recommended section so pinned apps get more room."),
                UiRisk, 223, Optional,
                Dword(Adv, "Start_Layout", 1).As(S("开始菜单布局", "Start menu layout"), Unset("默认", "default"),
                    (0, S("默认", "default")), (1, S("更多固定项", "more pins")), (2, S("更多推荐", "more recommendations")))),
            Reg("ui.dark_mode", "ui", S("深色模式", "Dark mode"),
                S("系统与应用均使用深色主题。", "Uses the dark theme for both Windows and apps."),
                RiskFlags.Reversible, 224, Optional,
                Dword(Personalize, "AppsUseLightTheme", 0).As(S("应用主题", "App theme"), Unset("默认浅色", "light by default"),
                    (0, S("深色", "dark")), (1, S("浅色", "light"))),
                Dword(Personalize, "SystemUsesLightTheme", 0).As(S("系统主题", "System theme"), Unset("默认浅色", "light by default"),
                    (0, S("深色", "dark")), (1, S("浅色", "light")))),
            Reg("ui.full_path", "ui", S("标题栏显示完整路径", "Full path in the title bar"),
                S("资源管理器标题栏显示当前目录的完整路径，而不是只有文件夹名。",
                  "The Explorer title bar shows the full path of the current folder instead of just its name."),
                UiRisk, 225, Optional,
                Dword(CabinetState, "FullPath", 1).As(S("标题栏路径", "Title bar path"), Unset("默认只显示文件夹名", "folder name only by default"),
                    (1, S("完整路径", "full path")), (0, S("只显示文件夹名", "folder name only")))),
            Reg("ui.taskbar_never_combine", "ui", S("任务栏按钮从不合并", "Never combine taskbar buttons"),
                S("同一程序的多个窗口在任务栏上各占一格并显示标题，不再叠成一个图标。",
                  "Each window gets its own labelled taskbar button instead of stacking under one icon."),
                UiRisk, 226, Optional,
                Dword(Adv, "TaskbarGlomLevel", 2).As(S("任务栏按钮", "Taskbar buttons"), Unset("默认始终合并", "always combine by default"),
                    (0, S("始终合并", "always combine")), (1, S("任务栏已满时合并", "combine when full")), (2, S("从不合并", "never combine"))),
                Dword(Adv, "MMTaskbarGlomLevel", 2).As(S("副屏任务栏按钮", "Secondary taskbar buttons"), Unset("默认始终合并", "always combine by default"),
                    (0, S("始终合并", "always combine")), (1, S("任务栏已满时合并", "combine when full")), (2, S("从不合并", "never combine")))),
            Reg("ui.clock_seconds", "ui", S("任务栏时间显示秒", "Show seconds on the taskbar clock"),
                S("系统托盘的时钟精确到秒。微软标注此项会略微增加功耗。",
                  "The tray clock ticks in seconds. Microsoft notes this slightly increases power consumption."),
                UiRisk, 227, Optional,
                Dword(Adv, "ShowSecondsInSystemClock", 1).As(S("时钟精度", "Clock precision"), Unset("默认不显示秒", "no seconds by default"),
                    (1, S("显示秒", "with seconds")), (0, S("不显示秒", "without seconds")))),
            Reg("ui.quick_access_clean", "ui", S("快速访问不显示最近项目", "Keep recent items out of Quick Access"),
                S("资源管理器主页不再列出最近用过的文件和常用文件夹，共用电脑时少一份痕迹。",
                  "Explorer Home stops listing recent files and frequent folders — one less trail on a shared machine."),
                UiRisk, 228, Optional,
                Dword(Adv, "ShowRecent", 0).As(S("最近用过的文件", "Recent files"), Unset("默认显示", "shown by default"), (1, Shown), (0, Hidden)),
                Dword(Adv, "ShowFrequent", 0).As(S("常用文件夹", "Frequent folders"), Unset("默认显示", "shown by default"), (1, Shown), (0, Hidden))),
            Reg("ui.no_aero_shake", "ui", S("关闭抖动最小化", "Disable Aero Shake"),
                S("拖着窗口晃两下不再把其它窗口全部最小化，避免误触。",
                  "Shaking a window no longer minimizes everything else — easy to trigger by accident."),
                UiRisk, 229, Optional,
                Dword(Adv, "DisallowShaking", 1).As(S("抖动最小化", "Aero Shake"), Unset("默认开启", "on by default"), (1, Off), (0, On))),

            // ---- 中文输入法（值含义来自社区整理：Default Mode 0 中文/1 英文；English Switch Key 0 Shift/1 Ctrl/2 关闭；
            //      EnableChineseEnglishPunctuationSwitch 1 开/0 关，对应 Ctrl+. 标点切换）----
            Reg("ime.default_english", "ime", S("微软拼音默认英文模式", "Start Microsoft Pinyin in English mode"),
                S("微软拼音启动时处于英文输入状态，需要中文时再切换。适合以英文/代码输入为主的用户。",
                  "Microsoft Pinyin starts in English, switch to Chinese when you need it. Suits people who mostly type English or code."),
                SignOutRisk, 300, Optional,
                Dword(ImeChs, "Default Mode", 1).As(S("启动时输入模式", "Mode at startup"), Unset("默认中文", "Chinese by default"),
                    (0, S("中文", "Chinese")), (1, S("英文", "English")))),
            Reg("ime.punct_hotkey", "ime", S("关闭微软拼音 Ctrl+. 标点切换", "Free up Ctrl+. in Microsoft Pinyin"),
                S("Ctrl+. 在多数编辑器里是常用快捷键，关闭输入法对它的占用，避免误切中英文标点。",
                  "Ctrl+. is a common editor shortcut. Releasing it from the IME stops punctuation flipping unexpectedly."),
                SignOutRisk, 301, Optional,
                Dword(ImeChs, "EnableChineseEnglishPunctuationSwitch", 0)
                    .As(S("Ctrl+. 切换标点", "Ctrl+. punctuation toggle"), Unset("默认开启", "on by default"), (1, On), (0, Off))),
            Reg("ime.shift_switch", "ime", S("关闭微软拼音 Shift 键切换中英文", "Disable single-Shift language switching"),
                S("单击 Shift 不再切换中英文，只用 Win+空格 或 Ctrl+空格 切换，避免误触。",
                  "A single Shift tap no longer flips between Chinese and English; use Win+Space or Ctrl+Space instead."),
                SignOutRisk, 302, Optional,
                Dword(ImeChs, "English Switch Key", 2).As(S("中英文切换键", "Language switch key"), Unset("默认 Shift", "Shift by default"),
                    (0, "Shift"), (1, "Ctrl"), (2, Off))),

            // ---- 可选：去推送（不标推荐，按需执行） ----
            Reg("promo.lockscreen", "promo", S("关闭锁屏聚焦与趣味信息", "Turn off lock screen Spotlight and fun facts"),
                S("锁屏不再轮播 Windows 聚焦图片与提示。", "The lock screen stops cycling Windows Spotlight images and tips."),
                RiskFlags.Reversible, 400, Optional,
                Dword(Cdm, "RotatingLockScreenEnabled", 0).As(S("锁屏聚焦图片", "Spotlight images"), Unset("默认开启", "on by default"), (1, On), (0, Off)),
                Dword(Cdm, "RotatingLockScreenOverlayEnabled", 0).As(S("锁屏趣味信息", "Fun facts and tips"), Unset("默认开启", "on by default"), (1, On), (0, Off))),
            Reg("promo.suggestions", "promo", S("关闭系统建议与推荐安装", "Turn off suggestions and silent app installs"),
                S("关闭开始菜单/设置页的建议内容、提示通知，以及自动静默安装推荐应用。",
                  "Turns off suggested content in Start and Settings, tip notifications, and the silent installation of recommended apps."),
                UiRisk, 401, Optional,
                OnOff(Dword(Cdm, "SubscribedContent-338388Enabled", 0), S("建议与推荐", "Suggestions")),
                OnOff(Dword(Cdm, "SubscribedContent-338389Enabled", 0), S("建议与推荐", "Suggestions")),
                OnOff(Dword(Cdm, "SubscribedContent-338393Enabled", 0), S("建议与推荐", "Suggestions")),
                OnOff(Dword(Cdm, "SubscribedContent-353694Enabled", 0), S("建议与推荐", "Suggestions")),
                OnOff(Dword(Cdm, "SubscribedContent-353696Enabled", 0), S("建议与推荐", "Suggestions")),
                OnOff(Dword(Cdm, "SilentInstalledAppsEnabled", 0), S("静默推荐安装", "Silent app installs")),
                OnOff(Dword(Cdm, "SystemPaneSuggestionsEnabled", 0), S("建议与推荐", "Suggestions")),
                OnOff(Dword(Cdm, "SoftLandingEnabled", 0), S("提示与技巧", "Tips and tricks"))),
            Reg("promo.start_recommend", "promo", S("关闭开始菜单推荐区", "Turn off the Start menu Recommended section"),
                S("开始菜单不再显示“推荐的项目”。", "The Start menu stops showing Recommended items."),
                UiRisk, 402, Optional,
                Dword(Adv, "Start_IrisRecommendations", 0).As(S("开始菜单推荐区", "Recommended section"), Unset("默认显示", "shown by default"), (1, Shown), (0, Hidden))),
            Reg("promo.bing_search", "promo", S("关闭任务栏搜索联网结果", "Turn off web results in taskbar search"),
                S("任务栏搜索只搜本机，不再显示必应结果和搜索高亮。",
                  "Taskbar search stays local: no Bing results, no search highlights."),
                UiRisk, 403, Optional,
                OnOff(Dword(Search, "BingSearchEnabled", 0), S("必应联网结果", "Bing web results")),
                OnOff(Dword(@"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", 0), S("搜索高亮", "Search highlights"))),
            Reg("promo.sync_notifications", "promo", S("关闭资源管理器里的同步提供程序通知", "Turn off sync provider notifications in Explorer"),
                S("资源管理器不再插播 OneDrive 之类的推广横幅。这一项只关广告位，不影响 OneDrive 本身的同步。",
                  "Explorer stops showing OneDrive-style promo banners. This only closes the ad slot; OneDrive sync itself is unaffected."),
                UiRisk, 405, Optional,
                Dword(Adv, "ShowSyncProviderNotifications", 0)
                    .As(S("同步提供程序通知", "Sync provider notifications"), Unset("默认显示", "shown by default"), (1, Shown), (0, Hidden))),
            Reg("promo.ad_id", "promo", S("关闭广告 ID 与定制体验", "Turn off the advertising ID and tailored experiences"),
                S("应用不再通过广告 ID 投放个性化广告，系统不再基于诊断数据推送定制内容。",
                  "Apps can no longer use the advertising ID to personalize ads, and Windows stops tailoring content from diagnostic data."),
                RiskFlags.Reversible, 404, Optional,
                OnOff(Dword(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0), S("广告 ID", "Advertising ID")),
                OnOff(Dword(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0),
                    S("定制体验", "Tailored experiences"))),

            // ---- 显示与电源 ----
            new RefreshRateTask(),
            new FastStartupOffTask(),
            new AcTimeoutsTask(),

            // ---- GPU ----
            new HagsTask(),

            // ---- 空间清理：三条可执行项，不做大文件扫描 ----
            new TempCleanupTask(),
            new HibernateOffTask(),
            Reg("storage.sense", "storage", S("开启存储感知", "Turn on Storage Sense"),
                S("开启存储感知，并把运行频率设为“磁盘空间不足时”，自动清理临时文件与回收站。",
                  "Turns on Storage Sense and sets it to run during low free disk space, clearing temporary files and the Recycle Bin."),
                RiskFlags.Reversible, 502, null,
                Dword(StoragePolicy, "01", 1).As(S("存储感知", "Storage Sense"), Unset("默认关闭", "off by default"), (1, On), (0, Off)),
                Dword(StoragePolicy, "2048", 0).As(S("运行频率", "How often it runs"), Unset("默认磁盘空间不足时", "during low free disk space by default"),
                    (0, S("磁盘空间不足时", "during low free disk space")), (1, S("每天", "every day")),
                    (7, S("每周", "every week")), (30, S("每月", "every month")))),
        };
        return list;
    }
}
