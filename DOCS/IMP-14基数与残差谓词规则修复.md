# IMP-14：基数与残差谓词规则修复

实施日期：2026-09-09。当前步骤已完成；关联 R09、R10、D03、UI-01，依赖 IMP-11 的算子事实和 IMP-12 的诊断协议。真实 SQL Server 计划、SSMS 对照及完整 WPF 操作验收继续按 IMP-20/22 推进，不把本次合成夹具测试当作完整问题关闭。

## 实现与调用入口

同日完成 [审查修复与加固](IMP-14审查修复与加固说明.md)：修复独立/游离 RelOp 经引擎转换后丢失 NodeId，统一保留诊断、运行记录、合并、JSON/文本及失败结果的局部编号。新增 26 项回归，最新 Debug/Release 全量各 1684 项通过。下文验证表及原验证目录保留初版 1658 项记录。

`RowEstimateMismatchRule`、`CardinalityErrorRule`、`ResidualPredicateRule`、`ResidualPredOpRule` 均提供原生 `Evaluate(RuleAnalysisContext)`，版本为 **2.0.0**，共用 `RowCountRuleEvaluator`。引擎入口使用只读 `PlanOperatorFacts`，旧 `Analyze(XElement, XNamespace)` 也执行相同判断。`LegacyDiagnosticAdapter` 移除这四条规则的旧推断分支，其他规则的兼容适配保持原用途。

`PlanOperatorFacts` 增加 PhysicalOp 和 HasResidualPredicate：前者支持独立节点的原生诊断，后者区分“没有残差”与“残差存在但缺少可读 ScalarString”。提取器仍停止在子 RelOp、跨命名空间、InternalInfo 等边界；空白 ScalarString 不算可用谓词。支持直接 Predicate 和 IndexScan 等当前算子载荷中的 Predicate、ProbeResidual、Residual，并保留所有不同的残差/Seek 标量文本。

XML 由既有 SafeXmlHelper 解析；未新增不安全 XML 加载或执行数据库操作。

## 基数口径与阈值

定义：E 为 EstimatedRows，A 为 RowsPerExecution，D = max(1, min(E, A))，R = max(E, A) / D，Δ = abs(E - A)。D 沿用历史的 1 行下限；零值或不足 1 行时，R 是带下限的偏差指标，不是原始数学倍数。

| 规则 | 触发条件 | 严重度 |
| --- | --- | --- |
| RULE_004_ESTIMATE_MISMATCH | E ≥ 1000 且 A ≤ 100 | Critical |
| RULE_004_ESTIMATE_MISMATCH | 其余情况，max(E,A) ≥ 100 且 R ≥ 100 | Critical |
| RULE_004_ESTIMATE_MISMATCH | 其余情况，max(E,A) ≥ 100 且 R ≥ 10 | Warning |
| RULE_030_CARDINALITY_ERROR | max(E,A) > 100，R > 10 且 Δ > 1000 | Critical |

数值阈值未改动。A = 实际总输出 / 可确定的逻辑执行次数，不能把 worker 的执行次数相加作为分母，也不能拿实际总输出直接比较估算单次行数。

- 16 worker 各输出 1000、执行 1 次：总输出 16000、逻辑次数 1，与估算 16000 匹配，两条规则均 NoHit。
- 串行输出 10000、执行 100 次、估算 100：单次实际 100，两条规则均 NoHit。
- 活跃 worker 执行次数不一致、重复 Thread、线程字段不完整、协调线程非零输出等无法确定单次口径的场景：Skipped / RULE_MISSING_EVIDENCE。
- 缺少运行信息、非法/非有限估算、零执行次数：不制造实际单次行数。实际零输出且执行次数为正是有效观测，可触发过高估算诊断。

证据包括 EstimatedRows、OutputRows、LogicalExecutions、RowsPerExecution、ComparisonDenominator、DeviationRatio、AbsoluteRowDifference，以及本算子的残差/Seek 谓词。统计信息、参数分布、列相关性和表达式只是候选原因；不再从 AND 文本或偏差计数断言根因。

## 残差存在与读取放大

支持 Index Seek、Clustered Index Seek、Index Scan、Clustered Index Scan、Table Scan、Columnstore Index Scan。RULE034 不再要求存在非空 Seek ScalarString；Scan 载荷里的残差也可以用实测行数判断。其他算子为 RULE_NOT_APPLICABLE。

