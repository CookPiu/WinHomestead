using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinHomestead.Core.Infrastructure;

namespace WinHomestead.App;

public sealed class SoftwareEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("category")] public string Category { get; set; } = string.Empty;
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    [JsonPropertyName("install_dir")] public string InstallDir { get; set; } = string.Empty;
    /// <summary>msi | nsis | inno | electron_user | msix | other</summary>
    [JsonPropertyName("installer")] public string Installer { get; set; } = "other";
    [JsonPropertyName("custom_path")] public bool CustomPath { get; set; } = true;
    [JsonPropertyName("detect")] public string Detect { get; set; } = string.Empty;
    [JsonPropertyName("usages")] public List<string> Usages { get; set; } = new();
    [JsonPropertyName("note")] public string? Note { get; set; }
}

public sealed class SoftwareCatalog
{
    public IReadOnlyList<SoftwareEntry> Entries { get; }
    private SoftwareCatalog(IReadOnlyList<SoftwareEntry> entries) { Entries = entries; }

    public static SoftwareCatalog LoadEmbedded()
    {
        using var s = typeof(SoftwareCatalog).Assembly.GetManifestResourceStream("software.json")
                      ?? throw new InvalidOperationException("缺少嵌入资源 software.json");
        using var r = new StreamReader(s);
        var list = JsonSerializer.Deserialize<List<SoftwareEntry>>(r.ReadToEnd(), JsonDefaults.Options) ?? new List<SoftwareEntry>();
        return new SoftwareCatalog(list);
    }

    public static string CategoryLabel(string c) => c switch
    {
        "tools" => L.S("基础工具", "Essentials"),
        "browser" => L.S("浏览器", "Browsers"),
        "im" => L.S("通讯", "Messaging"),
        "office" => L.S("办公 / 笔记", "Office and notes"),
        "media" => L.S("媒体", "Media"),
        "dev" => L.S("开发", "Development"),
        "game" => L.S("游戏", "Games"),
        "runtime" => L.S("运行库", "Runtimes"),
        _ => c,
    };


    /// <summary>分类对应的安装子目录，避免所有软件平铺在 Applications 下。</summary>
    public static string CategoryDir(string c) => c switch
    {
        "tools" => "Tools",
        "browser" => "Browsers",
        "im" => "Chat",
        "office" => "Office",
        "media" => "Media",
        "dev" => "Dev",
        "game" => "Games",
        "runtime" => "Runtimes",
        _ => "Others",
    };

    /// <summary>分类下的一句话说明，帮着挑，不是要求全装。</summary>
    public static string CategoryHint(string c) => c switch
    {
        "tools" => L.S("解压、截图、搜索这类天天用得上的小工具，挑顺手的装。",
            "Archivers, screenshot tools, instant search \u2014 the small things you reach for daily. Pick what suits you."),
        "browser" => L.S("系统自带 Edge 已经够用，习惯别家的再装。",
            "The bundled Edge is perfectly usable; install another only if you are used to one."),
        "im" => L.S("按你实际在用的选，用不到的不必装。", "Install the ones you actually use and skip the rest."),
        "office" => L.S("写文档和记笔记，按习惯二选一即可。", "For documents and notes \u2014 pick whichever you are used to."),
        "media" => L.S("系统自带播放器解码有限，常看片或录屏再考虑。",
            "The built-in player decodes a limited set of formats; worth adding if you watch or record a lot."),
        "dev" => L.S("只写代码才需要，不开发可以整段跳过。", "Only needed if you write code \u2014 skip the whole section otherwise."),
        "game" => L.S("平台本体装到 Applications，游戏库单独放数据盘的 Games。",
            "Install the client under Applications and point its game library at Games on the data drive."),
        "runtime" => L.S("老程序常缺的运行库，遇到报错再装也来得及。",
            "Runtimes that older programs tend to miss. Installing them when something complains is soon enough."),
        _ => string.Empty,
    };

    public static string InstallerHint(SoftwareEntry e) => e.Installer switch
    {
        "electron_user" => L.S("安装器默认装到用户目录，通常不支持自定义路径",
            "This installer targets your user profile and usually offers no custom path"),
        "msix" => L.S("通过微软商店安装，位置由系统管理", "Installed from the Store; Windows decides where it goes"),
        _ => e.CustomPath
            ? L.S("安装时选择“自定义安装”并粘贴推荐路径", "Choose a custom install during setup and paste the suggested path")
            : L.S("该安装器不支持自定义路径", "This installer doesn't support a custom path"),
    };
}
