# IMP-16 审查修复与加固说明

实施日期：2026-09-09。修复 IMP-16 审查发现的三个评分问题，并加固谓词来源、参数识别和异常回归。前置基线为初版 IMP-16 的 Debug/Release 各 1816 项测试，本轮新增 60 项，最终各 1876 项通过。初版顺序不变性、成本并集和输入来源契约继续有效，见 [实施说明](IMP-16模拟顺序依赖与模型限制.md)。

## 修复结果

| 问题 | 原行为 | 当前行为 |
| --- | --- | --- |
| 括号、带符号常量漏计分 | 文本 `[K]=(1)`、`[K]=(-1)` 不被识别为直接列与值的比较 | 解析 ScriptDom 的括号节点；仅对数值常量展开正负号。等值键得到 30 分，范围键得到 15 分；函数、算术和带符号列不被当作直接列 |
| CASE 内部条件被提升为过滤条件 | 展平 Predicate 下所有 Compare，导致 CASE 的条件或分支也增加索引分数 | 从完整谓词根读取，仅沿 AND 独立合取项向下分析。IF/CASE、OR、NOT 等子树停止取证；保留同级独立 AND 条件的分数 |
| 结构化 NULL 谓词漏计分 | `IS NULL` 单操作数及 `IS` 与 NULL 常量的结构无法计分；存在结构时文本后备也无法补足 | 支持合法的单操作数 `IS NULL`/`IS NOT NULL`，以及 `IS`/`IS NOT` 与明确 NULL 常量的两个方向。前者计等值 30 分，后者计范围 15 分 |

普通 `= NULL`、`<> NULL` 不转换成 NULL 判断，不推断 ANSI_NULLS 会话设置。非 NULL 的 `IS`/`IS NOT` 结构仍不在支持范围。NULL 分类和评分权重是本工具的启发式契约，不是 SQL Server 对索引性能的承诺。

实现集中在 [IndexPredicateEvidence.cs](../SqlXmlAnalyzer.Core/Scoring/IndexPredicateEvidence.cs)。仅使用当前目标访问算子自身 payload 的完整 `Predicate/ScalarOperator` 根，避免借用子算子、CASE 分支、子查询或 Seek `RangeExpressions` 的表达式。结构化根必须满足单一表达式结构，允许末尾的官方 `InternalInfo` 扩展但不从中提取谓词。外来命名空间、重复表达式和错误操作数形状均跳过。

只有根没有结构化子节点时，才使用完整 `ScalarString` 的语法后备；存在不支持或损坏的结构时，不再从根文本或后代文本补造分数。结构化读取和文本布尔树遍历使用显式栈。

文本中的真正 `VariableReference` 可作为参数；`[@p]` 这类带引用符号的名称，必须有当前访问算子或所属 QueryPlan 的参数证据，且不能同时是已核验实体列。`[@real]` 等真实列保留实体含义；其他 QueryPlan 的 ParameterList 不能借用。三个旧测试夹具补充了原本缺失的参数元数据，不再让测试依赖名称猜测。

既有 `imp15_index_targets.sqlplan` 中带括号的等值与范围谓词恢复 30 + 15 分，加上已知覆盖 40 分，沙盒为 **85/100**；捕获的 SQL Server Impact 保持 **80**。两者来源和含义继续分开。

## 版本与兼容性

- 评分模型：`index-evidence-score/2.0.1`；权重和公开字段保持不变。
- `RULE_020_MISSING_INDEX`、`RULE_035_SARGABLE_INDEX_RECOMMENDATION`：`2.1.1`；规则 ID、默认严重程度和 RuleConfiguration.json 不变。
- 成本模型仍为 `captured-cost-exposure/2.0.0`，输入模型仍为 `sandbox-input-assumptions/2.0.0`。
- 直接括号/数值/NULL 谓词可能提高评分；之前错误借用 CASE、内部条件或参数名称的候选可能降低评分。消费者应保留模型版本，重新计算后再排序。

## 异常、DUMP 与日志