| 情况 | RULE006 | RULE034 |
| --- | --- | --- |
| 无本算子残差，或只有 Seek 条件 | NoHit | NoHit |
| 有残差元素但无可读 ScalarString | Skipped / 缺少证据 | Skipped / 缺少证据 |
| 有残差，运行计数缺失或不完整 | 输出残差存在的观测；不生成读放大数值 | Skipped / RULE_MISSING_EVIDENCE |
| 读取少于输出，或已知零执行却有非零读取/输出 | 保留残差存在及计数矛盾说明 | Skipped / RULE_INCONSISTENT_EVIDENCE |
| 完整且一致的读取 > 输出 × 1.2，且差值 > 100 | Warning，读取放大 | Warning，读取放大 |
| 完整且一致，但未达到上述阈值 | 通常 Info，残差存在 | NoHit |

按当前算子所有线程的完整计数计算，逐线程矛盾不能被总量掩盖。证据包含 OutputRows、RowsRead、RowsReadMinusOutput 和 ReadAmplificationRatio。输出为 0 时，倍数为 N/A，仍保留实测读取和差值；例如读取 101、输出 0 可命中，读取 100、输出 0 不命中严格差值阈值。

`PredicateFunctionEvidence` 使用 ScriptDom 核对 YEAR、SUBSTRING、ISNULL、CONVERT 的列参数，避免从字符串常量、列名、大小写或常量函数调用误判。无读取放大时，该候选提示保留历史结果编号 RULE_007_NON_SARGABLE、Warning，配置仍属于 RULE006；已确认读取放大时，函数信息作为同一诊断的补充假设。ScalarString 可能不是有效 T-SQL；解析失败或单项超过 16384 字符时只跳过函数提示并记录警告，保留残差观测。

建议要求核对搜索条件和现有索引键顺序，不再直接要求添加 INCLUDE、断言只有前导键参与 Seek，或将行数差异等同于物理 IO/耗时改善。

## 去重、配置与兼容变化

- RULE004/030 的共享语义编号为 `CARDINALITY_ROWS_PER_EXECUTION`；同时命中且位置/证据一致时合并成一条诊断，保留全部 Origins、规则版本、适用条件和补充谓词，严重度按现有合并策略取最高。
- RULE006/034 确认读取放大时共享 `RESIDUAL_READ_AMPLIFICATION` 和完整证据集合，合并规则相同。无放大时，普通残差为 `RESIDUAL_PREDICATE_PRESENT`。
- Runs 仍分别记录每次规则调用；两条 Hit 可指向同一 DiagnosticId。禁用其中一条不会禁用另一条，严重度覆盖先按各自配置生效再合并。
- 不修改全局诊断身份算法，不跨语句/算子合并。相同 NodeId 出现在不同语句时仍有不同完整位置和诊断 ID。
- 未增加可配置 RuleId，未修改 RuleConfiguration.json。原 XML 单结果入口无法表达 Skipped，调用者需要 Detailed API 获取原因。
- 告警数量可能减少；RULE006 的实测读取放大由普通 Info 提升为 Warning，RULE034 的 Scan 支持可能新增告警。RULE030 的既有 Critical 条件保留，即使同位置 RULE004 只命中 Warning，合并后也为 Critical。

## 异常、DUMP 与日志

原生规则的未知异常由 RuleEngine 执行边界记录为 Failed，保留原 RuleId、版本、异常类型和捕获结果，并继续其他规则。旧 XML 入口通过 ExceptionPolicy 捕获后重新抛出，避免把异常变成 null/NoHit。既有分析/重构入口继续阻止对 Failed 结果自动重构。

未知异常调用 UnexpectedErrorReporter，生成 Windows `process.dmp` 与 `exception.json`；默认目录 `%LOCALAPPDATA%\SqlXmlAnalyzer\dumps`，不可用时尝试 `%TEMP%\SqlXmlAnalyzer\dumps`。同一异常实例只捕获一次；DUMP 写入或校验失败时保留原错误和侧车记录，并明确输出捕获失败，不能伪称已生成。预期 I/O/输入错误不请求 DUMP；请求的取消向上传播。

| 构建 | 日志级别 |
| --- | --- |
| Debug | DEBUG、WARN、ERROR、CRITICAL（致命） |
| Release | ERROR、CRITICAL（致命） |

