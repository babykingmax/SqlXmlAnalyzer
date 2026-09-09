# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### IMP-0 到 IMP-19 增强（2026-09-09）

- 汇总阶段 0 基线、反例矩阵、输入与输出保护、统一事实及诊断协议、死锁图、规则与索引模拟、多语句 A/B 比较、可审核 SQL 提案和语义验证后可靠应用；具体范围与限制见 [实施规划](DOCS/软件改善实施规划.md)。
- 提交前从暂存文件导出独立目录验证，Debug/Release 构建各 0 警告、0 错误，测试各 2229 项全部通过，见 [发布验证摘要](DOCS/verification/IMP-0-19-publish/publish-validation.json)。保留可复现附件及原始字节；原始 TRX、日志、DUMP、备份和构建产物不纳入 Git，历次测试计数和摘要另行归档。

### IMP-18 / IMP-19 联合审查修复（2026-09-09）

- 无损保留 SQL 返回的未配对 UTF-16 码元；读取任何行前拒绝不能完整比较内部属性的 sql_variant，包括空结果、全 NULL 和对象快照。
- SQL 字节/字符预算前移至 AST 和规则执行之前；应用复查及写回校验同样有界。建库确认失败或取消后仍以独立连接清理本轮库，失败保留诊断。
- WPF 顶部、预览和详情同步显示真实提交结果及备份。新增 47 项回归，Debug/Release 全量各 2229 项通过，完整构建各 0 警告/错误。见 [修复说明及证据](DOCS/IMP-18-19联合审查修复与加固说明.md)。

### IMP-19 审查修复与加固（2026-09-09）

- 修复观察器可通过 DELETE/UPDATE 等写操作擦除真实数据差异的问题；ObserveSql 独立限制为只读 SELECT，阻断 SELECT INTO、嵌入 DML、赋值、事务和控制语句，验证/应用入口在数据库工作前检查。
- 将只读观察器移到内部状态查询之前，保留受测 SQL 的 ROWCOUNT/ERROR；相同错误不能作为可应用证据。修正跨批次 RecordsAffected=-1 的哨兵值累计，保留“不适用”与零行 DML 的区别，非法/溢出计数明确失败。
- 新增 41 项回归；Debug/Release 各 2182 测试通过，构建各 0 警告/错误。SQL Server 2019/2025 每配置新增 26 项场景及真实应用阻断检查通过，未知异常 DUMP 和分模式日志回归通过。见 [修复、兼容性与验证记录](DOCS/IMP-19审查修复与加固说明.md)。

### IMP-19 SQL 语义验证与可靠应用（2026-09-09）

- 新增专用 LocalDB 原 SQL/候选 SQL 比较，记录版本、兼容级别、排序规则、固定会话设置、结果/重复行/类型、错误、事务及对象观测；每侧独立新库、无登录用户、不可恢复身份、有界流读取和清理失败阻断。
- 新增 `semantic-compare`、`rewrite-validate`、`rewrite-apply`；桌面接入验证、场景审核确认及带备份应用。应用绑定源字节、提案版本/选择/预览和场景，使用一次性进程内凭据与既有可靠写回服务；保留提交不确定及报告输出失败后的实际写入状态。`refactor` 仍只输出提案。
- 新增 58 项回归；Debug/Release 各 2141 测试通过、构建各 0 警告/错误，两版本 SQL Server 每配置 42/42 场景回归通过，CLI 与 WPF 实际验证/写回/备份及四张截图通过。未知异常 DUMP、日志模式和失败降级已测试。场景通过不宣称一般等价或性能改善，TRIM/表变量转换维持限制。见 [实现、使用与证据](DOCS/IMP-19SQL语义验证与可靠应用.md)。

### IMP-18 审查修复与加固（2026-09-09）

- 修复终端/文本文件输出完整候选而忽略选择的问题，统一展示 `Review.PreviewSql` 并保留原始空白与注释；更新 `--show-sql` 帮助。
- 补齐 SELECT INTO 创建目标检查，加固完整标识符、大小写、DROP IF EXISTS、批次与 IF/WHILE/TRY-CATCH 分支；明确完整对象定义与生命周期尚未证明。
- 新增 36 项回归，Debug/Release 完整构建各 0 警告/错误，全量测试各 2083 通过、0 失败、0 跳过。保留真实 DUMP、诊断失败和分模式日志验证。见 [检索依据与验收记录](DOCS/IMP-18审查修复与加固说明.md)。

