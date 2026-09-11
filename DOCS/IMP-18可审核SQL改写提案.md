# IMP-18：可审核 SQL 改写提案

**联合审查更新（2026-09-09）：** IMP-18 / IMP-19 联合审查发现的五项问题已修复，详情见 [联合审查修复说明](IMP-18-19联合审查修复与加固说明.md)。静态提案契约保持不变，后续应用窗口同步显示真实提交状态。

**IMP-19 后续更新（2026-09-09）：** [SQL 语义验证与可靠应用](IMP-19SQL语义验证与可靠应用.md)已接入独立流程，补齐真实 SQL Server 场景、WPF 验证/应用及四张窗口截图。`refactor` 和静态 `RewriteReview.CanApply=false` 的提案契约保持不变。下文“IMP-19 待实施”和截图受限是 IMP-18 初次交付时的历史记录，以后续说明为当前状态；完整工作台需求仍由 IMP-21 推进。

日期：2026-09-09。状态：本步实现及自动化测试完成；实际窗口截图验收受当前桌面访问限制，未完成。IMP-19 的数据库语义验证与可靠应用、IMP-21 的完整工作台交互仍待实施。R02/R19/R20 继续保持风险缓解状态，不宣称语义问题已完整关闭。

**审查修复更新：** 文本报告已使用所选 `Review.PreviewSql`；补齐 SELECT INTO 目标检查，并加固完整对象名、大小写、DROP IF EXISTS 和 IF/WHILE/TRY-CATCH 分支比较。新增 36 项回归，详见 [修复、检索依据与最新验证记录](IMP-18审查修复与加固说明.md)。本文原 35/2047 测试计数保留为初次实施记录。

## 行为与兼容性

SQL 改写现在返回审核提案。所有候选默认未选择；ScriptDom 解析成功不代表等价或可应用。CLI `refactor`、桌面计划分析、标量子查询灯泡入口共用 `RewriteProposal` / `RewriteReview` 及审核文字。

**行为变化：`refactor query.sql` 不再自动写回，即使未传 `--dry-run`。** `--select <提案ID>` 可重复，只生成组合审核预览。`IsSuccess=true` 表示生成/审核成功，`SourceWritten=false`、`CanApply=false`；不存在以勾选或解析成功绕过语义验证的路径。IMP-03 的安全写回服务及其备份、并发和故障测试保留，后续由 IMP-19 的独立应用流程接入。

兼容字段 `RefactorResult.OutputSql` / JSON `RefactoredSql` 仍表示完整候选，仅供预览；所选组合的正式结果是 `Review.PreviewSql`。调用方应使用 `Review`，不能把旧字符串字段视为可应用许可。旧引擎未提供提案时，Application 给出 `MissingProposalContract` 并阻止自动写回。

## 提案与校验契约

| 字段 | 含义 |
| --- | --- |
| `Id` | 源、步骤基线、候选、规则版本和依赖的 SHA-256 摘要；相同输入及规则版本可重现 |
| `SourceHash` | **完整解码 SQL 文本**按 UTF-8 无 BOM 计算的 SHA-256；保留换行/空白/注释，不等同原文件字节指纹 |
| `BaseSqlHash` / `CandidateHash` | 此步骤的输入/输出内容摘要 |
| `RuleId` / `RuleVersion` | 原规则 ID 与实现程序集版本；规则发布时需维护版本 |
| `Diff` | 步骤输入上的字符 offset、原范围和替换范围；不是全部步骤共用原文件 offset |
| `DependsOn` | 先前生成步骤的保守依赖链，防止把后续 AST 改写错误地当作独立候选 |
| `Preconditions` / `Risks` / `Evidence` / `Warnings` | 审核前提、风险、实际匹配依据和警告 |
| `Validation` | `SyntaxValid`、已检查的性质、未证明性质、错误及 `Equivalence=Unproven` |
| `IsSelected` | 默认 false；选择仅影响审核预览 |

Analyze 使用原 SQL/规则上下文；Propose 为每个实际改变 SQL 的规则步骤生成不可变快照；Validate 检查语法及临时对象/批次结构；Review 按选择组合，重新校验；Apply 保持关闭。源 hash 是一致性检查，不是签名或用户批准凭据。

审核时检查源 hash、重复/未知 ID、步骤依赖、基线 hash、diff 原范围及候选 hash，并对完整组合再次解析，检查临时对象 CREATE/DROP、表变量声明序列和批次边界一致性。无效选择返回错误，不返回部分预览；引擎错误丢弃已生成变更、保留原 SQL。取消全部选择逐字符恢复原文。GUI 选择后续项时同时选择其前序依赖，取消前项时取消依赖它的后续项；CLI 必须明确传入这些 ID。当前采用保守依赖链，不提供任意规则的独立重排。

加固后也检查 `SELECT INTO` 的完整目标名称（含永久目标），保留标识符大小写及临时表 `DROP IF EXISTS`，对对象事件的 IF/WHILE 条件和 TRY/CATCH 分支做结构比较。这些仍是限定范围内的静态检查，不证明完整对象定义、模块/动态 SQL 作用域或完整生命周期。

语法、这些结构性质与语义证明分别记录。结果集/重复行、NULL/类型/排序规则、错误/溢出、事务、会话设置和性能仍未证明。附加计划来源仅作为上下文，未核验与 SQL 的语句绑定时明确标注，不能借用计划诊断充当等价证明。所有规则警告保留在审核结果中，即使对应候选未选择。

