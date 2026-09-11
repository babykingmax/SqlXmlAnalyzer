# IMP-12：有证据和运行状态的诊断协议

实施日期：2026-09-09。范围：执行计划规则、Analysis、GUI、两套 CLI 入口及文本 / HTML / JSON / JUnit 报告。依赖 IMP-10 的完整身份及 IMP-11 的算子事实。死锁协议、更多规则的领域修正及完整界面验收仍分别按 IMP-13/14/20/22 推进。

同日完成[审查修复与加固](./IMP-12审查修复与加固说明.md)：恢复四个旧结果分支编号，分开节点运行状态与性能警告，修正串行证据状态，统一默认 PreservedSource 能力。结果 RuleId 与实际执行来源可以不同，配置仍归属 Origins / RuleRun 的执行规则。

随后完成 [IMP-09 至 IMP-13 联合审查修复](IMP-09至13联合审查修复与加固说明.md)：默认计划/节点诊断与读取入口共享 `PlanCapabilityService`。空 Statements 保留 PlanStatements，InternalInfo 内的同名算子及错误位置的运行计数不再虚增能力；同一 XML 根按变更失效缓存，显式传入的能力限制继续生效。最新 Debug/Release 全量测试各 1579 项通过，下文保留该阶段历史记录。

## 交付与调用

`RuleEngine.AnalyzePlanDetailed`、`AnalyzeNodeDetailed` 和 `PlanDiagnosticAnalyzer.AnalyzeDetailed` 返回 `PlanDiagnosticReport`。`IPlanAnalyzerRule.Evaluate(RuleAnalysisContext)` 是新规则入口；旧 `Analyze(XElement, XNamespace)` 通过默认接口实现接入，原 RuleId 和配置字段继续有效。

```csharp
var report = PlanDiagnosticAnalyzer.AnalyzeDetailed(document, ns,
    cancellationToken: cancellationToken);
string text = DiagnosticTextFormatter.Format(report);
// 输入读取成功与规则执行成功分别判断；Failed 不能当作 NoHit。
bool requiresAttention = report.HasFailures || report.HasMissingEvidence;
```

源码入口：`SqlXmlAnalyzer.Core/Rules/DiagnosticProtocol.cs`、`RuleEngine.Execution.cs`、`LegacyDiagnosticAdapter.cs`、`DiagnosticTextFormatter.cs`。

## 协议 v1.0

后续 [IMP-15](IMP-15索引目标身份与DDL标识符.md) 将 RULE035 改为版本 2.0.0 的 Statement 原生规则，保留每条候选的目标/列/来源证据和未解析状态；隔离 XML 副本登记原 Envelope，避免源身份变化。RULE020 保留计划汇总范围，版本为 2.0.0。

2026-09-09 的 [IMP-14 审查加固](IMP-14审查修复与加固说明.md) 为 PlanDiagnostic 和 RuleRun 增加可空 NodeId。该值保存局部编号，完整定位仍使用 Location；游离节点不会因此获得伪造的完整身份。RuleRun 的位置构造/解构签名保持原样；计划/语句调用的 NodeId 为空。消费者须允许新增 JSON 字段。

| 对象 | 内容与约束 |
| --- | --- |
| PlanDiagnosticReport | 协议版本、文档身份、引擎/Schema 版本、能力、配置快照、全部调用记录、去重后的诊断 |
| PlanDiagnostic | DiagnosticId、SemanticCode、规则版本、完整位置、严重度、置信度、证据、假设、建议、适用条件、限制及所有来源 |
| DiagnosticEvidence | 名称、值、来源、位置，以及适用时的单位、状态、指标类型、聚合口径；数字用不依赖区域设置的字符串保存 |
| DiagnosticOrigin | 原始 RuleId、规则版本/元数据、RunId、调用位置；合并不丢弃来源 |
| RuleRun | 规则和版本、调用位置、状态、原因码/原因、耗时、DiagnosticIds；失败另含异常类型及 DUMP / sidecar 结果 |
| PlanAnalysisContext | 只读模型值、复制后的规则配置、输入能力、版本、取消令牌；不向新规则暴露可修改 XML |