### IMP-18 SQL 改写提案（2026-09-09）

- 引入源/步骤 hash、SQL diff、规则版本、前提/风险/证据、验证性质、依赖和选择状态；未证明等价的候选默认未选择。
- CLI `refactor` 停止自动写回，新增可重复 `--select <ID>` 仅供审核预览；GUI/快速修复共用审核窗口，选择后重验组合，取消全部选择恢复原文。
- 建立批次符号与临时名称预留，限制未知作用域和清理归属；旧表变量访问器不能绕过提案安全边界。
- 新增 35 项回归，Debug/Release 各 2047 项通过、构建各 0 警告/错误；包含真实 DUMP 和分模式日志验证。截图受当前桌面限制未完成，数据库等价性与可靠应用留待 IMP-19。见 [交付说明](DOCS/IMP-18可审核SQL改写提案.md)。

### Fixed

- IMP-17 evidence hardening: fingerprint BuildResidual separately from ProbeResidual, include ordered Sort/TopSort semantics, and reject incomplete object identities even for manual comparisons. Propagate incomplete evidence through ancestor and QueryPlan matching; preserve valid local and object-free plans. Add 55 regressions; Debug/Release each pass 2012 tests with zero build warnings/errors, including native DUMP and logging policy checks. See [behavior and verification](DOCS/IMP-17比较证据完整性加固.md).

- IMP-17 review: include ordered range columns, comparison bounds and typed predicate expressions in operator identity; keep incomplete evidence at candidate confidence without numeric operator deltas. Separate A/B scope selection from shared snapshots and roundtrip it in session 2.1. Retain recorded source hashes with XML revision binding, reject changed current-format content, and label unverifiable legacy hashes as historical metadata. Add 29 regressions; Debug/Release each pass 1957 tests with zero build warnings/errors. See [fixes, compatibility and evidence](DOCS/IMP-17审查修复与加固说明.md).

- IMP-17 verification probe: catch screenshot/result I/O failures at the process entry point, preserve existing artifacts with create-only writes, and return explicit failure codes instead of leaking managed exceptions. Capture unknown failures as native DUMPs with independent diagnostics; verify exclusive file locks and diagnostic sink failures in real child processes. See [probe fix and evidence](DOCS/IMP-17验证探针异常处理修复.md).

- IMP-17: compare all captured statements before matching QueryPlans and operators, using unique SQL/object/structure evidence with explicit candidate, manual and unmatched states. Gate nullable metric deltas on captured versions, SET options, parameters and execution basis; retain absolute changes with a zero baseline and remove ungated total-cost improvement claims. Add manual scope selectors, paired statement trees, source metadata and session selection roundtrips. Add 52 regressions; Debug/Release each pass 1928 tests with clean builds, native DUMP/log checks and production WPF binding/screenshot validation. See [comparison contract and evidence](DOCS/IMP-17多语句与可比性检查的AB比较.md).

- IMP-16 review: recognize parenthesized and signed numeric values and structured NULL predicates; read only independent AND conjuncts so CASE branches cannot earn predicate points. Require local evidence for quoted parameter names and reject malformed or foreign scalar shapes. Version the score model at 2.0.1 and RULE020/035 at 2.1.1. Add 60 regressions; Debug/Release each pass 1876 tests with clean builds, real DUMP/logging and CLI checks. Eight native SQL Server plan cases and the production WPF layout/binding probe pass; converted ConstExpr values remain explicitly unsupported. See [hardening and validation](DOCS/IMP-16审查修复与加固说明.md).

- IMP-16: compute the complete captured own-cost denominator before evaluating index candidates; union overlapping operators without stacking subtree costs or predicted gains. Publish model versions, evidence, weights and input assumptions; separate captured SQL Server Impact from tool scores and retain unknown forecasts. Missing coverage no longer earns 40 points, and predicate text uses syntax parsing. Add 50 regressions; Debug/Release each pass 1816 tests with clean builds, real DUMP/logging and CLI checks. Full WPF layout/bindings pass; off-screen bitmap capture remains unavailable. See [implementation and validation](DOCS/IMP-16模拟顺序依赖与模型限制.md).

