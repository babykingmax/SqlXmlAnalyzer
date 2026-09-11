# IMP-07 字段映射与绑定修正

实施日期：2026-09-08。范围：R06、R11、R21 当前最小修复及 UI-01 / UI-04 的相关字段；完整证据模型、死锁因果判定和报告出口分别保留给 IMP-11、IMP-13、IMP-20、IMP-22。

审查后已修复 `N/A` 导致估算计划重算成本归零的回归，并以 `HasActualRows` 贯通节点、成本和连线；真实 0 与部分线程计数的消费行为也已加固。最新 Debug / Release 各 1135 项测试通过，见 [审查修复与加固说明](IMP-07审查修复与加固说明.md)。下方 1106 项记录保留为初次实施证据。

## 字段契约

| 字段 | 读取与展示规则 |
| --- | --- |
| 死锁优先级 | 按属性是否存在选择 `priority` → `currentdeadlockpriority` → `deadlockpriority`；`taskpriority` 不作为别名。选中字段非法时不回退到低优先级别名。使用 invariant 有符号整数解析和规范化，支持负数、0、7；缺失/非法在旧字符串模型保留空串，详情及 Mermaid 显示 `N/A`。不把缺失值当作 0。 |
| 实际执行模式 | 仅读取当前 `RelOp/RunTimeInformation/RunTimeCountersPerThread/@ActualExecutionMode`。各线程完整且相同时显示 Row 或 Batch，不同则显示 `Mixed (Batch, Row)`；部分缺失或非法不声称已获得完整实际模式。 |
| 估算执行模式 | 仅读取当前 `RelOp/@EstimatedExecutionMode`。没有完整实际模式时，已知估算显示 `Row (估算)` / `Batch (估算)`；均未知则 `N/A`。详情分列实际和估算，保留分歧。 |
| 并行 | 仅读取当前 `RelOp/@Parallel`，`IsParallel` 改为 `bool?`。缺失/非法为 null，界面显示 `N/A`；不从 ThreadStat、子算子或 Parallelism 名称推断。并行图标仅在明确 true 时出现。 |
| Ordered | 仅读取当前算子直属 `IndexScan` / `TableScan` / `XcsScan` 的 Ordered；不从 LogicalOp=Sort、后代算子或 InternalInfo 推断。合法值规范化为 True/False；缺失、非法或多个扫描节点为 `N/A`。此属性不是通用的“输出是否有序”结论。 |
| 实际输出行 | 两处全局属性表均绑定 `ActualRows`，标题“实际输出行（行）”。图中的实际输出 0 保留 0，缺少实际行时，行数模式明确显示 `Est R`。 |
| 实际读取行 | 两处表格另列 `ActualRowsRead`，标题“实际读取行（行）”；图提示和详情使用同一计数结果。缺失读取行时不使用输出行代替，部分线程缺失时完整读取总数为 `N/A`。 |

XML 布尔值遵循 `true` / `false` / `1` / `0`，允许 XML 空白；`TRUE`、yes、空串等为非法值。执行模式仅接受 XSD 中的 Row/Batch，命名空间及当前算子作用域严格隔离。

行计数按 XSD `unsignedLong` 读取，以 decimal 精确累计并统一使用 invariant 千分位格式，因此超过 2^53 的整数、UInt64 最大值以及跨线程总和仍能准确显示；现有成本、图布局和规则计算继续使用 double，精度模型的全面统一留待 IMP-11。空 RunTimeInformation、非法/负数/小数/溢出计数不作为已观测行数。

## 实现与异常处理

- Core 的 `PlanExecutionFactsService` 统一执行模式和布尔事实；节点构建与属性详情共同消费它。行数展示复用 `PlanGraphRuntimeCountersService`，避免两处 UI 分别求值。
- `DeadlockXmlParser` 返回非法优先级警告，并通过原有异常边界处理未知故障。`PlanGraphNodeBuilderService` 新增诊断边界，未知异常生成 DUMP 后重新抛出原异常，不返回伪造节点。
- 完整 WPF 检查发现旧 `PlanView` 注册的 NodeSelected / NodeDoubleClicked 没有对应方法，导致初始化异常；已补齐兼容签名，选择时加载共享属性，清空选择时清除旧详情，异常进入 `Logger.LogException`。
- 已复用真实 `WindowsMiniDumpWriter`、`UnexpectedErrorReporter` 和异常去重；保留 `exception.json` 中的托管堆栈，验证实际 `.dmp` 格式。目录和写入失败行为继承 IMP-04/06：默认 `%LOCALAPPDATA%\SqlXmlAnalyzer\dumps`，失败时尝试 `%TEMP%\SqlXmlAnalyzer\dumps`，无法生成则明确记录失败。诊断失败不能覆盖原始异常。
- Debug 输出 DEBUG / WARN / ERROR / CRITICAL；Release 仅 ERROR / CRITICAL。正常缺失不报错误，非法字段记录 WARN；新的字段日志不记录原始 XML、SQL、标识符或非法属性值。输入格式错误不触发 DUMP。