位置沿用 Document / Batch / Statement / QueryPlan / Operator 的完整键及 XML 来源位置。相同 NodeId 在不同语句或同一 QueryPlan 的不同算子上不会合并。未声明的引擎/Schema 版本保留 null，不猜测 SQL Server 版本。现有内置规则初始协议版本统一为 `1.0.0`。

命中必须有非空 SemanticCode 和本输入的可定位证据。无证据、外部文档证据、非法严重度/置信度/范围、不存在的结果位置或不合法的返回状态都记为 Failed；整个返回批次验证完成后才发布，防止出现部分成功的伪结果。新规则应在 `RuleMetadataCatalog` 注册稳定 `RULE_*` 标识并由引擎注册。

## 四种运行状态

| 状态 | 含义 | 典型原因码 |
| --- | --- | --- |
| Hit | 已执行且返回至少一条诊断 | RULE_HIT |
| NoHit | 已执行，未满足当前规则实现的触发条件 | RULE_NO_HIT |
| Skipped | 禁用、不适用、无对应范围或缺少所需证据 | RULE_DISABLED、RULE_NOT_APPLICABLE、RULE_NO_CONTEXT、RULE_MISSING_EVIDENCE |
| Failed | 本次规则执行或协议校验失败；保留规则身份及原因，继续其他调用 | RULE_EXECUTION_FAILED |

节点入口还记录 `RULE_OUTSIDE_SCOPE`：计划/语句规则由其范围的首个算子执行；节点提示展示计数，完整记录保留在节点 Diagnostics 中。计划入口的语句范围包括模型识别的所有语句；旧语句规则仅支持 StmtSimple，其余显式跳过。

`HitCount` 等是**调用次数**，不是不同 RuleId 数或去重诊断数。一条规则可对多个算子运行；多个 Hit 也可指向同一个 DiagnosticId。`RunId` 是按规则版本与调用位置计算的关联键，同一位置重新分析可得到相同键，每份报告各自保留当次状态和耗时。

真实取消通过传入令牌传播，不生成 DUMP；后续调用停止。Analysis 和 CLI 转为既有 `INPUT_CANCELLED` / 130 路径。规则自行抛出、但调用令牌未取消的 OperationCanceledException 只使该规则 Failed，不伪造整次用户取消。

## 事实与推断分开

首批逐项结构化迁移：RULE_004_ESTIMATE_MISMATCH、RULE_030_CARDINALITY_ERROR、RULE_016_ZERO_ROW_ACTUALS、RULE_034_RESIDUAL_PRED_OP。

- 行数诊断使用 IMP-11 的 EstimatedRows / OutputRows / LogicalExecutions / RowsPerExecution；残差读取诊断使用当前算子的读取行、输出行与残差谓词。置信度 High 表示对这些观测事实的确信程度，不代表已确定根因。
- “可能与统计信息、参数或过滤条件有关”属于假设；核对条件与验证方案属于建议；不从这些计数直接断定统计信息过时、连接算法错误或新索引必然有效。
- 上述行数规则，以及线程倾斜、并行倾斜、循环执行和残差比较的相关前置检查，在证据不完整时返回 Skipped。线程/并行倾斜首先将明确串行算子标为 RULE_NOT_APPLICABLE；合法零值不当成缺失。
- 其余旧规则保留 XML 调用范围证据和 `LegacyExplanation`，置信度为 Unspecified，并明确标记兼容说明中的根因/建议尚未逐项结构化核验。尚未把所有旧规则的 null 细分成不同缺证据原因；其 NoHit 仅表示该旧实现执行后没有返回匹配项。

这一步建立协议与渐进适配，不宣称所有规则推理已完成校正。R035 的 SQL 语法分析失败记录为缺少证据；缓存保留失败原因，不能在第二个算子上变成 NoHit，修改语句后缓存失效并重新分析。

## 去重与不可变性