- IMP-15 review: require entity ownership before offering plan columns for index DDL; retain raw ColumnReference names, including literal brackets. Resolve Sort independently within its captured QueryPlan, validate every order column and direction, and keep multiple orders separate. Add 46 regressions and a captured SQL Server plan; Debug/Release each pass 1766 tests with clean builds, native DUMP/logging and CLI checks. Real LocalDB CREATE/rollback and entity/Sort validation pass. See [hardening evidence](DOCS/IMP-15审查修复与加固说明.md).

- IMP-15: quote identifier parts without splitting embedded dots; retain database/server context, validate options and generate one deterministic SHA-256 based name for create/rollback scripts. Scope index extraction, SQL binding, scoring and sandbox evidence to captured object/QueryPlan identities; publish RULE035 candidates once per statement and retain source identities in isolated rule XML. Prevent deployment-comment breakout, reject unresolved/ambiguous targets, and retain explicit schema-validation limits. Add 36 regressions; Debug/Release each pass 1720 tests with clean builds, DUMP/log/CLI checks and five real LocalDB target/key/include/rollback/server-guard checks. See [implementation and evidence](DOCS/IMP-15索引目标身份与DDL标识符.md).

- IMP-14 review fix: preserve standalone/detached RelOp NodeId in native diagnostics, merged findings, all run outcomes, legacy results and JSON/text reports. Capture the source identifier before evaluation; keep invocation/diagnostic scopes separate and full Location identity authoritative. Add 26 regressions; Debug/Release each pass 1684 tests with zero build warnings/errors, including real DUMPs, logging and CLI checks. See [hardening and verification](DOCS/IMP-14审查修复与加固说明.md).

- IMP-14: use one native evaluation contract for RULE004/030 cardinality and RULE006/034 residual checks, with shared per-execution measurements, explicit denominators and semantic deduplication retaining all origins/predicates. Support local Scan/Seek residuals without requiring Seek ScalarString; distinguish missing/inconsistent counters and undefined zero-output ratios. Replace asserted root causes and function-keyword matches with bounded ScriptDom column-argument hints. Keep IDs/configuration/numeric thresholds, version these rules at 2.0.0; measured RULE006 read amplification is Warning and RULE034 now covers scans. Add 79 regressions; Debug/Release each pass 1658 tests with zero build warnings/errors, validated native DUMPs, capture-failure handling, build-specific logging and CLI process checks. See [implementation and evidence](DOCS/IMP-14基数与残差谓词规则修复.md).

