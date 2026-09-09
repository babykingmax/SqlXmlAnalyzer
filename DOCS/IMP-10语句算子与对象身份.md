# IMP-10：语句、算子与对象身份

实施日期：2026-09-08（Asia/Taipei）。本步完成文档层级模型、身份定位和既有服务适配；关联 D02、R14、R16、UI-02、UI-03，但不代表这些问题的全部后续功能已经关闭。

Debug / Release 完整解决方案构建均为 **0 警告、0 错误**，完整测试均为 **1290 通过、0 失败、0 跳过**。在 IMP-09 的 1247 项基础上新增 43 项身份回归，并迁移 2 项旧表名匹配测试。汇总见 [verification.json](implementation/IMP-10/verification.json)。

## 1. 层级与定位键

模型位于 [PlanDocument.cs](../SqlXmlAnalyzer.Core/Models/PlanDocument.cs)，由 [PlanDocumentBuilder](../SqlXmlAnalyzer.Core/Services/PlanDocumentBuilder.cs) 构建：

```text
PlanDocument
  PlanBatch          DocumentId + BatchOrdinal
    PlanStatement    BatchKey + StatementOrdinal
      PlanQueryPlan  StatementKey + QueryPlanOrdinal
        PlanOperator QueryPlanKey + NodeId? + OperatorOrdinal
```

全部序号从 1 开始。批次序号跨 BatchSequence 按源顺序编号；语句序号在批次内以前序遍历编号，嵌套语句另保留 Parent；QueryPlan 序号在所属语句内编号；算子序号在所属 QueryPlan 内编号。每层同时保存带命名空间的 XML 路径和原输入已有的行列位置。

支持 `StmtSimple`、`StmtCond`、`StmtCursor`、`StmtReceive`、`StmtUseDb`，包含 Then/Else、UDF/StoredProc 内的嵌套 Statements，以及游标、接收语句内的多个 QueryPlan。没有 QueryPlan 或 RelOp 的合法语句仍保留。外部命名空间及 `InternalInfo` 内容保留在源 XML 中，不当作领域语句或算子读取。

`StatementId`、`QueryHash`、`QueryPlanHash` 是可缺失或重复的元数据，不参与唯一性判定。NodeId 原值保留在源 XML；审查加固后，身份键按 Showplan `xsd:int` 保存规范整数文本。缺失、无效或重复分别增加 `PLAN_NODE_ID_MISSING` / `PLAN_NODE_ID_INVALID` / `PLAN_NODE_ID_DUPLICATE` 诊断，缺失或无效的键值为 null，OperatorOrdinal 保证节点不会覆盖。未依赖输入通过完整 XSD 校验来保证唯一性。

例如同一个输入中的两个 NodeId=1：

```text
<DocumentId>/B1/S1/Q1/N1/O2
<DocumentId>/B1/S2/Q1/N1/O2
```

NodeId 的字符串显示经过 URI 转义；消费代码应比较键对象，不从显示文字拆解身份。文档 ID 来自 IMP-09 Envelope：原始字节快照的 SHA-256 可用于同内容稳定定位；只有 XDocument 时使用该源版本的独立 ID，不伪造原始文件哈希。相同字节从不同路径读取产生相同内容身份，路径仍单独保存在 Envelope 中。

## 2. 对象身份与未知信息

`SqlObjectIdentity` 分别保存 Server、Database、Schema、Object，Alias、Index、TableReferenceId 在 `SqlObjectReference` 中独立保存。引用携带所属算子的位置及全部原始 Object 属性，未知属性不丢失。父算子只取得自身载荷中的 Object，不取得子 RelOp 的表名。

| 输入/比较情形 | 行为 |
| --- | --- |
| `[sales].[T]` 与 `[audit].[T]` | 不同 schema，分别定位 |
| dbA 与 dbB 中的同名对象 | 不同数据库，分别定位 |
| `[T.part]]x]` | 作为一个字段解码为 `T.part]x`，不按点拆分 |
| `[a]]b]` / `"a""b"` | 分别解码括号/双引号转义，并保留源属性 |
| 缺失或空名称字段 | null，显示 `?`，不从 USE、别名、文件名或默认 dbo 推断 |
| 不同大小写 | Ordinal 比较；无排序规则证据时不折叠大小写 |
| 全局对象一致性判断 | `IsSameKnownObject` 要求四段均已知且逐段相同 |

同名对象的多个源引用仍各自保留，不因对象记录相等而合并算子。对象记录的值相等只表示字段相同，不代表已经证明两个未知服务器上的对象是同一个物理对象。

缺失索引的旧节点关联增加保护：必须属于**同一文档、同一 QueryPlan**，Database/Schema/Object 已知，所有字段完全相同且只有一个候选。双方 Server 都缺失时，仅允许在该精确源 QueryPlan 内关联相同的相对引用；这不建立跨文档的物理对象等价关系。缺失其他名称段、只有 TableName、大小写差异或多候选时不猜选。完整候选集合、DDL 生成及交互归 IMP-15。

## 3. 来源有效期与兼容适配