诊断键由语义编号、有效位置和规范化证据集合计算。证据的名称、值、单位、状态、来源及位置均影响身份；证据顺序、重复项及显示标题不影响身份。相同诊断合并保留所有规则/调用来源、全部假设/建议/条件/限制；严重度取最高、置信度取最低。不同语义或不同证据保持独立。

IMP-12 初版内置兼容适配的语义编号包含 RuleId，没有合并 RULE004/RULE030。2026-09-09 的 [IMP-14](./IMP-14基数与残差谓词规则修复.md)已将 RULE004/030、确认读放大的 RULE006/034 分别迁移到共享原生语义和完整证据，按现有身份算法合并并保留全部来源。其他兼容规则仍保留各自语义编号。文件输入沿用 IMP-09/10 的文档身份；独立构造、未注册读取来源的内存文档使用本次文档身份，不保证跨进程重建后得到同一个编号。

配置在引擎加载时复制，外部修改 `ConfigurationLoadResult` 不影响已加载引擎。发布时再次复制诊断集合。新上下文复用只读模型；旧规则在隔离的计划工作副本上执行，并以 XML Changing 事件阻止修改；节点入口同样阻止旧规则修改源 XML。外部文档的身份模型会被拒绝。旧 XML 接口保留给适配使用，不是新规则的数据访问契约。

## 入口与兼容性变化

- GUI 全局分析保存 `CurrentPlanDiagnostics`；节点保存 Diagnostics，共同格式器生成证据和独立的 `DiagnosticStatusText`，运行摘要和跳过状态不再触发性能警告图标。HTML 导出优先复用当前 GUI 分析记录，拒绝不同文档版本的记录；便携报告沿用同一诊断文本。
- AnalysisReport 分开保留 InputStatus 与 Diagnostics；输入仍为 Success 时规则可以 Failed。`AnalysisErrorCode=RULE_EXECUTION_FAILED`，不会改写为 XML 解析错误。任一 Failed 阻止 ApplicationOrchestrator 与 GUI 自动重构/写回。
- CLI scan JSON 每个文件新增 `Diagnostics`。保留原 Issues 作为兼容字段，命中携带 Diagnostic、执行失败携带 Run。失败记录保留原 RuleId，以 Critical 兼容项提示旧消费者；严重度覆盖为 Info 也不能隐藏执行失败。CLI refactor 的失败 JSON 和正常结果 JSON 同样保留协议记录。
- console 与桌面 CLI 文本显示协议；JUnit 的 system-out 含证据和运行记录，规则失败类型为 RuleExecutionFailure，特殊 XML 文本经过转义。任一 Failed 导致 scan 非零退出。桌面 `--analyze` 也会返回失败，而非显示“成功”。
- **scan 的 Passed/退出 0 仍表示当前门禁未失败，不是完整健康认证。** 缺少运行信息的估算计划可以门禁通过，同时带有 Skipped / HasMissingEvidence；CI 若要求实际运行数据，须另检查该字段。Disabled 和不适用不会自动使门禁失败。
- RuleId、旧配置格式、严重度配置及内置阈值不变。`RegisteredRules` 现在包含已禁用规则，以便生成可见的 Skipped 记录。旧 AnalyzePlan/AnalyzeNode 列表不包含 Skipped，需迁移到 Detailed API 才能取得全部运行记录。文本去掉“完美通过 / 完全健康”表述，原文案断言随协议调整。

## 异常、DUMP 与日志

34 个旧规则文件移除吞异常后返回 null 的处理；规则引擎统一捕获失败并继续其他规则。不可恢复异常由既有 `UnexpectedErrorReporter` 写入真实 Windows minidump 和 `exception.json`，sidecar 包含 RuleId、规则版本、RunId 与托管异常堆栈。同一异常对象在同一 reporter 中只捕获一次。DUMP 路径默认在 `%LOCALAPPDATA%/SqlXmlAnalyzer/dumps`，失败时尝试临时目录；写入、验证或诊断组件本身失败时，原规则仍为 Failed，明确显示 DUMP 失败原因。