- IMP-09–13 joint review fixes: share ownership-aware plan capabilities across input and default diagnostic entries, including empty Statements and opaque extensions; intersect offset deadlock lines with node boundaries and reject non-finite geometry; release retained inputs and source snapshots when clearing results. Add 38 regressions; Debug/Release each pass all 1579 tests with zero build warnings/errors, including native DUMP, log filtering, CLI output, input lifetime and production WPF rendering. See [fixes and verification](DOCS/IMP-09至13联合审查修复与加固说明.md).
- IMP-13 review fixes: replace quadratic victim validation with indexed membership; carry EvidenceId through WPF drawing, drag, playback and badge caches, with bounded parallel offsets and indexed event projection. Build Range evidence from typed mode fields, including dangling raw relations, and restore Mermaid index labels. Preserve cancellation through safe XML reads. Add 17 regressions; Debug/Release builds have zero warnings/errors and each full suite passes 1541 tests, including native DUMP, log filtering and production WPF rendering. See [hardening and evidence](DOCS/IMP-13审查修复与加固说明.md).
- IMP-13: share parsed deadlock facts, resource/link identities, the complete victim set and iterative SCC membership across graph, dependency playback, diagnostics and reports. Normalize explanatory cycles while preserving distinct resource edges; bound path count, length and search work with visible truncation. Require observed Range modes, attach structured evidence and qualify root-cause hypotheses. Preserve cycle/victim styling after playback and reuse completed GUI analysis for HTML export. Add 43 regressions including a 10,000-node graph, real DUMP, log filtering and production WPF component rendering. Debug/Release builds have zero warnings/errors; each full suite passes 1524 tests. See [implementation and verification](DOCS/IMP-13统一死锁图环与诊断依据.md).
- IMP-12: introduce a versioned diagnostic protocol with immutable analysis context, evidence, confidence, hypotheses, recommendations, limitations, complete locations and semantic deduplication retaining every origin. Record Hit/NoHit/Skipped/Failed for rule invocations; stop swallowing rule failures, preserve DUMP results, and block refactoring on Failed. GUI, both CLI entries, JSON/HTML/text/JUnit reports share the protocol. The initial implementation added 45 regressions. See [contract, compatibility and verification](DOCS/IMP-12有证据和运行状态的诊断协议.md).
- IMP-12 review fixes: preserve the four existing result-ID branches while validating their owning implementation; retain original configuration ownership and matching versions when merging. Separate node execution status from performance warnings, mark serial thread-skew checks as not applicable, and include PreservedSource in default capability inference. Add 47 regressions, including production WPF tooltip rendering; Debug/Release builds have zero warnings/errors and each full suite passes 1481 tests, including real DUMP and log-filter checks. See [hardening and evidence](DOCS/IMP-12审查修复与加固说明.md).
- IMP-11 review: accept signed xsd:int thread IDs with invariant parsing and XML whitespace; normalize equivalent spellings before duplicate detection, retain raw values and abstain from negative thread roles. Add 33 regressions including schema-valid cross-output checks and safe thread diagnostic logging. Full Debug/Release builds have zero warnings/errors and each suite passes 1389 tests. See [hardening evidence](DOCS/IMP-11审查修复与加固说明.md).
- IMP-11: centralize operator-local facts and metric aggregation across rules, graph/table/details, comparison runtime metrics and CLI JSON. Stop child RelOp objects, predicates and scalar conversions leaking into parent facts; preserve unknown versus zero and exact thread counts. Use a common execution denominator for cardinality, worker-only skew, and shared own/subtree costs. Missing evidence can suppress previous diagnostics; no rule IDs or configured thresholds change. Add 44 regressions including real DUMP, failure handling and Debug/Release logging; both full suites pass 1356 tests. See [metric contract and evidence](DOCS/IMP-11集中算子事实与指标口径.md).
- IMP-10 review: bind R035 global summaries to document scope and deduplicate them while retaining operator suggestions; contain comparison failures at the UI boundary so swap/load/clear finish; propagate CLI read cancellation through identity construction and check before output; normalize RelOp NodeId as xsd:int to bound repeated parent keys. Add 22 regressions; Debug and Release each pass 1312 tests with validated native DUMP and logging. See [hardening evidence](DOCS/IMP-10审查修复与加固说明.md).
- IMP-10: prevent repeated NodeId values from sharing graph selection, stop parent operators borrowing child objects, preserve nested statement ownership, and require exact source/object identity for missing-index association. Multi-query comparison requires explicit selection; multi-statement analysis preserves all SQL and withholds automatic rewriting until a statement is selected. Classify invalid identity/selection data as expected errors without DUMP.
- IMP-09 review: index sibling positions once per read and honor cancellation during traversal; retain empty XE payload diagnostics and source ordinals; propagate CLI cancellation through redact/refactor; read complete XML encoding declarations within existing budgets. Add 20 regressions; Debug and Release each pass 1247 tests. See [hardening evidence](DOCS/IMP-09审查修复与加固说明.md).
- IMP-09: unify bounded XML/XEL document reads, capture metadata, capability flags and explicit Partial/TooLarge/Cancelled outcomes. Preserve source snapshots, unknown fields and skipped event locations; reject incomplete input before SQL writeback. GUI retains partial event context; CLI failures remain nonzero and batches continue after individual input failures. See [contract and verification](DOCS/IMP-09统一文档读取与能力契约.md).
- IMP-08 review: make dependency inference and sandbox evidence text follow the active theme, including unknown-benefit and invalid-assumption states. Add eight resource-replacement regressions and real WPF theme/contrast verification; Debug and Release each pass 1183 tests. See [hardening evidence](DOCS/IMP-08审查修复与加固说明.md).
- IMP-08: preserve unknown runtime metrics in A/B comparisons, label estimated costs and dependency inference explicitly, require observed Range lock modes, and distinguish diagnostic hypotheses. Suspend uncalibrated sandbox benefit percentages and absolute Seek/Scan promises. Add 40 regressions, native dump/logging checks and real WPF state verification. See [implementation and evidence](DOCS/IMP-08展示纠错与证据边界说明.md).
- IMP-07 review: propagate explicit runtime-row availability through graph loading, recosting and connections. Preserve estimated own cost for missing/invalid/partial counts and preserve observed zero rows. Add 29 regressions, cost debug logging and WPF reload verification. See [hardening evidence](DOCS/IMP-07审查修复与加固说明.md).
- IMP-07: read standard deadlock priority before compatibility aliases; preserve missing versus zero. Read execution mode, Parallel and Ordered in their correct scopes without invented defaults. Separate output/read rows in both WPF tables, graph tooltips and details, preserving exact large counts; repair legacy PlanView event handlers. Add field, WPF binding, real dump and build-specific logging regressions. See [implementation and evidence](DOCS/IMP-07字段映射与绑定修正说明.md).
- IMP-06 review: reject nested ShowPlan namespace mismatches and malformed encoded bytes; use strict BOM retry on the same stream while preserving InternalInfo extensions. Read refactor auxiliary plans as XML byte streams and retain real source paths when selecting deadlock events. See [hardening and verification](DOCS/IMP-06审查修复与加固说明.md).
- IMP-06: Share minimum XML input recognition across GUI, scan, desktop CLI and refactor plan analysis. Reject unrelated XML, unsupported ShowPlan namespaces and damaged structures with explicit input status; invalid scan inputs return Failed and exit code 1.
- Preserve prefixed ShowPlan and schema-valid statements without RelOp. Expose every deadlock XML event through the existing selector; single-event parsers reject ambiguous collections.
- Propagate input failures before SQL refactoring/writeback, and route unexpected failures through the existing DUMP and Debug/Release logging policy.

