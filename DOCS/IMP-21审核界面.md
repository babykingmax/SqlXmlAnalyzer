# IMP-21：完成 A/B、改写和索引审核界面

后续审查的三项问题已修复，并加固选项通知与应用错误保留；新增 15 项回归，两配置全量各 2311 项通过。当前行为与验证见[审查修复说明](./IMP-21审查修复与加固说明.md)。下述 23 项新增、2296 项全量和截图为首次实施的历史快照。

实施日期：2026-09-09。对应 UI-05、UI-06、UI-09，依赖 IMP-15–IMP-20。本步界面、单元测试、异常处理、日志与文档已完成；IMP-22–IMP-30 仍待实施。

## 已实现的操作

### A/B 比较

历史列表上方提供“设为 A（基准）”“设为 B（待比较）”按钮，未选择快照时禁用。右键入口、捕获、交换、会话和人工配对保留。比较摘要公开配对、待确认与各侧未匹配数量；未匹配清单显示 Batch/Statement/QueryPlan、所属侧 SQL 和未配对原因，交换或清空后同步更新。

“匹配依据与可比性”保留每个配对的 Build、CE、兼容级别、DOP、参数、源指纹及运行采集条件。人工配对不绕过可比性检查。无运行证据、不同引擎或其他不可比条件继续显示 N/A，不生成改善百分比；B 只表示待比较版本。证据区有高度限制和滚动，展开长清单时保留两侧计划树空间。

### SQL 改写审核

审核窗口提供三个页签：逐项选择与差异、完整审核报告、完整 SQL 对照。每项显示规则/版本、选择状态、前提、风险/警告、未证明属性、证据、依赖与语法状态，以及替换前后的精确片段。偏移为 UTF-16，绑定该步骤输入 hash；有前序依赖时不误称相对于最初原文。完整对照显示原文及所选提案组合的最终预览。

| 操作 | 行为与约束 |
| --- | --- |
| 复制候选 SQL | 仅在存在所选且静态审核有效的提案时可用；复制精确 `PreviewSql`，不产生应用权限 |
| 保存为新 .sql 文件 | 严格 UTF-8 无 BOM 编码；先写同目录唯一临时文件并刷盘，再禁止覆盖地移动至目标；拒绝已有文件、路径别名指向的已有文件及非法扩展名 |
| 验证所选 SQL | 继续选择与原文一致的源文件和场景 JSON，通过既有隔离数据库验证取得应用凭据 |
| 备份并应用到原文件 | 必须有有效凭据及场景审核确认，继续强制源字节/选择复核、可靠备份及一次性写回；显示实际提交状态 |

导出不会设置场景确认或 `CanApply`。选择变化会作废既有验证凭据，并提示此前导出的内容不会自动更新。编码或写入失败不发布不完整候选，覆盖失败保留原文件及原导出字节。剪贴板占用可重试。应用边界如果未返回明确提交状态就意外抛出，界面保守显示“提交结果未知”，禁止重复应用，并提供错误/DUMP 信息供核对源文件和备份。

### 索引审核

顶部显示实例、数据库、架构、表，缺失部分使用 `?`。键列保持有序，可上移/下移；INCLUDE 与可用列分别展示。可审核 ONLINE、SORT_IN_TEMPDB、DATA_COMPRESSION（NONE/ROW/PAGE）和 MAXDOP（空表示不指定，整数 0–64）。默认选项保留既有行为，不自动判断目标环境是否支持。

索引名称留空时继续使用完整对象和列信息的稳定指纹；自定义名称作为单个原始标识符处理，严格限制长度并转义 `]`，不会将名称解释为 SQL。CREATE/DROP 使用同一次编译的名称及对象。非法名称/选项、缺少键列或未知刷新故障会清除创建与回滚脚本，禁用复制；修正后重新生成。窗口使用候选列副本，不改变原始建议。

原计划优化器 Impact 和访问算子成本、工具规则评分、用户可编辑的沙盒假设分别展示；模型未校准时不产生收益预测。窗口只提供审核和复制，不连接服务器核验既有索引、不执行 DDL。仍需核对版本/版本类型、ONLINE 的限制与锁等待、压缩支持、tempdb 和目标库空间以及索引目录。

## 异常与日志

新输出服务、索引审核刷新、快照捕获及打开审核窗口复用 `ExceptionPolicy` / `UnexpectedErrorReporter`。预期的输入、I/O、权限、编码、取消和剪贴板占用错误给出可恢复提示；未知错误记录 CRITICAL 并请求经结构校验的 Windows minidump，同一异常去重。DUMP 或诊断组件失败时保留主错误和明确失败原因，不宣称已生成文件。界面错误详情支持滚动、选择和复制。

