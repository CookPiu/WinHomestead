# WinHomestead 项目说明

- 目标框架 net48（Windows 自带运行时），SDK 风格工程，用 .NET 10 SDK 构建。
- 构建/测试走 `build\build.ps1`。脚本里的 `$sdk` 是 .NET 10 SDK 目录（默认 `D:\Applications\dotnet`），SDK 已在 PATH 时这行不起作用；换机器改这一个变量即可。
- 依赖方向：App → Tasks → Core；App → Native → Core。Tasks 不得引用 Native，只通过 Core 接口访问系统。
- 任务必须实现 Detect（无副作用）/ Apply / Verify / Rollback；所有写操作通过 TaskContext 中带 journal 的封装进行。
- 微软服务与预装应用默认保留，不新增卸载、禁服务、关更新类任务。
- 用户可见文案一律双语：`L.S("中文", "English")`，XAML 用 `{local:Loc Zh='…', En='…'}`。日志与内部异常消息保持中文。界面逻辑不得比较显示文本（换语言就失效），要比就比布尔属性。
- 设计文档在 docs/，改动范围或流程时同步更新对应文档。
