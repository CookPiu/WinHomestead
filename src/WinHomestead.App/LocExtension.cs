using System;
using System.Windows.Markup;
using WinHomestead.Core.Infrastructure;

namespace WinHomestead.App;

/// <summary>
/// XAML 里的双语文案：<c>Text="{local:Loc Zh='检查项', En='Checks'}"</c>。
/// 值一律用单引号包起来，否则含逗号或等号的英文句子会被标记扩展的解析器切开。
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }
    public LocExtension(string zh, string en) { Zh = zh; En = en; }

    public string Zh { get; set; } = string.Empty;
    public string En { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => L.S(Zh, En);
}