Debug 记录 DEBUG、WARN、ERROR、CRITICAL；Release 只记录 ERROR、CRITICAL，`forceVerbose` 不能绕过构建模式。新增正常日志仅记录动作与阶段，不写 SQL 或对象名称。原始候选、诊断元数据与 DUMP 不属于脱敏输出。

## 测试与验收

新增 `ReviewWorkspaceTests` 16 项及 `ReviewWorkspaceDiagnosticTests` 7 项，合计 **23 项**：A/B 选择/交换/清空、采集条件差异、原文与步骤 diff、精确候选导出、覆盖保护、异常编码清理、剪贴板占用、同名不同 schema、恶意名称转义、键序与环境选项、无效选项恢复，以及未知异常真实 DUMP、生成失败/去重、诊断组件失败和分模式日志。

Debug、Release 全量均为 **2296 通过、0 失败、0 跳过**；完整构建均为 **0 警告、0 错误**。真实 `App` / `MainWindow` / 审核窗口验证通过，两配置绑定错误均为 **0**，各保留 9 张关键状态截图。验证读取实际控件和生产命令，使用合成 SQL/ShowPlan 输入，不把绘制的示意图当作截图。

源码、脚本、TRX、构建日志和 18 张截图的 SHA-256 见[最终验证摘要](./verification/IMP-21/summary.json)。原始日志/TRX 位于仓库 `.tmp.imp21-*`，DUMP 由诊断测试创建并验证后清理，不纳入 Git。既有 IMP-20 及更早步骤的证据保持历史快照，不用旧摘要替代本次结果。

| 关键状态 | Debug | Release |
| --- | --- | --- |
| A/B 未匹配语句与计划树 | [截图](./verification/IMP-21/wpf-Debug-69616aed7c764fda9273116a12054088/ab-unmatched.png) | [截图](./verification/IMP-21/wpf-Release-047dbb5789e54cea8b7eb8494ec55699/ab-unmatched.png) |
| 改写逐项选择及 diff | [截图](./verification/IMP-21/wpf-Debug-69616aed7c764fda9273116a12054088/rewrite-selected.png) | [截图](./verification/IMP-21/wpf-Release-047dbb5789e54cea8b7eb8494ec55699/rewrite-selected.png) |
| sales 架构索引审核 | [截图](./verification/IMP-21/wpf-Debug-69616aed7c764fda9273116a12054088/index-sales.png) | [截图](./verification/IMP-21/wpf-Release-047dbb5789e54cea8b7eb8494ec55699/index-sales.png) |
| 无效选项阻止复制 | [截图](./verification/IMP-21/wpf-Debug-69616aed7c764fda9273116a12054088/index-invalid.png) | [截图](./verification/IMP-21/wpf-Release-047dbb5789e54cea8b7eb8494ec55699/index-invalid.png) |

本次没有在真实目标服务器执行新增索引 DDL，也未测量性能改善；DDL 使用 ScriptDom 验证并核对解析后的名称、对象与列序。原文件写回继续由既有 IMP-19/03 服务及其全量回归约束，不因新增导出入口扩展数据库执行权限。此处不宣称完成全部 DPI/可访问性或 IMP-22 报告统一验收。

## 复现

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug
dotnet build SqlXmlAnalyzer.sln -c Release
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release
& DOCS/verification/IMP-21/Run-WpfVerification.ps1 -Configuration Debug
& DOCS/verification/IMP-21/Run-WpfVerification.ps1 -Configuration Release
```

WPF 脚本输出独立证据目录。`Write-VerificationSummary.ps1` 接受两个目录，通过 `.tmp.imp21-build-*`、`.tmp.imp21-tests-*` 的最终日志/TRX 校验计数与摘要。

## 设计依据

按既定优先级先核对 Microsoft 官方资料。本步环境选项的语义已由官方文档覆盖，无需扩大到 CSS 案例或社区猜测。在线索引并非所有版本类型可用，存在锁等待及额外空间要求；SORT_IN_TEMPDB 与 MAXDOP 也需结合目标环境审核。参考 [CREATE INDEX](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-index-transact-sql?view=sql-server-ver17) 和 [在线索引操作指南](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/guidelines-for-online-index-operations?view=sql-server-ver17)，检索日期 2026-09-09。没有引入新的第三方代码或依赖。
