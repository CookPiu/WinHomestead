using System;
using System.Collections.Generic;
using NewPcSetup.Core.Abstractions;
using NewPcSetup.Core.Models;
using static NewPcSetup.Tasks.RegistryEntry;

namespace NewPcSetup.Tasks;

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

    private static bool Win10Like(EnvironmentSnapshot s, Answers a) => a.UiStyle == UiStyle.Win10Like;
    private static bool Promo(EnvironmentSnapshot s, Answers a) => a.DisablePromotions;

    private static RegistryValueTask Reg(string id, string module, string name, string desc, RiskFlags risk, int order,
        Func<EnvironmentSnapshot, Answers, bool>? applicable, params RegistryEntry[] entries)
        => new(new TaskMetadata(id, module, name, desc, risk, NoDeps, order), entries, applicable);

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

            // ---- 开发缓存 ----
            new EnvVarTask("pip", "Python / pip", new[] { new EnvVarTask.Var("PIP_CACHE_DIR", @"DevCache\pip-cache") }, 130),
            new EnvVarTask("uv", "uv", new[] { new EnvVarTask.Var("UV_CACHE_DIR", @"DevCache\uv-cache") }, 131),
            new EnvVarTask("npm", "npm", new[] { new EnvVarTask.Var("npm_config_cache", @"DevCache\npm-cache") }, 132),
            new EnvVarTask("pnpm", "pnpm", new[] { new EnvVarTask.Var("PNPM_HOME", @"DevCache\pnpm") }, 133),
            new EnvVarTask("yarn", "Yarn", new[] { new EnvVarTask.Var("YARN_CACHE_FOLDER", @"DevCache\yarn-cache") }, 134),
            new EnvVarTask("gradle", "Gradle", new[] { new EnvVarTask.Var("GRADLE_USER_HOME", @"DevCache\gradle") }, 135),
            new EnvVarTask("nuget", "NuGet", new[] { new EnvVarTask.Var("NUGET_PACKAGES", @"DevCache\nuget-packages") }, 136),
            new EnvVarTask("cargo", "Cargo / Rust", new[] { new EnvVarTask.Var("CARGO_HOME", @"DevCache\cargo"), new EnvVarTask.Var("RUSTUP_HOME", @"DevCache\rustup") }, 137),
            new EnvVarTask("go", "Go", new[] { new EnvVarTask.Var("GOPATH", @"DevCache\go"), new EnvVarTask.Var("GOMODCACHE", @"DevCache\go\pkg\mod") }, 138),
            new EnvVarTask("pub", "Flutter / Dart", new[] { new EnvVarTask.Var("PUB_CACHE", @"DevCache\pub-cache") }, 139),
            new EnvVarTask("hf", "Hugging Face", new[] { new EnvVarTask.Var("HF_HOME", @"Models\huggingface") }, 140),
            new EnvVarTask("ollama", "Ollama", new[] { new EnvVarTask.Var("OLLAMA_MODELS", @"Models\ollama") }, 141),

            // ---- 界面：默认执行 ----
            Reg("ui.file_ext", "ui", "显示文件扩展名", "资源管理器显示所有文件的扩展名，避免双扩展名伪装。", UiRisk, 200, null,
                Dword(Adv, "HideFileExt", 0)),
            Reg("ui.hidden_files", "ui", "显示隐藏文件", "资源管理器显示隐藏的文件和文件夹（不含受保护的系统文件）。", UiRisk, 201, null,
                Dword(Adv, "Hidden", 1)),
            Reg("ui.launch_to_pc", "ui", "资源管理器打开到“此电脑”", "新开资源管理器窗口时直接显示磁盘列表，而不是主页。", UiRisk, 202, null,
                Dword(Adv, "LaunchTo", 1)),
            Reg("ui.desktop_this_pc", "ui", "桌面显示“此电脑”图标", "在桌面上显示“此电脑”。", UiRisk, 203, null,
                Dword(DesktopIcons, "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", 0)),
            Reg("ui.end_task", "ui", "任务栏右键“结束任务”", "在任务栏应用图标的右键菜单中增加“结束任务”，无需打开任务管理器。", UiRisk, 204, null,
                Dword(Adv + @"\TaskbarDeveloperSettings", "TaskbarEndTask", 1)),
            Reg("ui.search_icon", "ui", "任务栏搜索改为图标", "把任务栏的搜索框缩成一个图标，节省空间。", UiRisk, 205, null,
                Dword(Search, "SearchboxTaskbarMode", 1)),
            Reg("ui.lang_hotkey", "ui", "关闭 Alt+Shift 切换输入语言", "禁用 Alt+Shift 与 Ctrl+Shift 切换输入语言/键盘布局的快捷键，避免误触。保留 Win+空格。注销后生效。", SignOutRisk, 206, null,
                Str(@"Keyboard Layout\Toggle", "Language Hotkey", "3"), Str(@"Keyboard Layout\Toggle", "Hotkey", "3"), Str(@"Keyboard Layout\Toggle", "Layout Hotkey", "3")),
            Reg("ui.mouse_accel", "ui", "关闭鼠标加速", "关闭“提高指针精确度”，指针移动距离与手部移动成正比。注销后生效。", SignOutRisk, 207, null,
                Str(@"Control Panel\Mouse", "MouseSpeed", "0"), Str(@"Control Panel\Mouse", "MouseThreshold1", "0"), Str(@"Control Panel\Mouse", "MouseThreshold2", "0")),
            Reg("ui.sticky_keys", "ui", "关闭粘滞键/切换键快捷键", "连按五次 Shift 不再弹出粘滞键，按住 Num Lock 不再弹出切换键。注销后生效。", SignOutRisk, 208, null,
                Str(@"Control Panel\Accessibility\StickyKeys", "Flags", "506"), Str(@"Control Panel\Accessibility\ToggleKeys", "Flags", "58")),

            // ---- 界面：问卷决定 ----
            Reg("ui.taskbar_left", "ui", "任务栏左对齐", "任务栏图标靠左排列（Windows 10 风格）。", UiRisk, 220, Win10Like,
                Dword(Adv, "TaskbarAl", 0)),
            Reg("ui.taskview_widgets", "ui", "隐藏任务视图与小组件按钮", "从任务栏移除“任务视图”和“小组件”按钮，功能仍可通过快捷键使用。", UiRisk, 221, Win10Like,
                Dword(Adv, "ShowTaskViewButton", 0), Dword(Adv, "TaskbarDa", 0)),
            Reg("ui.classic_menu", "ui", "恢复经典右键菜单", "右键直接显示完整菜单，不再需要点“显示更多选项”。", UiRisk, 222, Win10Like,
                Str(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "", "")),
            Reg("ui.start_layout", "ui", "开始菜单显示更多固定项", "开始菜单缩小“推荐”区域，给固定的应用更多空间。", UiRisk, 223, Win10Like,
                Dword(Adv, "Start_Layout", 1)),
            Reg("ui.dark_mode", "ui", "深色模式", "系统与应用均使用深色主题。", RiskFlags.Reversible, 224, (s, a) => a.DarkMode,
                Dword(Personalize, "AppsUseLightTheme", 0), Dword(Personalize, "SystemUsesLightTheme", 0)),

            // ---- 中文输入法（值含义来自社区整理：Default Mode 0 中文/1 英文；English Switch Key 0 Shift/1 Ctrl/2 关闭；
            //      EnableChineseEnglishPunctuationSwitch 1 开/0 关，对应 Ctrl+. 标点切换）----
            Reg("ime.default_english", "ime", "微软拼音默认英文模式", "微软拼音启动时处于英文输入状态，需要中文时再切换。适合以英文/代码输入为主的用户。", SignOutRisk, 300,
                (s, a) => a.ImeMode == ImeMode.EnglishDefault,
                Dword(ImeChs, "Default Mode", 1)),
            Reg("ime.punct_hotkey", "ime", "关闭微软拼音 Ctrl+. 标点切换", "Ctrl+. 在多数编辑器里是常用快捷键，关闭输入法对它的占用，避免误切中英文标点。", SignOutRisk, 301,
                (s, a) => a.ImeMode == ImeMode.EnglishDefault,
                Dword(ImeChs, "EnableChineseEnglishPunctuationSwitch", 0)),
            Reg("ime.shift_switch", "ime", "关闭微软拼音 Shift 键切换中英文", "单击 Shift 不再切换中英文，只用 Win+空格 或 Ctrl+空格 切换，避免误触。", SignOutRisk, 302,
                (s, a) => !a.KeepShiftSwitch,
                Dword(ImeChs, "English Switch Key", 2)),

            // ---- 可选：去推送（默认不勾选，由问卷开启） ----
            Reg("promo.lockscreen", "promo", "关闭锁屏聚焦与趣味信息", "锁屏不再轮播 Windows 聚焦图片与提示。", RiskFlags.Reversible, 400, Promo,
                Dword(Cdm, "RotatingLockScreenEnabled", 0), Dword(Cdm, "RotatingLockScreenOverlayEnabled", 0)),
            Reg("promo.suggestions", "promo", "关闭系统建议与推荐安装", "关闭开始菜单/设置页的建议内容、提示通知，以及自动静默安装推荐应用。", UiRisk, 401, Promo,
                Dword(Cdm, "SubscribedContent-338388Enabled", 0), Dword(Cdm, "SubscribedContent-338389Enabled", 0),
                Dword(Cdm, "SubscribedContent-338393Enabled", 0), Dword(Cdm, "SubscribedContent-353694Enabled", 0),
                Dword(Cdm, "SubscribedContent-353696Enabled", 0), Dword(Cdm, "SilentInstalledAppsEnabled", 0),
                Dword(Cdm, "SystemPaneSuggestionsEnabled", 0), Dword(Cdm, "SoftLandingEnabled", 0)),
            Reg("promo.start_recommend", "promo", "关闭开始菜单推荐区", "开始菜单不再显示“推荐的项目”。", UiRisk, 402, Promo,
                Dword(Adv, "Start_IrisRecommendations", 0)),
            Reg("promo.bing_search", "promo", "关闭任务栏搜索联网结果", "任务栏搜索只搜本机，不再显示必应结果和搜索高亮。", UiRisk, 403, Promo,
                Dword(Search, "BingSearchEnabled", 0), Dword(@"Software\Microsoft\Windows\CurrentVersion\SearchSettings", "IsDynamicSearchBoxEnabled", 0)),
            Reg("promo.ad_id", "promo", "关闭广告 ID 与定制体验", "应用不再通过广告 ID 投放个性化广告，系统不再基于诊断数据推送定制内容。", RiskFlags.Reversible, 404, Promo,
                Dword(@"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0),
                Dword(@"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0)),

            // ---- GPU ----
            new HagsTask(),

            // ---- C 盘治理 ----
            new TempCleanupTask(),
            new HibernateOffTask(),
            Reg("storage.sense", "storage", "开启存储感知", "开启存储感知，并把运行频率设为“磁盘空间不足时”，自动清理临时文件与回收站。", RiskFlags.Reversible, 502, null,
                Dword(StoragePolicy, "01", 1), Dword(StoragePolicy, "2048", 0)),
        };
        return list;
    }
}
