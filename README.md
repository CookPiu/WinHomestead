# NewPcSetup · 新机开荒

面向 Windows 11 全新电脑的一次性开荒工具：探测 → 问卷 → 方案预览 → 一键执行 → 报告。
核心是"东西不进 C 盘"：目录骨架、用户文件夹迁移、开发缓存环境变量迁移，加上少量可靠的界面与中文输入法设置。微软服务与预装应用默认全部保留。

文档：

- [01 范围清单](docs/01-范围清单.md)
- [02 用例与用户流程](docs/02-用例与用户流程.md)
- [03 技术设计](docs/03-技术设计.md)

## 构建

构建机需要 .NET 10 SDK（产物不依赖，运行只需 Windows 自带的 .NET Framework 4.8）。

```powershell
# 本机 SDK 位于 D:\Applications\dotnet，脚本会自动加入当前会话 PATH
.\build\build.ps1          # Debug 构建 + 单元测试
.\build\publish.ps1        # Release 构建（Costura 单 exe）+ 体积校验，产物在 artifacts\
.\build\publish.ps1 -Thumbprint <证书指纹>   # 同上并用 signtool 签名（SHA256 + RFC3161 时间戳）
```

发布前的人工验收步骤见 [04 发布检查清单](docs/04-发布检查清单.md)。

## 结构

```
src/NewPcSetup.Core     模型、ITask、Planner、TaskRunner、日志、持久化（无 WPF 依赖）
src/NewPcSetup.Native   注册表、环境变量、已知文件夹、Explorer、还原点、环境探测
src/NewPcSetup.Tasks    任务实现与任务目录
src/NewPcSetup.App      WPF 界面（WPF-UI）
tests/                  xUnit 单元测试
build/                  构建脚本与只读采集脚本
```

## 运行时要求

- Windows 11 22H2 及以上
- 管理员账户（程序清单声明 requireAdministrator，启动时提权一次）
- 所有改动执行前写入 `%ProgramData%\NewPcSetup\journal-*.jsonl`，失败自动回滚
