# IMP-22 审查修复与加固

日期：2026-09-10。修复审查确认的报告内存放大（P1）和游标选择 XML 层级丢失（P2）。

## 报告构建与输出预算

`ReportSizeBudget` 在保留字段、诊断、规则运行、节点和边之前计入共享预算，源 XML 也计入同一预算。默认限制为 800 万内容字符及 10 万条总记录；记录数包含字段和图记录，不等于算子数。字符预算包含分类、项目头和固定开销，属于保守上限。超过限制抛出可恢复的 `REPORT_BUDGET_EXCEEDED`，不返回截断报告。

- 完全没有指标的算子沿用 `PlanOperator.ExportFacts` 的紧凑策略，保留算子身份并用 `Metrics/State=Missing`、`Metrics/Value=N/A` 表达缺失。存在指标的算子继续保留原有数值、状态、单位及聚合方式。
- 工厂、不可变快照构造和脱敏快照共用预算；懒枚举达到上限后停止，不先对全部记录执行 `ToArray`。
- JSON 缓冲区在扩容请求前检查大小；XML 和正文在追加字符前检查。HTML/JSON/SVG 另设 6400 万编码长度上限，为转义留出空间。限制不是进程峰值内存保证。
- 预览分别缓存不可变的原始、脱敏正文，状态通知不再反复生成全量文本。文件写入器按目标格式渲染，SVG/sqlplan 不再先生成无用正文。
- 工厂及渲染路径传递取消令牌，取消或超预算沿用既有失败提示和暂存文件清理。
- CLI 超预算返回 `Failed`、退出码 1，并清除待输出的 `Plan`、`Diagnostics`、`DiagnosticReport`，防止失败结果通过旧 DTO 再次展开完整模型。输入识别摘要与明确失败原因仍保留。

完全缺失指标的事实投影形状有变化；没有新增规则 ID 或调整诊断严重度。

## 保留所选计划的 XML 结构

`PlanReportXml` 从原始语句流式写出选择范围，保留 `StatementSetOptions`、`CursorPlan`、`Operation`、操作类型、命名空间及原有属性。排除其他已识别语句时按源元素身份处理；选择游标单个 QueryPlan 时排除整个未选中的 Operation。完整游标语句仍保留两个操作。

新增固定夹具包含 PopulateQuery/FetchQuery 两个操作及重复 NodeId。选择任一操作后，原始和脱敏 `.sqlplan` 都通过仓库保存的 Microsoft SQL Server 2019 Showplan XSD 校验。条件语句的 `Condition`、`Then/Statements` 容器和普通语句选项也得到保留。

XSD 要求 RECEIVE 计划恰有两个 Operation。单操作范围现在明确返回 `REPORT_SCOPE_NOT_REPRESENTABLE`；工厂的完整语句范围及 CLI 文档范围仍可输出完整 RECEIVE 计划。当前 GUI 的单操作选择不会生成结构无效的报告。

## 分配量与复现

使用 22,100 个字符、1,000 个完全缺失指标的算子，相同 PowerShell/.NET 运行时、相同 Debug 配置。测试只调用空规则引擎，测量报告构建和正文生成，排除此前的身份模型构建。

| 项目 | 修复前 | 修复后 |
| --- | ---: | ---: |
| 事实字段 | 169,001 | 6,001 |
| 正文字符 | 8,828,145 | 824,584 |
| 工厂累计分配字节 | 63,935,880 | 8,242,000 |
| 正文生成累计分配字节 | 35,440,696 | 3,325,680 |
| 两个游标选择的 XSD 错误数 | 各 1 | 各 0 |

分配量是 `GC.GetAllocatedBytesForCurrentThread` 的一次进程内测量，包含临时对象，不代表峰值驻留内存或稳定性能基准。原始记录和程序集 SHA-256 见 [before.json](./verification/IMP-22-hardening/before.json)、[after-debug.json](./verification/IMP-22-hardening/after-debug.json)；另有 [Release 复核](./verification/IMP-22-hardening/after-release.json)。旧程序集的参数数目为 7，新版增加取消令牌后为 8，可辅助辨别记录对应版本。

实际 CLI 对超预算计划返回完整的 1,381 字节失败 JSON，退出码 1，三个大型模型字段均为 null，输入哈希不变，见 [CLI 样例](./verification/IMP-22-hardening/cli-sample.json)。

新增 26 项自动化回归，覆盖预算、懒枚举、单次写入原子性、取消、预览缓存、CLI 失败输出、原始/脱敏游标文件、前缀命名空间、普通/条件/RECEIVE 范围。Debug/Release 全量各 **2,414 通过、0 失败、0 跳过**，完整构建各 **0 警告、0 错误**。日志、TRX 和哈希见 [验证摘要](./verification/IMP-22-hardening/summary.json)。既有六格式导出、失败清理及诊断测试一并运行。

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build
dotnet build SqlXmlAnalyzer.sln -c Release
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release --no-build
# 输出必须使用新的文件名；每次测量使用新 PowerShell 进程。
& DOCS/verification/IMP-22-hardening/Measure-ReportBudget.ps1 -Configuration Release -OutputPath .tmp.imp22-measure-new.json
```

本轮验证覆盖结构校验、实际文件和自动化测试，未进行 SSMS 实际打开或完整 GUI 人工验收，也未生成覆盖率报告。

## 官方依据

- [Microsoft SET SHOWPLAN_XML](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-showplan-xml-transact-sql?view=sql-server-ver17)：Showplan 输出及官方 XSD 的位置。具体 CursorPlan、Operation、StmtCond 和 ReceivePlan 约束依据仓库中的 `TestData/Schemas/showplanxml-sql2019.xsd`。
- [Microsoft StringBuilder 补充说明](https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-text-stringbuilder)：连续小额追加可能越过 MaxCapacity，因此这里在每次写入前显式检查长度。
- [dotnet/runtime v8.0.0 XNode 官方源码](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Private.Xml.Linq/src/System/Xml/Linq/XNode.cs)：`ToString` 最终写入 StringWriter；本次改为受预算约束的 TextWriter/XmlWriter，保留标准 XML 转义。

官方文档通过 HTTPS 读取；GitHub 插件在本任务没有可调用的连接器，官方运行时源码通过公开 raw URL 读取。未引入第三方实现或新依赖。