[PlanIdentityAdapter](../SqlXmlAnalyzer.Core/Services/PlanIdentityAdapter.cs) 在旧 XDocument/XElement 入口上提供按源版本缓存的模型。IMP-09 输入服务先注册 Envelope，各服务共享模型。元数据集合只读；源码查询方法显式返回原始 XElement，不重建 XML。

同一未修改的 XDocument 经 `Recognize` / `AnalyzeDocument` 再次进入旧接口时，复用已登记的 Envelope 和模型；已有来源哈希不会因重复识别丢失，也不会在图与报告之间产生新的文档 ID。新的原始字节快照可以建立来源，只有 XML 的重识别不能替换已有来源证据。

源 XML 发生变化后，旧模型的源码定位方法抛出 `PLAN_SOURCE_CHANGED`；再次获取模型会生成新 ID、清除原 SourceHash/SourceName。旧元数据仍可作为历史快照读取，但不能拿旧键定位变更后的 XML。普通内存快照克隆保留捕获时 Envelope；克隆未提供的行号保持未知。调用方应避免并发修改正在解析或展示的 XDocument。

| 入口 | 本步接入行为 |
| --- | --- |
| `AnalysisReport.Plan`、`IAnalysisIssue` | 报告保留完整 Plan、Location、Objects；旧接口默认值维持兼容 |
| `RuleEngine.AnalyzePlan` | 旧规则在源副本执行；临时 RelOp 不污染原文档、缓存或原始位置；语句上下文不借用嵌套语句的首个 RelOp |
| 规则结果与文本报告 | Operator 结果附准确算子及自身对象；Statement 结果定位语句；Plan 结果只标文档范围，不冒称属于首条语句 |
| `PlanGraphNodeBuilderService` / 图节点 | Identity、SourceLocation、ObjectReferences 传入 ViewModel；对象标签显示分段名称；多对象不压成任意第一对象 |
| 图连线和选择 | 内部使用 SelectionKey，避免重复 NodeId 使另一语句连线同时高亮；算子枚举及父子边排除扩展内容和跨 Statements/QueryPlan 路径 |
| `PlanAnalysisService` | 多语句 SQL 全部显示并标 Batch/Statement；未选择单条语句时不运行自动重构，并显示原因 |
| `PlanSnapshot` / 比较控制器 | 内存捕获保留身份；SelectedQueryPlan 指定比较范围；多计划未选择、选择不属于当前版本时明确报错 |
| Mermaid / 文本树 | 输出所有包含算子的 QueryPlan，分别标归属；没有算子的语句/QueryPlan 仍在 Plan 模型中保留；Mermaid 使用独立图节点序号，保留完整 Source 键注释 |
| 独立 CLI read / scan | JSON 增加 Plan 层级，扫描算子/问题保留 Location、Objects；控制台和 JUnit 的现有问题明细增加位置；旧字段保留 |

CLI read 现在包含语句文本及对象名，因此输出也带原始信息隐私提示。身份日志只记录处理阶段、数量和错误，不主动记录 SQL 或对象名称。审查加固后，取消令牌贯穿 read 的身份构建，发布输出前再次检查；比较 UI 处理选择错误，使交换、载入和清空可以完成。R035 通过 ResultScope 明确全局汇总的文档范围，并在整份报告内去重。

```powershell
dotnet run --project SqlXmlAnalyzer.CLI -c Release -- read SqlXmlAnalyzer.Tests/TestData/imp10_scoped_identities.sqlplan
dotnet run --project SqlXmlAnalyzer.CLI -c Release -- --path SqlXmlAnalyzer.Tests/TestData/imp10_scoped_identities.sqlplan --format json
```

真实独立进程的 [read JSON](implementation/IMP-10/cli-read.json)、[scan JSON](implementation/IMP-10/cli-scan.json) 和[退出码](implementation/IMP-10/cli-exit.json)已保存，read、scan 均退出 0。样例包含 2 批次、4 语句、4 QueryPlan、6 算子。

## 4. 异常与日志

身份构建边界使用既有 `ExceptionPolicy` / `UnexpectedErrorReporter`。未知异常捕获 Windows minidump，经 `MinidumpValidator` 校验并写异常侧车；同一异常去重。DUMP 组件失败时保留主错误并明确说明 DUMP 失败，不返回成功。取消和 `InvalidDataException`（源变更、无效身份或选择）属于预期失败，不生成 DUMP；本轮修复了 `InvalidDataException` 未被既有策略覆盖的遗漏。

Debug 固定输出 DEBUG / WARN / ERROR / CRITICAL；Release 固定只输出 ERROR / CRITICAL。INFO/Verbose 兼容调用归入 DEBUG，不能在 Release 绕过阈值。构建入口记录开始、层级计数及 NodeId 消歧告警。分析引擎将取消、无效身份和未知错误分别映射为 Cancelled、Invalid、UnexpectedError。

`PlanIdentityModelTests.UnknownModelFailure_ProducesValidatedDumpAndBuildAppropriateLogs` 在两种配置真实注入模型构建异常，验证 `.dmp` 的格式、异常侧车、ERROR/CRITICAL 以及 DEBUG/WARN 的模式差异，并断言日志未泄露样例 SQL。DUMP/侧车只留在隔离临时目录，测试结束清理；仓库保留通过记录，不保存进程内存。