新取证路径仍在 `IndexScoringCalculator.Evaluate` 的统一 `ExceptionPolicy` 边界内。预期输入失败、I/O 和取消沿用既有分类；无法识别的结构作为缺少证据处理，不伪装成成功谓词，也不因不支持一种表达式而生成故障 DUMP。

未知错误生成 Windows minidump 和 `exception.json`；相同异常实例去重，DUMP 写入失败保留明确失败信息，仍传播原异常。本轮新增两个评分入口故障测试，分别验证真实 minidump 与写入失败路径，检查元数据来源和去重。测试使用专用目录并清理，不保存二进制 DUMP 到仓库。

日志继续执行编译配置策略：Debug 输出 DEBUG、WARN、ERROR、CRITICAL；Release 仅 ERROR、CRITICAL，强制 verbose 也不能绕过。日志回归经过修复后的括号谓词路径，并检查 SQL/标识符不进入模型诊断日志；CLI stdout 保持纯 JSON，日志走 stderr/文件。

## 测试与真实计划验证

首批 41 个针对性用例在修改前出现 29 个失败、12 个通过，见 [失败记录](verification/IMP-16-review/red-tests.log)。最终新增 58 个谓词用例和 2 个评分故障用例，共 60 个；包括括号/正负数、结构化 NULL、CASE 三个分支、AND 重排、OR/NOT、参数归属、实体同名、异常结构、InternalInfo、范围表达式，以及 8 个真实捕获计划用例。

结构化 NULL 测试通过仓库内冻结的 [Microsoft ShowPlan SQL 2019 XSD](../SqlXmlAnalyzer.Tests/TestData/Schemas/showplanxml-sql2019.xsd) 验证 ScalarType 片段；它不是对所有手工构造完整计划的模式验证。解析和模式读取使用 SafeXmlHelper，禁用外部解析。

使用专用 SQL Server LocalDB 17.0 实例、合成表 `dbo.T` 捕获八个查询的估算计划。XML 记录服务器 Build 为 **17.0.4075.5**，连接版本为 **17.00.4075**。直接常量与 NULL/CASE 回归使用 `OPTION (RECOMPILE)` 保留相应表达式形状；另保留自动参数化查询作为不支持形状的反例。

| 原生查询条件 | 等值分 | 范围分 | 取证结果 |
| --- | --- | --- | --- |
| `K = -1`，RECOMPILE | 30 | 0 | 直接常量 |
| `K IS NULL` | 30 | 0 | NULL 等值 |
| `K IS NOT NULL` | 0 | 15 | 非 NULL 范围 |
| `CASE WHEN K=1 THEN V ELSE R END = 3` | 0 | 0 | CASE 内部条件不取证 |
| `K=-1 AND CASE WHEN V=1 THEN R ELSE V END = 3` | 30 | 0 | 仅独立等值条件 |
| `K=@p`，已声明参数 | 30 | 0 | 结构化参数 |
| `K=[@real]` | 0 | 0 | 实体列对列比较 |
| `K=-1`，保留自动参数化 | 0 | 0 | 本次捕获为 ConstExpr/Convert，明确不支持 |

这些分项不包括已知覆盖分。最后一项仍为 `MISSING_PREDICATE_EVIDENCE`：当前不会穿透 Convert/ConstExpr、列函数或其他计算表达式证明它是可评分的值，也不会用 ScalarString 覆盖已有结构。这是当前模型限制，不能解释成“查询不可使用索引”。捕获文件已加入 [嵌入式回归夹具](../SqlXmlAnalyzer.Tests/TestData/imp16_review_predicates.sqlplan)，Debug/Release 都重放八个用例。专用数据库、数据文件和 LocalDB 实例已清理，既有实例保留。

