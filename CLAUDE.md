# NewPcSetup 项目说明

- 目标框架 net48（Windows 自带运行时），SDK 风格工程，用 .NET 10 SDK 构建。
- 本机 SDK 路径 `D:\Applications\dotnet`（不在 PATH 中）。构建/测试用 `build\build.ps1`，或先执行 `$env:PATH = "D:\Applications\dotnet;$env:PATH"`。
- 依赖方向：App → Tasks → Core；App → Native → Core。Tasks 不得引用 Native，只通过 Core 接口访问系统。
- 任务必须实现 Detect（无副作用）/ Apply / Verify / Rollback；所有写操作通过 TaskContext 中带 journal 的封装进行。
- 微软服务与预装应用默认保留，不新增卸载、禁服务、关更新类任务。
- 设计文档在 docs/，改动范围或流程时同步更新对应文档。
