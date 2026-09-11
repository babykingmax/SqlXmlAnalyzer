# IMP-17：多语句与可比性检查的 A/B 比较

最新契约见[比较证据完整性加固](IMP-17比较证据完整性加固.md)：比较模型 1.2.0，补齐 BuildResidual、Sort/TopSort 和未知对象身份门槛，Debug/Release 全量各 2012 项通过。以下 1.0.0/1.1.0 版本及其测试计数保留为历史记录。

实施日期：2026-09-09。完成本步多语句匹配、指标可比性、人工选择和相关界面连接。前置基线为 IMP-16 审查后的 Debug/Release 各 1876 项测试，本步新增 52 项，最终各 1928 项通过。

同日[审查修复与加固](IMP-17审查修复与加固说明.md)：修复 Seek 结构身份、同一快照 A/B 选择覆盖和会话来源指纹丢失，追加 29 项回归，Debug/Release 各 1957 项通过。当前模型版本为 `plan-comparison/1.1.0`，会话版本为 `2.1`；本篇下方 1.0.0 实现与 1928 项验证记录保留为初版历史，选择及指纹契约以加固说明为准。

后续收到临时 `Probe.exe` 写入截图时的文件占用 APPCRASH 报告，已补齐验证探针的顶层异常捕获、只新建输出及故障进程回归。此前的独立目录修复只避开了覆盖冲突；完整处理和本次证据见 [验证探针异常修复](IMP-17验证探针异常处理修复.md)。

## 行为与匹配契约

旧比较器对多 QueryPlan 输入要求先选择一个范围，随后按子节点序号递归配对；顶部另用快照总成本计算百分比，绕过了语句及采集条件检查。初始多语句回归在修改前抛出 `PLAN_SELECTION_REQUIRED`，见 [失败记录](verification/IMP-17/red-tests.log)。

现在默认读取 IMP-10 模型中的全部语句，先匹配语句，再在该语句内匹配 QueryPlan，最后匹配算子。第二条语句成本从 **2 变为 999** 可见；有充分捕获条件时，本侧 B 的绝对差值为 **997**、相对 A 为 **49850%**。第一条不变，不会遮住第二条。缺失语句、重复候选、无 QueryPlan 的语句和无法匹配的算子均保留。

| 层级 | 依据与边界 |
| --- | --- |
| 语句 High | 唯一 SQL 词法序列、语句种类与捕获对象一致。忽略空白/注释，保留字面量与标识符大小写；不是 SQL 等价证明 |
| 语句 Candidate | SQL 一致但对象不同；或唯一 QueryHash/对象候选；或唯一对象/结构候选。仅作为待确认对应，不能直接产生数值差值 |
| 语句 Manual | 用户从两侧选择任意语句/QueryPlan。人工配对不绕过版本、参数或指标口径检查 |
| QueryPlan | 已匹配语句内各只有一个计划时配对；多个计划要求唯一对象/结构，重复形状保持未匹配，不按序号猜测 |
| 算子 | 使用类型、逻辑角色、捕获对象/别名、谓词和子树签名。NodeId 只保留为源身份，不作为跨计划匹配键；子节点重排不按位置误配 |
| 物理算子改变 | 相同逻辑结构可形成 Candidate；显示对应关系，不直接比较该算子的数值指标 |
| 未匹配 | 保留两侧树和空侧占位。Added/Removed 是本侧未匹配标记，不证明 SQL 操作确实新增/删除 |

自动匹配只接受当前阶段两侧各唯一的候选，不贪心消费重复 SQL 或重复算子。对象保留数据库/schema/表及捕获服务器、别名信息；不会仿照 SSMS 的默认选项自动忽略数据库名。结构签名不包含成本或 NodeId，子树采用排序后的摘要；成本变化不会改变匹配键。所有对应仍受启发式模型限制，尤其是重写、别名变化、自连接、多个相似访问及复杂嵌套计划。

核心证据与匹配位于 [PlanComparisonEvidence.cs](../SqlXmlAnalyzer.Core/Comparison/PlanComparisonEvidence.cs)，版本为 `plan-comparison/1.0.0`。语句源码通过 `PlanDocument.GetStatementSource` 定位，忽略 InternalInfo 内伪造的语句。控制器只在已匹配语句/QueryPlan 的范围内构造算子对应，不跨语句复用 NodeId。

## 可比性与指标

采集记录保留引擎 Build、ShowPlan schema 版本、可用的兼容级别、CE、SET 选项、DOP、参数名称/类型/编译值/运行值、运行记录存在性及输入哈希。原始文件字节哈希可用时保留；否则计算 XML 表示的 SHA-256，明确标注来源，不能把它当作原文件字节哈希。