沿用 Logger 的编译模式约束，verbose/级别参数不能绕过 Release 限制。新增计算日志仅记录规则 ID、状态和原因码，不记录 SQL、对象名或非法字段原值。日志写文件及 stderr，CLI JSON stdout 保持可解析。

## 自动化验证与 CLI 样例

新增 **79 项**单元/集成用例：`CardinalityResidualRuleTests` 和 `CardinalityResidualDiagnosticTests`。覆盖串行/16 worker/空闲及协调线程/重复执行/顺序变化、阈值等号/零值/非法和不完整计数、本算子与子节点隔离、命名空间前缀/多标量/多语句重复 NodeId、函数误报、禁用及严重度覆盖、去重、JSON/节点/计划一致性、取消与异常处理。

每种构建均有四条真实内置规则的未知故障注入，各生成并验证真实 minidump，确认同实例去重及后续规则继续执行；另测 DUMP 写入失败保留原异常、预期错误不生成 DUMP。日志测试执行真实跳过/失败路径，并检查文件、stderr 与 stdout。测试结束清理私有临时 DUMP，不将其提交到仓库。

| 验证 | Debug | Release |
| --- | --- | --- |
| 完整解决方案构建 | 0 警告、0 错误 | 0 警告、0 错误 |
| 全量 xUnit | 1658 通过、0 失败、0 跳过 | 1658 通过、0 失败、0 跳过 |
| 本步新增用例 | 79/79 | 79/79 |
| CLI 进程 | JSON 可解析、相关诊断 4 条、日志策略通过 | JSON 可解析、相关诊断 4 条、日志策略通过 |

复现构建和测试：

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build
dotnet build SqlXmlAnalyzer.sln -c Release
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release --no-build
dotnet run --project SqlXmlAnalyzer.CLI -c Release --no-build -- scan --path SqlXmlAnalyzer.Tests/TestData/imp14_cardinality_residual.sqlplan --format json
```

合成夹具包含两个语句、各一个 NodeId=0，估算 100、实际单次 10000、读取 100000。CLI 实测相关字段摘录如下；其他默认规则结果未在此表展开：

| 语句 | 节点 | 语义 | 来源 | 严重度 |
| --- | --- | --- | --- | --- |
| 1 | 0 | CARDINALITY_ROWS_PER_EXECUTION | RULE004、RULE030 | Critical |
| 2 | 0 | CARDINALITY_ROWS_PER_EXECUTION | RULE004、RULE030 | Critical |
| 1 | 0 | RESIDUAL_READ_AMPLIFICATION | RULE006、RULE034 | Warning |
| 2 | 0 | RESIDUAL_READ_AMPLIFICATION | RULE006、RULE034 | Warning |

两种构建的 CLI 均返回 **1**，因为合成夹具命中 Critical 门禁；`Diagnostics.HasFailures=false`，没有规则执行失败。完整计数、源文件 SHA-256 和验证边界见 [validation.json](verification/IMP-14/validation.json)，[Debug 构建](verification/IMP-14/build-debug.log)、[测试](verification/IMP-14/test-debug.log)、[Release 构建](verification/IMP-14/build-release.log)、[测试](verification/IMP-14/test-release.log)及 [CLI 字段摘录](verification/IMP-14/cli-release-excerpt.json)。

## 语义依据及边界

2026-09-09 通过 Microsoft Learn 正文核对：

- [Showplan logical and physical operators reference](https://learn.microsoft.com/en-us/sql/relational-databases/showplan-logical-and-physical-operators-reference)：Scan 可带 Predicate；Seek 的 SeekPredicates 与后续 Predicate 有不同作用。因此不以 Seek 标量文本是否存在作为全部残差诊断的前提。
- [Cardinality estimation](https://learn.microsoft.com/en-us/sql/relational-databases/performance/cardinality-estimation-sql-server)：估算使用统计信息等资料，也受查询表达式和相关性等条件影响；本工具的行数差异不能单独证明原因。

网络检索工具连接失败后使用只读 HTTPS 获取上述官方页面。数值阈值及保守的 worker 逻辑次数约定属于本项目规则，不宣称是 Microsoft 定义的告警标准。新增夹具明确为合成 XML；本次没有连接 SQL Server、执行索引变更或开展完整 WPF/SSMS 人工验收。
