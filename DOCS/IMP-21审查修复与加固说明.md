# IMP-21 审查修复与加固说明

日期：2026-09-09。范围为 IMP-21 review 的三项 P2 问题，以及相邻的选项通知与应用错误保留逻辑。既有 IMP-20/21 实现和历史验收记录保留；后续 IMP-22–IMP-30 状态不变。

## 修复行为

| 问题 | 修复与边界 |
| --- | --- |
| 索引刷新失败后 WPF 仍显示旧回滚脚本、复制按钮或错误状态 | 先清除编译结果、创建/回滚脚本、评分及假设展示，再逐个事件订阅者发布失败状态。某个订阅者抛出不会中断其他订阅者；对本轮已失败的订阅者停止重试并记录诊断。直接调用 `Recalculate()` 仍保留原异常，UI 刷新边界负责收敛。 |
| 复制/保存遗漏未脱敏提示 | `ReviewOutputService` 成功消息统一附带 `OutputPrivacy.RawNotice`；索引复制保留服务消息。剪贴板和文件仍是精确的可执行 SQL，不把提示写进 SQL。 |
| 已应用或提交未知后，导出错误声称尚未应用 | 输出消息只陈述复制或新文件保存结果，原文件状态继续由实际应用结果决定。已写回、未写回和提交未知均不因导出变化。 |

相邻加固：索引名称、MAXDOP、压缩、ONLINE、SORT_IN_TEMPDB 的属性通知纳入刷新异常边界，避免选项刚改变就抛出时遗留旧脚本。改写审核分别保存审核/验证/应用错误和导出错误；导出失败并列显示，导出成功仅清除导出错误，保留应用诊断及 DUMP 提示。改变提案选择时清除旧导出错误并提示重新输出。

所有未知异常继续通过 `ExceptionPolicy` / `UnexpectedErrorReporter` 请求并校验真实 Windows minidump，同一异常去重；生成失败或诊断组件失败必须明确显示。预期 I/O、输入等错误保持可恢复提示。Debug 记录 DEBUG、WARN、ERROR、CRITICAL；Release 仅 ERROR、CRITICAL。正常动作日志不加入 SQL 或对象身份。

## 回归与证据

新增 **15 项**用例，修改前全部失败，修复后通过：

- 6 项真实 WPF 绑定用例：分别在 CurrentScore、Error、MaxDop 通知时注入故障，覆盖坏订阅者早于/晚于 WPF 订阅的顺序；检查创建/回滚文本、索引名、错误、复制按钮和修复后恢复。
- 3 项输出隐私用例：改写复制、改写新文件、索引复制保留精确 SQL/UTF-8 字节，成功状态含统一未脱敏说明。
- 6 项应用后导出用例：已写回、未写回、提交未知各覆盖复制和保存；先注入导出失败再成功，原应用错误、提交状态、备份信息和应用门槛保持正确。

同时扩展既有真实 DUMP 用例，确认故障订阅者之后的监听者仍收到脚本、按钮和错误通知；扩展成功写回用例，验证应用后复制/保存内容与实际原文件一致。

Debug、Release 完整构建均 **0 警告、0 错误**；全量测试各 **2311 通过、0 失败、0 跳过**。包括真实 minidump 结构验证、生成失败/去重、诊断组件失败和两种构建模式的日志回归。未生成覆盖率报告，不声明覆盖率提升。

真实 `App`、`MainWindow`、改写窗口及索引窗口验证由 [WPF 脚本](./verification/IMP-21-hardening/Run-WpfVerification.ps1)执行：保留 A/B、选择/diff、SQL 对照、同名跨 schema 和无效选项检查，并新增可见未脱敏提示、通知失败禁用复制/清空脚本和恢复状态。各配置 11 张截图、0 个 WPF 绑定错误，路径、源码和证据 SHA-256 见[本次验证摘要](./verification/IMP-21-hardening/summary.json)。窗口故障注入使用模拟诊断组件返回失败状态，真实 DUMP 在诊断测试中生成和校验，避免将进程内存纳入截图证据目录。

日志和 TRX 位于 `.tmp.imp21-hardening-*`；原始 DUMP 经测试验证后清理，不纳入 Git。历史 [IMP-21 实施摘要](./verification/IMP-21/summary.json)的 2296 项是实施时快照，本次以 2311 项摘要为准。

| 关键状态 | Debug | Release |
| --- | --- | --- |
| 通知失败：清空旧脚本、禁用复制、显示诊断 | [截图](./verification/IMP-21-hardening/wpf-Debug-81b3501abd284583bce6b3e333e5c4f2/index-notification-failed.png) | [截图](./verification/IMP-21-hardening/wpf-Release-a788045b00c742cba04cf4897d4ea1e1/index-notification-failed.png) |
| 索引复制的未脱敏提示 | [截图](./verification/IMP-21-hardening/wpf-Debug-81b3501abd284583bce6b3e333e5c4f2/index-sales.png) | [截图](./verification/IMP-21-hardening/wpf-Release-a788045b00c742cba04cf4897d4ea1e1/index-sales.png) |
| 改写保存的未脱敏提示 | [截图](./verification/IMP-21-hardening/wpf-Debug-81b3501abd284583bce6b3e333e5c4f2/rewrite-selected.png) | [截图](./verification/IMP-21-hardening/wpf-Release-a788045b00c742cba04cf4897d4ea1e1/rewrite-selected.png) |

## 检索与依据

按要求优先检索 Microsoft 官方资料；本次缺陷属于 .NET 事件传播与桌面输出状态，不涉及新增 SQL Server 引擎语义。官方资料已直接说明根因，无须借社区推测替代。GitHub 插件当前未提供可调用的连接器，公开源码通过 HTTP 只读获取。

1. [Microsoft：Using Delegates](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/delegates/using-delegates)说明多播按顺序调用，未捕获异常会阻止剩余调用；修复使用 `GetInvocationList()` 隔离故障订阅者。
2. [Microsoft：Implement Property Change Notification](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/data/how-to-implement-property-change-notification)说明绑定依赖属性通知；仅改变 backing field 不足以更新控件。
3. [Microsoft .NET WPF v8.0.0 源码：PropertyChangedEventManager](https://github.com/dotnet/wpf/blob/v8.0.0/src/Microsoft.DotNet.Wpf/src/WindowsBase/System/ComponentModel/PropertyChangedEventManager.cs)提供实际属性事件订阅与分发参考；本次用真实 WPF 控件回归验证。
4. 本仓库 [IMP-05 输出隐私契约](./IMP-05脱敏覆盖与输出入口验证.md)要求可执行 SQL 原样输出，并显式提示未脱敏；本次恢复三个入口的一致性。

未在真实服务器新增执行索引 DDL 或测量性能；改写原文件应用仍受既有语义场景、确认、源快照和备份约束。WPF 验证为合成输入和指定窗口尺寸，不代表完成全部 DPI/可访问性验收。

## 复现

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug
dotnet build SqlXmlAnalyzer.sln -c Release
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release
& DOCS/verification/IMP-21-hardening/Run-WpfVerification.ps1 -Configuration Debug
& DOCS/verification/IMP-21-hardening/Run-WpfVerification.ps1 -Configuration Release
```

`Write-VerificationSummary.ps1` 接受两个 WPF 输出目录，核对本次构建日志、TRX 和窗口结果并生成摘要；不会覆盖原 IMP-21 证据。