兼容级别没有记录时为 N/A，不从 CE 版本推导。标准 ShowPlan 经常未提供兼容级别；双方均未知时记录限制，允许继续做“捕获条件下的估算比较”；一侧已知或双方已知但不同则停止差值。硬件、缓存、统计信息时点和负载未被单份计划证明，因此任何差值都不是性能改善结论。

| 检查 | 数值差值条件 |
| --- | --- |
| 估算成本 | 语句为 High/Manual、QueryPlan 对应明确、捕获对象一致；有效且一致的 Build、CE、七项 SET 选项、编译参数；两侧算子 High 匹配且成本有效 |
| 编译参数 | 两侧明确 ParameterList、无重复参数名称、相同名称/类型/编译值；明确空列表与缺失列表不同 |
| 运行条件 | 估算采集条件通过，双方有运行记录、运行参数一致、DOP 有效且相同 |
| 算子运行口径 | 上述运行条件通过，且逻辑执行次数可确定且相同、Parallel 和实际执行模式均存在且一致 |
| 单项指标 | 两侧该项均完整、非负且有限；任一项缺失不妨碍保留其他已观测值 |
| 百分比 | 基准必须大于 0，结果有限；零基准仅保留绝对差值，百分比为 N/A |

七项 SET 选项为 ANSI_NULLS、ANSI_PADDING、ANSI_WARNINGS、ARITHABORT、CONCAT_NULL_YIELDS_NULL、NUMERIC_ROUNDABORT、QUOTED_IDENTIFIER；支持 XML 布尔值 `true/false/1/0`，非法值不能构成可比证据。ParameterList 或运行参数缺失时，严格保留未知；不因两个字段都没有就认定参数相同。

复用 IMP-11 当前算子事实：Elapsed 来自 `ActualElapsedms`，单位 ms、线程最大值；Logical reads 来自 `ActualLogicalReads`，单位页、线程合计；Rows read 来自 `ActualRowsRead`，单位行、线程合计。输出保留本侧值、另一侧值、可空差值/百分比、指标来源及停止计算的原因。树中本侧差值是“本侧减另一侧”，因此 B 侧是 B−A，A 侧反向；不是同一个分母的重复显示。估算子树成本不等于耗时，也不把父子成本相加形成性能收益。

Actual 与 Estimated 可以保留结构和满足捕获条件的估算比较；缺少的运行指标为 N/A，不生成虚构的运行改善值。未验证运行实验、统计信息时间及工作负载，物理算子候选配对也不会自动升级为可比测量。

## 界面与快照

比较页默认列出所有语句/计划配对、未匹配数量及待确认数量。两侧树按配对行保留占位；语句头显示位置与置信度，悬停显示匹配依据、范围和来源。展开“匹配依据与可比性”可查看版本、参数编译/运行值、输入指纹及不可比原因。

“人工选择 A/B 的语句”下拉框允许选任意 QueryPlan，“比较所选”应用配对，“全部语句”恢复自动匹配。B 明确标为“待比较”。顶部显示逐语句检查提示，不再由两份总成本直接宣称优化成功。成本/运行信息换行显示，可滚动查看。

快照总估算成本改为可空：汇总顶层语句各自唯一 QueryPlan 的唯一根成本；任何所需成本缺失、根不唯一或语句有多个计划时为未知，不只取首语句。复杂控制流不将互斥路径或嵌套执行成本强行求和。总成本只作快照摘要，不直接进入跨快照改善率。

`.pesession` 保留完整源 XML、捕获时间、原始哈希（若已知）、XML 表示哈希和人工 QueryPlan 选择。载入时从源 XML 重新计算摘要，并把选择重建为当前文档身份；陈旧或不存在的选择明确报错。新增字段可选，保持既有会话可读取。原始参数与 SQL 属于未脱敏会话内容，沿用既有 Privacy 标记。

## 异常、DUMP 与日志

比较入口统一经 `ExceptionPolicy` 分类，支持取消令牌；源模型构建、匹配阶段和算子构建检查取消。预期输入/选择错误、I/O 和取消不产生未知故障 DUMP。

未知异常生成 Windows minidump 与 `exception.json`，同一异常实例只捕获一次；DUMP 写入失败记录失败状态并传播原异常。新测试在第二条语句抛出未知异常，验证不返回部分比较结果，并实际写入/校验 minidump。测试专用 DUMP 已清理。

界面在两侧树全部准备成功后发布；失败时清空旧树、摘要和选择候选，显示错误，后续交换、清空、恢复选择仍能继续。沿用编译配置日志策略：Debug 为 DEBUG/WARN/ERROR/CRITICAL，Release 仅 ERROR/CRITICAL，CRITICAL 对应致命信息。比较日志只记录计数/状态，不输出 SQL、参数值或对象名称；强制 verbose 不绕过配置。