### Added
- IMP-10: add document/batch/statement/query-plan/operator keys, four-part object references, source locations and revision-aware legacy adapters. Propagate identity to reports, graph nodes, snapshots and CLI outputs; render all query plans. Add 43 regressions and real DUMP/logging/WPF verification; Debug and Release each pass 1290 tests. See [implementation and evidence](DOCS/IMP-10语句算子与对象身份.md).
- IMP-09: add `IDiagnosticDocumentReader`, configurable `DocumentReadOptions`, CLI `read` and `--read-options`, contract/budget/XEL/WPF regressions, native DUMP and Debug/Release logging verification.
- Input recognition, schema-validated fixture, CLI, event selection, cancellation and native DUMP regression tests. See [IMP-06 implementation and verification](DOCS/IMP-06统一最低输入识别说明.md).

## [2.0.0] - 2026-06-24

### Fixed
- **Core Rules**: Reduced duplicate warnings in parameter sniffing rule (`ParameterSniffingRule.cs`) by restricting execution strictly to `NodeId = 0` (Plan level).
- **Core Rules**: Eliminated false positives in parameter sensitivity detection (`QueryRewriteRule.cs`) by removing the assumption that statistics usage alone implies parameter sniffing; compile-time and runtime parameter value differences are now required.
- **Core Rules**: Filtered out healthy statistics info alerts (with `Info` severity) from `StatsUsageRule.cs`, raising warnings/critical flags only for actual issues (e.g. stale stats, low sampling, high modifications).

### Added
- **SQL Refactoring**: Added `TryRewriteSelectedSubquery` to `ScalarSubqueryToJoinRule.cs` to enable targeted, single-expression scalar subquery-to-join rewrites by offset and length.
- **WPF UI**: Introduced inline quick-fixes for rewriteable subqueries within the original SQL diff viewer. Clicking the lightbulb icon launches a side-by-side comparison in `QuickFixWindow` and allows applying the rewrite localized to that subquery.
- **WPF UI**: Added SQL tokenization and syntax highlighting in the quick-fix and diff viewer (comments, strings, standard keywords, and generated aliases `t_sub_*` and `agg_*`).
- **Tests**: Added suite of unit tests for parameter sensitivity rules (`ParameterSensitivityRuleTests.cs`) and targeted scalar subquery rewrites (`ScalarSubqueryQuickFixTests.cs`).