## 5. 回归与迁移差异

新增 [模型测试](../SqlXmlAnalyzer.Tests/PlanIdentityModelTests.cs) 25 项、[链路测试](../SqlXmlAnalyzer.Tests/PlanIdentityFlowTests.cs) 18 项，覆盖层级、缺失/重复 ID、嵌套语句、前缀命名空间、对象所有权/转义/未知/大小写、源修改与取消、真实 DUMP、诊断组件失败、规则和 GUI/CLI 归属、连线、快照、显式比较选择和多语句重构保护。

初版曾对 8 份既有输入逐份比较 IMP-09 构建和 IMP-10 初版的 `RuleId/Severity/Title/Message/NodeId`，当时全部相同。比较使用同一仓库 RuleConfiguration，并加载 ScriptDom，避免缺依赖导致规则被跳过。见[基线](implementation/IMP-10/rule-baseline-complete.json)、[初版输出](implementation/IMP-10/rule-current-complete.json)、[初版差异记录](implementation/IMP-10/rule-output-differences.json)、[采集脚本](implementation/IMP-10/Measure-RuleIdentityParity.ps1)和[初版比较脚本](implementation/IMP-10/Compare-RuleIdentityParity.ps1)。这些是历史证据；审查修复将 R035 全局汇总 NodeId 改为空并去重，当前差异及新增 22 项回归见[审查修复与加固说明](IMP-10审查修复与加固说明.md)。较早未显式加载依赖的探针记录不作为验收依据。不能在当前 DLL 上重跑并冒充旧基线。

有意改变的行为包括：表名宽松匹配改为精确来源关联、模糊多计划比较报错、多语句不自动改写、全部语句/计划可见、嵌套语句与扩展节点隔离。相关测试证明新行为，不能以“规则字段一致”推导这些入口输出完全不变。旧规则仍基于 XML 执行；Statement 规则当前适配 StmtSimple，完整规则迁移归 IMP-12。

初版记录：[Debug 构建](implementation/IMP-10/build-debug.log)、[Release 构建](implementation/IMP-10/build-release.log)、[Debug TRX](implementation/IMP-10/debug-verified.trx)、[Release TRX](implementation/IMP-10/release-verified.trx)。初版使用隔离的 `IntermediateOutputPath=obj/imp10-final-identity/<Configuration>/` 避开本机已有 WPF 中间资源锁；没有删除被占用产物。审查修复后两种配置全量各 1312 项通过，最新构建、测试和源哈希见[修复验证](implementation/IMP-10-review/verification.json)。常规命令仍为 `dotnet build SqlXmlAnalyzer.sln` 和 `dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj`。

下图是 Release 程序实际节点 DataTemplate 在 STA 上渲染的对象标签：父 NestedLoops 不借用子表名，两条 NodeId=1 分别保留 sales.T 与 audit.T。见[渲染脚本](implementation/IMP-10/Run-IdentityUiProbe.ps1)、[绑定观测](implementation/IMP-10/ui-observed.json)、[进程结果](implementation/IMP-10/ui-process.json)。这是实际控件模板验证，不是完整窗口交互验收。

![执行计划节点对象归属](implementation/IMP-10/operator-identities.png)

## 6. 依据与后续范围

按 Microsoft 优先策略核对：

- [Database identifiers](https://learn.microsoft.com/en-us/sql/relational-databases/databases/database-identifiers?view=sql-server-ver16)：标识符的大小写规则依赖所属范围的排序规则，因此未知排序规则下不能统一忽略大小写。
- [Showplan XML schema](https://schemas.microsoft.com/sqlserver/2004/07/showplan/) 与[仓库 Microsoft SQL Server 2019 XSD](../SqlXmlAnalyzer.Tests/TestData/Schemas/showplanxml-sql2019.xsd)：核对 StmtBlockType、StatementId/QueryHash、游标/接收 Operation/QueryPlan、RelOp NodeId、ObjectType 的独立名称段和 Alias。可选元数据及多层作用域是设计依据。
- [Showplan logical and physical operators reference](https://learn.microsoft.com/en-us/sql/relational-databases/showplan-logical-and-physical-operators-reference?view=sql-server-ver16)：对象与物理算子按各自来源保留，不从显示字符串猜测实体。

新夹具是用于边界测试的合成最小识别输入，包含故意重复元数据及未知扩展；不声称它通过完整 XSD 或来自真实生产捕获。本步未连接 SQL Server/SSMS，也未测量生产容量。

IMP-11 继续统一指标/谓词/线程事实，IMP-12 继续完整诊断范围和证据协议，IMP-15 继续完整缺失索引工作流，IMP-17 继续跨计划语义匹配。当前比较子节点仍按既有顺序对齐，完整 QueryPlan 选择界面和身份持久化由 IMP-20/21 完成；GUI 直接比较多计划快照会收到需要选择的错误，API 可通过 SelectedQueryPlan 指定。图布局成本汇总、会话总成本等旧指标口径也不因本步身份模型而自动修正。
