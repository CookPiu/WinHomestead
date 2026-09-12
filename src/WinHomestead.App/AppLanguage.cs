using System;
using WinHomestead.Core.Infrastructure;

namespace WinHomestead.App;

/// <summary>
/// 启动参数里的语言覆盖：<c>--lang en</c> 或 <c>--lang=zh-Hans</c>，不给就跟随系统 UI 语言。
///
/// 中文系统上想看英文界面（或者反过来）用得上，截 README 的图也靠它。
/// 必须赶在任何文案求值之前调用——界面上的字在控件构造时就取好了。
/// </summary>
public static class AppLanguage
{
    private const string Flag = "--lang";

    public static void Apply(string[] args)
    {
        if (args == null) return;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string? value = null;
            if (a.StartsWith(Flag + "=", StringComparison.OrdinalIgnoreCase)) value = a.Substring(Flag.Length + 1);
            else if (a.Equals(Flag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) value = args[i + 1];
            if (string.IsNullOrWhiteSpace(value)) continue;

            if (value!.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) L.Chinese = true;
            else if (value.StartsWith("en", StringComparison.OrdinalIgnoreCase)) L.Chinese = false;
            return;
        }
    }
}