## 检索依据

按 Microsoft 官方资料优先核对，足以确定当前字段，不需要用社区经验替代结构证据。搜索工具连接失败后，通过 HTTPS 直接读取 Microsoft 官方文档及其 GitHub 源文件。

1. [Microsoft 死锁指南](https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-deadlocks-guide)：官方 XML 样例分别包含 priority 和 taskpriority。[MicrosoftDocs 源文件](https://github.com/MicrosoftDocs/sql-docs/blob/live/docs/relational-databases/sql-server-deadlocks-guide.md)。两个兼容别名来自已有应用兼容行为，不宣称它们是标准字段。
2. [Microsoft Showplan SQL Server 2019 XSD](https://schemas.microsoft.com/sqlserver/2004/07/showplan/sql2019/showplanxml.xsd)：确认模式作用域、Ordered 所属类型、XML boolean 和 unsignedLong 行计数。复用仓库已有原始 XSD，SHA-256 `845B3FAA55748D442DFC71071315FF1EBB611E4E298E8D06A21EBF946B8C93A8`。
3. [Microsoft Showplan 算子参考](https://learn.microsoft.com/en-us/sql/relational-databases/showplan-logical-and-physical-operators-reference)：Ordered 是扫描属性，不能通过 Sort 名称替代读取。

## 验证与交付

新增合成夹具 [imp07_field_mapping.sqlplan](../SqlXmlAnalyzer.Tests/TestData/imp07_field_mapping.sqlplan)，通过 Microsoft 原始 XSD 校验；它是结构合法的合成反例，不是本次现场 SQL Server 捕获。

- 首轮测试：33 个映射用例中 27 失败、6 通过，证明原始缺陷可复现。[红灯记录](./implementation/IMP-07/tests/imp07-red.trx)。
- 定向回归：77 项通过，覆盖标准优先级及别名冲突、0/缺失/非法、模式分歧和混合线程、作用域隔离、布尔变体、精确大整数、区域格式、真实 WPF 单元格、事件签名、真实 DUMP 与日志策略。[记录](./implementation/IMP-07/tests/imp07-focused.trx)。
- 独立 WPF 进程使用实际 App 资源、DI 和 MainWindow 打开夹具；两种视图保留真实 ItemsSource 绑定，验证生成单元格、生产图模板的提示内容和旧视图节点详情。[进程结果](./implementation/IMP-07/ui-process.json)、[观察值](./implementation/IMP-07/ui/observed.json)、[探针源码](./implementation/IMP-07/FieldMappingUiProbe.cs.txt)。视图离屏布局与渲染，未操作数据库。
- 最终 Debug / Release 均完整构建 0 警告、0 错误，各 1106 项单元测试和 42 项验收工具回归通过。本次新增 62 项测试。R06/R11/R21 最低条件满足，矩阵共 10 项满足、12 项 NotMet、0 项探针执行失败；整体退出码 1 表示仍有后续步骤，不自动关闭评审问题。[Debug 摘要](./acceptance/IMP-02/runs/20260908T125119934Z_c5b1e0766cfc_2d768643/summary.json)、[Release 摘要](./acceptance/IMP-02/runs/20260908T125213729Z_c5b1e0766cfc_0243fdb7/summary.json)、[汇总与代码指纹核对](./implementation/IMP-07/verification.json)。

复验命令（PowerShell 7）：

```powershell
$env:UseArtifactsOutput = 'true'
$env:ArtifactsPath = Join-Path $PWD 'bin/imp07-20260908'
./DOCS/acceptance/IMP-02/Run-AcceptanceMatrix.ps1 -Configuration Debug
./DOCS/acceptance/IMP-02/Run-AcceptanceMatrix.ps1 -Configuration Release
./DOCS/implementation/IMP-07/Run-FieldMappingUiProbe.ps1
```

使用独立 bin 内产物目录避开原默认 WPF 中间文件的同步程序锁定；两配置执行完整构建及全套测试。最后 WPF 探针直接引用该 Release 产物；可重复脚本记录程序集及探针哈希。验证后核对两份输入快照的源码哈希均无漂移；未生成覆盖率报告。

截图：[主工作区表格](./implementation/IMP-07/ui/PlanWorkspaceView-table.png)、[旧版表格](./implementation/IMP-07/ui/PlanView-table.png)、[图节点提示](./implementation/IMP-07/ui/graph-tooltip.png)。导出报告最终一致性仍按 IMP-22 验收，本次不声称所有导出出口已统一。