## 验证与兼容变化

- 新增 44 个多语句、匹配、可比性、数值边界、来源及会话用例；4 个 DUMP/日志/取消用例；2 个界面操作/绑定用例；2 个主题文字对比度用例，共 **52 项**。
- Debug/Release 完整解决方案构建各 **0 警告、0 错误**，全量 xUnit 各 **1928 通过、0 失败、0 跳过**。旧“总成本直接产生优化百分比”和“多语句必须报选择错误”的测试随新契约更新。
- 新增 [A 夹具](../SqlXmlAnalyzer.Tests/TestData/imp17_comparison_a.sqlplan)与 [B 夹具](../SqlXmlAnalyzer.Tests/TestData/imp17_comparison_b.sqlplan)：第二条 2→999，Elapsed 50→25 ms，第三条不同 SQL 仅为结构候选而不给差值。这两份是合成输入，不是性能实验。
- 重放 IMP-16 中 SQL Server 17.0.4075.5 的原生捕获计划，反转语句顺序后逐条 SQL 对应正确，运行记录缺失仍不可比。本步没有创建新的 SQL Server 实例或执行线上工作负载。
- 完整生产 WPF 比较页使用真实服务事件连接运行：Debug/Release 两种配置、两种主题资源切换、三语句、人工选择/重置、第二条成本与运行差值、0 绑定错误。保留截图并检查文字裁切；这是当前应用主题的呈现，后续整体界面改造仍属于 IMP-21。
- 更新 IMP-02 当前验收适配器的 R16 读取方式，按第二条语句取证；旧运行目录不变，验收基础设施 42 项回归全部通过。本步没有重跑整套 R01–R22 验收工具或宣布所有问题关闭，R15/R16 的本步条件由上述回归覆盖。

`PlanComparisonResult.Statements` 是完整结果。兼容字段 PlanA/PlanB 仅在本侧只有一个根时返回该根；多根为 null，调用方不能继续把首根当作全部结果。节点成本、另一侧成本、成本百分比及 `PlanSnapshot.TotalCost` 改为可空，调用方需处理 N/A。新增置信度、来源、理由和快照选择字段，没有新规则 ID 或 RuleConfiguration.json 变更。CLI 未新增比较命令；全量套件继续覆盖既有 CLI 功能。

构建时 Google Drive 占用旧 `obj/Debug/net8.0-windows/SqlXmlAnalyzer.g.resources`，通过 Windows Restart Manager 定位。验证使用 `-p:IntermediateOutputPath=obj/imp17-final/Debug/`（Release 对应替换）及 `--disable-build-servers -m:1`，没有终止同步程序或删除用户文件。复跑时请选择新的 obj 子目录；WPF 脚本自动生成唯一目录。截图复核使用每次独立的输出目录，避免覆盖被同步锁定的二进制产物。

证据：[验证清单及哈希](verification/IMP-17/validation.json)、[Debug 构建](verification/IMP-17/build-debug.log)/[测试](verification/IMP-17/test-debug.log)、[Release 构建](verification/IMP-17/build-release.log)/[测试](verification/IMP-17/test-release.log)、[Debug WPF](verification/IMP-17/wpf-debug.json)、[Release WPF](verification/IMP-17/wpf-release.json)、[浅色截图](verification/IMP-17/wpf-Release-ec12f91f66994dd8af370458374ab26b/comparison-light.png)、[深色资源切换截图](verification/IMP-17/wpf-Release-ec12f91f66994dd8af370458374ab26b/comparison-dark.png)、[WPF 复现脚本](verification/IMP-17/verify-wpf.ps1)。未生成覆盖率报告，也未进行 SQL 改写等价证明或性能校准。

## 官方依据

网页搜索连接失败后使用只读 HTTPS 获取 Microsoft Learn 正文：

- [Compare execution plans](https://learn.microsoft.com/en-us/sql/relational-databases/performance/compare-execution-plans?view=sql-server-ver17)：多语句选择、相似区域对照及数据库名称比较选项。本工具保守保留数据库身份。
- [sys.dm_exec_query_stats](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-views/sys-dm-exec-query-stats-transact-sql?view=sql-server-ver17)：QueryHash 用于逻辑相似查询，可能归并仅字面量不同的查询，不能单独证明参数或结果语义相同。DMV 指标单位不直接用于本工具的 ShowPlan 算子计数。
- [Showplan logical and physical operators reference](https://learn.microsoft.com/en-us/sql/relational-databases/showplan-logical-and-physical-operators-reference?view=sql-server-ver17)：某些实际计划算子也可能没有独立运行记录，缺失不能填零。

唯一匹配策略、置信度等级和采集条件门槛是本工具的保守设计，未声称复现 SSMS 内部匹配算法。
