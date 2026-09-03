namespace NewPcSetup.Core.Models;

/// <summary>
/// 全局设置。界面不再做问卷：风格、输入法、去推送等都以独立条目出现在列表里，由用户逐项执行。
/// 这里只保留影响“哪些条目出现、目标路径是什么”的两个值。
/// </summary>
public sealed record Answers(
    string? DataDrive,        // "D:"；null 表示没有数据盘
    bool CreatePartition)     // 单盘时是否允许出现“压缩 C 并新建数据盘”条目
{
    public static Answers Default(EnvironmentSnapshot s) => new(s.DataDrive, s.DataDrive == null);
}
