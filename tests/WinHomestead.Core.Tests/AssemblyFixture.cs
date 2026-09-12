using Xunit;

// L.Chinese 是进程级静态，双语的用例会临时把它扳到英文。xunit 默认让不同测试类并行跑，
// 那样别的用例正好在英文状态下断言中文文案，失败还飘忽不定。整个程序集串行最省事——
// 这些用例全是内存里的纯逻辑，总共也就几秒。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