| 验证 | Debug | Release |
| --- | --- | --- |
| 完整解决方案构建 | 0 警告、0 错误 | 0 警告、0 错误 |
| 全量 xUnit | 1876 通过，0 失败，0 跳过 | 1876 通过，0 失败，0 跳过 |
| 未知错误真实 DUMP、写入失败及日志过滤 | 通过 | 通过 |
| CLI JSON、4 个候选、版本/证据、首项评分 85 | 通过 | 通过 |
| 新采集的 LocalDB 计划在线评分 | 8 个条件通过 | 嵌入式夹具重放通过 |
| 完整 WPF 窗口布局/绑定探针 | 全量套件中的 WPF 测试通过 | 浅色/深色、7 个绑定、滚动、用户编辑来源、85 分/Impact 80/模型版本，0 绑定错误 |

本轮没有修改 XAML。WPF 探针验证生产窗口的布局与绑定，未增加截图或人工视觉验收结论。未执行建索引后的耗时、I/O、并发或整个工作负载校准，也未生成覆盖率报告。

证据：[清单与文件哈希](verification/IMP-16-review/validation.json)、[Debug 构建](verification/IMP-16-review/build-debug.log)/[测试](verification/IMP-16-review/test-debug.log)、[Release 构建](verification/IMP-16-review/build-release.log)/[测试](verification/IMP-16-review/test-release.log)、[LocalDB 结果](verification/IMP-16-review/localdb-results.json)/[原生计划](verification/IMP-16-review/captured.sqlplan)、[CLI](verification/IMP-16-review/cli-validation.json)、[WPF 布局/绑定](verification/IMP-16-review/wpf-probe.json)。复现脚本：[LocalDB](verification/IMP-16-review/verify-localdb.ps1)、[CLI](verification/IMP-16-review/verify-cli.ps1)、[WPF](verification/IMP-16-review/verify-wpf.ps1)。初版 `verification/IMP-16/` 保留为历史证据。

## 检索依据

按 Microsoft 官方资料优先核验。网页检索连接失败后使用只读 HTTPS 读取 Microsoft Learn 和官方 GitHub API；以下官方资料已覆盖本轮语法和计划结构约束，未以社区推测代替结构证据。GitHub 未提供可调用连接器，本轮通过官方仓库 API 读取代码，没有执行远程代码。

- [Microsoft Learn：ParenthesisExpression](https://learn.microsoft.com/en-us/dotnet/api/microsoft.sqlserver.transactsql.scriptdom.parenthesisexpression?view=sql-transactsql-161) 与 [UnaryExpression](https://learn.microsoft.com/en-us/dotnet/api/microsoft.sqlserver.transactsql.scriptdom.unaryexpression?view=sql-transactsql-161)：括号和一元表达式是独立节点，不能只检查叶节点 Literal。
- [Microsoft Learn：IS NULL](https://learn.microsoft.com/en-us/sql/t-sql/queries/is-null-transact-sql?view=sql-server-ver17)：区分 NULL 测试与普通比较。
- [Microsoft Learn：CASE](https://learn.microsoft.com/en-us/sql/t-sql/language-elements/case-transact-sql?view=sql-server-ver17)：CASE 根据条件返回表达式结果，内部条件不等同于外部 WHERE 的独立条件。
- [Microsoft 官方 ShowPlan XSD](https://schemas.microsoft.com/sqlserver/2004/07/showplan/sql2019/showplanxml.xsd)：ScalarType 单一表达式和可选 InternalInfo、Compare 操作数及 NULL 操作码、ConditionalType 的 Condition/Then/Else 结构。本地冻结文件 SHA-256：`845B3FAA55748D442DFC71071315FF1EBB611E4E298E8D06A21EBF946B8C93A8`。
- [Microsoft SqlScriptDOM Ast.xml 固定版本](https://github.com/microsoft/SqlScriptDOM/blob/b583737682dbb1682b97d0ac5a5261dc94279b8d/SqlScriptDom/Parser/TSql/Ast.xml)：核对 ParenthesisExpression、BooleanParenthesisExpression、UnaryExpression、BooleanIsNullExpression 和 CASE 节点定义；Git blob 为 `e9c2d9ebe0b315350b8e2309e0d2a2d17e5cba2a`。解析器提供语法树，不提供数据库绑定或性能证明。
