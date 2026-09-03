using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewPcSetup.Core.Models;

namespace NewPcSetup.App.ViewModels;

public sealed class StorageRow
{
    public StorageRow(LargeItem item, bool isLaptop)
    {
        Path = item.Path;
        Size = $"{item.SizeGb:F1} GB";
        (Label, Tag, Advice) = item.Category switch
        {
            "hiberfil" => ("休眠文件", isLaptop ? "保留" : "可关闭", isLaptop ? "笔记本建议保留，用于休眠与快速启动。" : "台式机可在方案中勾选“关闭休眠”释放空间。"),
            "pagefile" => ("页面文件", "保留", "建议保留在系统盘；迁移需重启且易误配，不建议手动改动。"),
            "temp" => ("用户临时目录", "可清理", "执行路径迁移后新程序将使用数据盘；旧目录中 7 天前的文件由方案中的清理项删除。"),
            "phonelink" => ("手机连接缓存", "只提示", "由“手机连接”应用同步的手机内容，可在该应用设置中清理或断开设备。"),
            "appdata" => AppDataAdvice(item.Path),
            _ => ("大目录", "查看", "查看后决定是否迁移或清理。"),
        };
    }

    /// <summary>按目录名给 AppData 大户贴标签；未知目录一律“查看”，不建议删除。</summary>
    private static (string, string, string) AppDataAdvice(string path)
    {
        var name = System.IO.Path.GetFileName(path.TrimEnd('\\'));
        switch (name.ToLowerInvariant())
        {
            case "docker": return ("Docker Desktop 数据", "可迁移", "在 Docker Desktop 的 Settings → Resources → Advanced 中把 Disk image location 改到数据盘 VMs\\docker。");
            case "jetbrains": return ("JetBrains 缓存与索引", "可迁移", "在 IDE 的 Help → Edit Custom Properties 中设置 idea.system.path 与 idea.config.path 到数据盘 DevCache\\jetbrains。");
            case "nvidia": return ("NVIDIA 着色器缓存", "可清理", "在 NVIDIA 控制面板 → 管理 3D 设置 → 着色器缓存大小 中调小或清空，游戏会自动重建。");
            case "pnpm": return ("pnpm 存储", "可迁移", "方案中的 pnpm 缓存迁移项会把 PNPM_HOME 指向数据盘；旧目录待新安装完成后可删除。");
            case "pip": return ("pip 缓存", "可清理", "方案中的 pip 缓存迁移项生效后，旧目录可整体删除。");
            case "npm-cache": return ("npm 缓存", "可清理", "方案中的 npm 缓存迁移项生效后，旧目录可整体删除。");
            case ".cache": return ("通用缓存（Hugging Face 等）", "可迁移", "方案中的 Hugging Face 迁移项会把 HF_HOME 指向数据盘；其余子目录按工具自行处理。");
            case ".gradle": return ("Gradle 缓存", "可迁移", "方案中的 Gradle 迁移项生效后，旧目录可整体删除。");
            case ".nuget": return ("NuGet 包缓存", "可迁移", "方案中的 NuGet 迁移项生效后，旧目录可整体删除。");
            case ".cargo": return ("Cargo 缓存", "可迁移", "方案中的 Cargo 迁移项生效后，旧目录可整体删除。");
            case ".ollama": return ("Ollama 模型", "可迁移", "方案中的 Ollama 迁移项会把 OLLAMA_MODELS 指向数据盘，模型需重新拉取或手动移动。");
            default: return (name, "查看", "该目录超过 1 GB。打开后按内容决定是否迁移或清理；不确定的不要删。");
        }
    }

    public string Label { get; }
    public string Tag { get; }
    public string Path { get; }
    public string Size { get; }
    public string Advice { get; }
}

public sealed partial class StorageViewModel : ObservableObject
{
    private readonly Action _back;

    public StorageViewModel(AppServices services, Action back)
    {
        _back = back;
        var s = services.Session.Snapshot;
        if (s == null) { Summary = "尚未完成探测。"; return; }
        var sys = s.Volumes.FirstOrDefault(v => v.IsSystem);
        Summary = sys == null ? string.Empty : $"{sys.DriveLetter} 共 {sys.SizeGb:F0} GB，剩余 {sys.FreeGb:F0} GB";
        foreach (var i in s.LargeItems.OrderByDescending(i => i.SizeBytes)) Rows.Add(new StorageRow(i, s.IsLaptop));
    }

    public string Summary { get; }
    public ObservableCollection<StorageRow> Rows { get; } = new();

    [RelayCommand]
    private void OpenStorageSettings() => Process.Start(new ProcessStartInfo("ms-settings:storagesense") { UseShellExecute = true });

    [RelayCommand]
    private void Back() => _back();
}
