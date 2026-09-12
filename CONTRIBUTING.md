# 贡献指南

欢迎 issue 和 PR。下面这些是这个项目里已经定下来的约定，改动前先扫一眼，能省掉来回。

## 构建与验证

目标框架是 net48（用 Windows 自带的运行时），SDK 风格工程，用 .NET 10 SDK 构建。

```powershell
.\build\build.ps1          # Debug 构建 + 单元测试
.\build\publish.ps1        # Release 单 exe + 体积校验，产物在 artifacts\
```

SDK 不在 PATH 里的话，改 `build\build.ps1` 顶部的 `$sdk` 变量。

**提交前必须 `0 警告 0 错误、测试全绿**。CI 在 windows runner 上跑同样的脚本。

## 分层

```
App → Tasks → Core
App → Native → Core
```

**Tasks 不得引用 Native**，只通过 Core 里的接口（`IRegistry`、`IEnvironment`、`IShell`、`IStorage`、`IPower`、`IDisplay`…）访问系统。这条是为了每个任务都能用伪实现做单元测试，不必真去改注册表。原生调用一律集中在 Native，任务里不写 P/Invoke。

## 写一个新任务

实现 `ITask` 的四个环节，再注册进 `TaskCatalog`：

- `Detect` — 判断当前状态，**不得有副作用**
- `Apply` — 只做一件事；需要多步就拆成多个任务用 `DependsOn` 串起来
- `Verify` — 重新读一遍确认
- `Rollback` — 按 journal 逐条还原

所有写操作走 `TaskContext` 里带 journal 的封装，不要直接用原始服务——journal 是回滚和执行记录的唯一依据。

键不存在或当前版本不适用时，`Detect` 返回 `DetectResult.NotApplicableBecause("原因")`，界面会把原因单独显示一行。

## 范围

**微软服务与预装应用默认保留。** 卸载 Appx、禁用服务、关闭 Windows Update 这类任务不会被接受——不同版本行为不一致、出问题难回滚，收益配不上风险。判断标准是"效果能不能感知、风险对不对称"，取舍理由逐条写在 [01 范围清单](docs/01-范围清单.md) 的删除清单里。

安全相关设置（UAC、SmartScreen、内存完整性）一律不碰。

## 文案

用户可见的字符串一律双语，两份并列写在调用点：

```csharp
L.S("显示文件扩展名", "Show file extensions")
```

XAML 里用标记扩展，值一律用单引号包起来，否则含逗号的英文句子会被解析器切开：

```xml
Text="{local:Loc Zh='检查项', En='Checks'}"
```

日志与内部异常消息保持中文——它们进的是日志文件，读者是开发者。

两条容易踩的坑：

- **界面逻辑不能比较显示文本。** 曾经有个 `DataTrigger` 拿 `StatusLabel` 跟字符串「推荐」比来决定徽标底色，文案一换语言绿底就没了。要比就比布尔属性。
- **共用词不能用 `static readonly` 字段缓存。** 字段在类型初始化时就定死，换语言重建目录取不到新值，用属性。

测试断言中文文案的，靠 `AssemblyFixture.cs` 里的 `ModuleInitializer` 把整个测试程序集固定成中文；需要英文的用例自己临时切换并在 `finally` 还原。

## 文档

设计文档在 `docs/`，改动范围或流程时同步更新对应那份：

| 文档 | 什么时候要改 |
|---|---|
| [01 范围清单](docs/01-范围清单.md) | 加了新条目、改了取舍 |
| [02 用例与用户流程](docs/02-用例与用户流程.md) | 新增用例、改了交互流程、任务库索引变了 |
| [03 技术设计](docs/03-技术设计.md) | 改了分层、执行引擎、原生层封装 |
| [04 发布检查清单](docs/04-发布检查清单.md) | 新增了需要人工走查的东西 |

## 更新 README 的截图

程序跟随系统语言，但可以用启动参数覆盖：

```powershell
.\WinHomestead.exe --lang en     # 强制英文界面
.\WinHomestead.exe --lang zh     # 强制中文界面
```

README 里的截图用英文界面，所以在中文系统上也能直接 `--lang en` 跑起来截。

（试过在测试里离屏渲染主窗口来自动生成截图：顶部导航能出来，但 ContentControl 里的
内容页在没有真实窗口的情况下展不开，主体是空白。放弃了，手动截更省事。）

## 提交信息

说清楚改了什么、为什么。不要加任何 AI 署名或生成标记。
