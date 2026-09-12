using System;
using System.Globalization;

namespace WinHomestead.Core.Infrastructure;

/// <summary>
/// 双语文案。中英两份并列写在调用点：
///
///     L.S("显示文件扩展名", "Show file extensions")
///
/// 没有走 .resx 是有意的——这个项目的用户可见文字大多是带插值的整句（任务说明、检测结论、
/// 手动步骤），资源文件把它们拆成几百个键之后，漏译和格式串对不上都不会在编译期暴露，
/// 而并列写法改一句就是改一处，评审时两种语言也在同一屏里。
///
/// 语言按系统 UI 语言定，zh-* 用中文，其余用英文。
/// </summary>
public static class L
{
    private static bool? _chinese;

    /// <summary>测试与将来的手动切换可以直接赋值覆盖。</summary>
    public static bool Chinese
    {
        get => _chinese ??= CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("zh", StringComparison.OrdinalIgnoreCase);
        set => _chinese = value;
    }

    /// <summary>按当前语言取一份文案。</summary>
    public static string S(string zh, string en) => Chinese ? zh : en;

    /// <summary>顿号在英文里要换成逗号加空格，拼接列表时用它。</summary>
    public static string Join(params string[] parts) => string.Join(Chinese ? "、" : ", ", parts);
}
