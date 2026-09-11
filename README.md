# WinHomestead · 开荒

面向 Windows 11 全新电脑的开荒工具：启动即探测，所有可做的设置按分类列出，每一项显示当前值/目标值与状态，点“执行”立即生效，可随时回来逐项处理。

核心是“东西不进 C 盘”：目录骨架、用户文件夹迁移、开发缓存环境变量预设，加上少量可靠的界面与中文输入法设置。微软服务与预装应用默认全部保留。

> 状态：早期版本（0.1.x），功能可用但尚未广泛验证。发布产物没有代码签名，首次运行会看到 SmartScreen 提示。

## 它做什么

- **磁盘与路径**：识别数据盘，单盘无分区时给分盘建议（满足条件才提供一键压缩 C 并新建 D），在数据盘建好目录骨架，把文档/下载/图片/视频/音乐与 TEMP 迁过去。
- **开发缓存预设**：在装 Python、Node、JDK 这些工具*之前*，先把 pip、npm、pnpm、Gradle、Maven、NuGet、vcpkg、Cargo、Go、Conda、Ollama 等十几个缓存类环境变量指到数据盘。已设过的变量不动，已装的工具跳过。
- **Dev Drive 建议**：检测数据盘是不是 ReFS，按未分配空间给出创建建议与步骤。只建议，不格式化任何卷。
- **系统设置**：显示扩展名与隐藏文件、资源管理器打开到“此电脑”、关鼠标加速与粘滞键、任务栏与开始菜单风格、微软拼音的几个开关，以及一组可选的“去推送”项。全部走 HKCU，可回滚。
- **空间清理**：清理旧临时文件、关闭休眠（台式机）、开启存储感知。
- **只读检查项**：BitLocker 恢复密钥、多套杀软并存、补丁时间、还原点占用、OEM 预装软件、开机启动项、屏幕刷新率、电池健康、默认应用、区域与时区。只给结论与跳转按钮，不代改。
- **软件推荐**：官网下载页入口 + 按分类规划好的建议安装路径，一键复制。不下载、不静默安装、不内置安装包。

## 它不做什么

这些是有意不做的，不是还没做：

- 不卸载微软预装应用，不禁用系统服务，不动 Windows Update。
- 不碰 UAC、SmartScreen、内存完整性等安全设置。
- 不改 DNS、Hosts、IPv6 等网络配置。
- 不联网：不检查更新、不上传任何数据、不做遥测。
- 不扫描旧机垃圾文件——开荒面对的是全新机器。

## 使用

从 [Releases](../../releases) 下载 `WinHomestead.exe` 与同目录的 `WinHomestead.exe.config`，双击运行（会提权一次）。

- 运行环境：Windows 11 22H2 及以上，管理员账户。运行时只需系统自带的 .NET Framework 4.8。
- 每次会话第一次执行前会自动创建系统还原点。
- 所有写操作先记 journal 到 `%ProgramData%\WinHomestead\journal-*.jsonl`，单项失败自动回滚。
- 受 MDM/域管控的机器上，涉及 HKLM 的条目会自动跳过并说明原因。

工具会修改系统设置。虽然每一项都可回滚、且执行前建了还原点，仍建议先在虚拟机或不重要的机器上试一遍。本软件按“原样”提供，不承担任何担保责任（见 [LICENSE](LICENSE)）。

## 构建

需要 .NET 10 SDK（只是构建需要；产物运行只依赖 Windows 自带的 .NET Framework 4.8）。若 SDK 不在 PATH 里，可在脚本顶部改 `$sdk` 变量，或先把它加进当前会话的 PATH。

```powershell
.\build\build.ps1          # Debug 构建 + 单元测试
.\build\publish.ps1        # Release 构建（Costura 单 exe）+ 体积校验，产物在 artifacts\
.\build\publish.ps1 -Thumbprint <证书指纹>   # 同上并用 signtool 签名（SHA256 + RFC3161 时间戳）
```

`build\inspect-machine.ps1` 是一个只读采集脚本，把本机的相关设置打印到控制台，用来核对工具的探测结果。它不修改任何设置，也不把内容发到任何地方；输出里包含机器配置信息，贴到别处之前自己过一眼。

## 结构

```
src/WinHomestead.Core     模型、ITask、Planner、TaskRunner、日志、持久化（无 WPF 依赖）
src/WinHomestead.Native   注册表、环境变量、已知文件夹、Explorer、存储、还原点、环境探测
src/WinHomestead.Tasks    任务实现与任务目录
src/WinHomestead.App      WPF 界面（WPF-UI）
tests/                    xUnit 单元测试
build/                    构建脚本与只读采集脚本
```

依赖方向：App → Tasks → Core，App → Native → Core。Tasks 不引用 Native，只通过 Core 的接口访问系统，因此每个任务都能用伪实现做单元测试。每个任务都实现 Detect（无副作用）/ Apply / Verify / Rollback 四个环节。

## 文档

- [01 范围清单](docs/01-范围清单.md) — 做什么、不做什么、每一项的实现方式与取舍
- [02 用例与用户流程](docs/02-用例与用户流程.md)
- [03 技术设计](docs/03-技术设计.md)
- [04 发布检查清单](docs/04-发布检查清单.md)
- [05 开荒操作资料汇编](docs/05-开荒操作资料汇编.md) — 外部资料搜集与取舍依据

文档里的“参考机”指开发时用来对照的一台真实机器，它已经用了一段时间、装满了工具，正好用来验证“已满足”和“不适用”两条分支。

## 许可

[MIT](LICENSE)