预期的取消、输入或 I/O 错误不生成 DUMP，但规则内部预期错误仍保留 Failed 原因。沿用专用 DUMP 线程、进程级 DbgHelp 串行保护和超时边界。本步没有改变捕获机制为外部进程；进程已损坏、资源耗尽等情况下无法保证完成 DUMP，必须检查 FailureDiagnostic 的实际结果。

Debug 输出 DEBUG / WARN / ERROR / CRITICAL，Release 只输出 ERROR / CRITICAL（CRITICAL 对应致命信息）。开始/结束为 Debug，跳过为 Warning，失败为 Error，未知错误捕获为 Critical。正常规则状态日志只记录标识、状态和原因码，不附加 SQL、谓词或对象名；异常堆栈及原始报告继续适用既有隐私边界。

## 验证与资料

初版新增 45 项回归，审查修复另增 47 项。当前完整 Debug / Release 构建均为 0 警告、0 错误，完整测试各 1481 项通过、0 失败、0 跳过。测试计数、源码与 Core DLL 哈希见 [verification.json](./implementation/IMP-12/verification.json)，日志及 TRX 同目录保留；初版 1434 项结果保留为[历史快照](./implementation/IMP-12/hardening/before-verification.json)。复现：

```powershell
.\DOCS\implementation\IMP-12\Verify-IMP12.ps1
```

新增 `DiagnosticProtocolTests`、`DiagnosticProtocolFlowTests`，覆盖四状态、配置冻结/禁用/严重度、完整位置、语义去重及来源、不可变性、非法协议批次回滚、取消、真实 DUMP/失败捕获/分模式日志、GUI/CLI/HTML/JSON 一致性、JUnit 转义及失败阻止重构。原有规则行为和配置兼容测试也纳入完整测试。没有连接 SQL Server 执行查询，没有生成覆盖率报告，也未把节点/绑定测试称作完整窗口可视验收。

初版 CLI 实例见 [cli-verification.json](./implementation/IMP-12/cli-verification.json)：既有 `imp11_operator_facts.sqlplan` 产生 5 次 Hit / 39 次 NoHit / 7 次 Skipped / 0 次 Failed，门禁 Passed；`plan_no_relop.sqlplan` 显式保留 21 次 Skipped，门禁 Passed。初版 JSON 与 JUnit 输出分别为 [actual-plan.json](./implementation/IMP-12/actual-plan.json)、[no-operator-plan.json](./implementation/IMP-12/no-operator-plan.json)、[actual-plan.junit.xml](./implementation/IMP-12/actual-plan.junit.xml)。本轮 Release CLI 的[串行与低效并行实测记录](./implementation/IMP-12/hardening/smoke-verification.json)验证结果分支保留、HasMissingEvidence 无串行误报及 JUnit 内容。规则 Failed、严重度覆盖及非零失败路径通过注入异常的入口回归核验，不把性能阈值失败冒充未知异常。

检索遵循 Microsoft 官方文档优先；本步所需平台语义已有官方依据，无须追加社区或 GitHub 实现来替代依据。2026-09-09 三个链接均 HTTP 200；网页搜索连接失败后通过 HTTPS 读取官方正文：

1. [显示实际执行计划](https://learn.microsoft.com/en-us/sql/relational-databases/performance/display-an-actual-execution-plan?view=sql-server-ver17)：实际计划可提供运行指标和警告，支持把运行证据与缺失证据分开。
2. [.NET 异常最佳实践](https://learn.microsoft.com/en-us/dotnet/standard/exceptions/best-practices-for-exceptions)：无法在当前层恢复的异常交由上层处理，并保留取消与异常传播的语义；本项目在规则执行边界转换为可检查的 Failed 记录。
3. [MiniDumpWriteDump](https://learn.microsoft.com/en-us/windows/win32/api/minidumpapiset/nf-minidumpapiset-minidumpwritedump)：官方建议优先外部进程；进程内使用专用线程并同步 DbgHelp 调用。现有写入器沿用后者及失败反馈。

四状态枚举、语义编号与去重策略属于本项目设计，并非 Microsoft 定义的 SQL Server 协议。
