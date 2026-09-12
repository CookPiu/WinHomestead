using System.Runtime.CompilerServices;
using WinHomestead.Core.Infrastructure;
using Xunit;

// L.Chinese 是进程级静态，双语的用例会临时把它扳到英文。xunit 默认让不同测试类并行跑，
// 那样别的用例正好在英文状态下断言中文文案，失败还飘忽不定。整个程序集串行最省事——
// 这些用例全是内存里的纯逻辑，总共也就几秒。
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace WinHomestead.Core.Tests;

/// <summary>
/// 把整个测试程序集固定成中文跑。
///
/// 多数用例断言的是中文文案，而语言默认跟着 CurrentUICulture 走：开发机是中文系统所以通过，
/// CI runner 是 en-US 就整片失败。测试不该依赖运行环境的语言，这里一次性定死。
/// 需要英文的用例（LocalizationTests）自己临时切换并在 finally 里还原。
/// </summary>
internal static class TestLanguage
{
    [ModuleInitializer]
    internal static void UseChinese() => L.Chinese = true;
}