## 表变量、作用域与对象所有权

`SqlRewriteScope` 建立批次级表变量声明清单、声明位置及已有临时对象名称集合。可识别的顶层多声明/多批次使用独立名称预留，避开已知名称和本轮预留，记录归属源 hash。名称仅为预留，**不生成 CREATE、DROP 或清理脚本**，不能证明调用方或动态 SQL 中不存在同名对象。

嵌套声明、过程/函数/控制流、EXEC 和重复声明明确给出作用域限制；无法可靠判断时不预留转换名称。TRIM、表变量转换继续按 IMP-04 保留原写法，名称可分配也不等于事务语义等价。旧 `TableVariableVisitor` 直接调用同样只记录限制，不再创建/删除对象。验证器阻止提案组合改变临时对象创建/清理或表变量声明序列。

## 使用方法

桌面端打开单语句计划，在“SQL 改写提案”页点击“审核提案”，或点击标量子查询灯泡。窗口列出规则/版本、前提、风险、证据、diff 和原始/已选 SQL。默认预览原文；勾选只在该审核窗口生成预览，关闭不替换原 SQL，也不写文件。窗口选择当前不持久化。多语句计划仍要求先明确语句范围，不生成跨语句改写。

```powershell
# 查看提案与 diff；不修改 query.sql
dotnet run --project SqlXmlAnalyzer.CLI -- refactor query.sql --format json --show-sql

# 将返回的 ID 及其依赖逐个传入，只生成审核预览
dotnet run --project SqlXmlAnalyzer.CLI -- refactor query.sql --select <提案ID> --format json --show-sql
```

JSON `ProposalSchemaVersion=1`。不传 `--show-sql` 时，`Diff`、SQL 和 `Review.PreviewSql` 不展开，但仍输出风险/证据等审核元数据；这不是脱敏模式。文本与 GUI 通过 `RewriteReviewFormatter` 使用同一套限制说明，SQL 和说明分开。

实际 CLI 示例（合成 `Age + 10 > 50` 输入）：

```text
Outcome: CandidateGenerated
Review.Proposals[0].IsSelected: false -> true
Review.PreviewSql: 原文 -> WHERE Age > 40
Validation.Equivalence: Unproven
SourceWritten: false
CanApply: false
```

完整输出：[未选择 JSON](verification/IMP-18/cli-unselected.json)、[已选择 JSON](verification/IMP-18/cli-selected.json)、[未修改输入](verification/IMP-18/sample.sql)。

## 异常与日志

输入、范围/hash/依赖错误及 IO 为已知错误，记录 Error；取消记录 Warning。未知异常经 `ExceptionPolicy` / `UnexpectedErrorReporter` 捕获真实 Windows minidump 和异常 sidecar；同一异常对象去重。DUMP 写入失败时保留原异常与 sidecar，并明确记录失败；诊断组件自身异常不能替换主错误。

默认 DUMP 目录 `%LOCALAPPDATA%\SqlXmlAnalyzer\dumps`，失败时尝试 `%TEMP%\SqlXmlAnalyzer\dumps`；日志目录 `%LOCALAPPDATA%\SqlXmlAnalyzer\log`。DUMP 可能含进程数据，仍遵循现有受限诊断文件策略。

- Debug 日志：DEBUG、WARN、ERROR、CRITICAL（致命）。
- Release 日志：仅 ERROR、CRITICAL，verbose 参数不能放宽限制。
- 日志写 stderr/日志文件，不污染 CLI JSON stdout；新提案流程常规日志只记录规则和计数，不记录 SQL/对象名。
- 提案中的前提、风险和警告属于业务结果，两种配置均可阅读，不受诊断日志级别过滤。

## 验证与剩余边界

新增 35 项测试。Debug/Release 全量各 **2047 通过、0 失败、0 跳过**；完整构建各 **0 警告、0 错误**。覆盖默认不写回、稳定 ID、空选择恢复、依赖、组合重新验证、源/diff/hash 不一致、多声明/命名碰撞/未知作用域、GUI/CLI 同一契约、真实 DUMP、DUMP 失败、诊断失败和两种模式日志。

证据：[Debug 构建](verification/IMP-18/build-debug.log)、[Debug 测试](verification/IMP-18/test-debug.log)、[Release 构建](verification/IMP-18/build-release.log)、[Release 测试](verification/IMP-18/test-release.log)、[Debug TRX](verification/IMP-18/full-debug.trx)、[Release TRX](verification/IMP-18/full-release.trx)。

WPF 单元测试使用生产审核窗口验证绑定、布局和选择/取消恢复。截图生成另有[可复现工具](verification/IMP-18/verify-wpf.ps1)：本环境离屏渲染返回空白像素，[探针明确失败并生成 DUMP](verification/IMP-18/wpf-render-unavailable.log)；computer-use 启动实际窗口两次均返回 `GetCursorPos failed: 拒绝访问 (0x80070005)`。未交付空白图像，不宣称视觉截图或桌面人工验收通过。

本步未运行数据库等价性验证，未承诺性能改善，未生成覆盖率报告。后续 IMP-19 必须在隔离数据库验证受支持场景并再次核对源、选择及写回状态；未完成前所有提案继续不可应用。
