using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NewPcSetup.Core.Infrastructure;

namespace NewPcSetup.App;

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
        "tools" => "基础工具",
        "browser" => "浏览器",
        "im" => "通讯",
        "office" => "办公 / 笔记",
        "media" => "媒体",
        "dev" => "开发",
        "game" => "游戏",
        "runtime" => "运行库",
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
        "tools" => "解压、截图、搜索这类天天用得上的小工具，挑顺手的装。",
        "browser" => "系统自带 Edge 已经够用，习惯别家的再装。",
        "im" => "按你实际在用的选，用不到的不必装。",
        "office" => "写文档和记笔记，按习惯二选一即可。",
        "media" => "系统自带播放器解码有限，常看片或录屏再考虑。",
        "dev" => "只写代码才需要，不开发可以整段跳过。",
        "game" => "平台本体装到 Applications，游戏库单独放数据盘的 Games。",
        "runtime" => "老程序常缺的运行库，遇到报错再装也来得及。",
        _ => string.Empty,
    };

    public static string InstallerHint(SoftwareEntry e) => e.Installer switch
    {
        "electron_user" => "安装器默认装到用户目录，通常不支持自定义路径",
        "msix" => "通过微软商店安装，位置由系统管理",
        _ => e.CustomPath ? "安装时选择“自定义安装”并粘贴推荐路径" : "该安装器不支持自定义路径",
    };
}
