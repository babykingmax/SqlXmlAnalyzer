# IMP-16：模拟顺序依赖与模型限制

审查更新：三个谓词评分问题及相邻取证边界已修复，评分模型升为 2.0.1、RULE020/035 升为 2.1.1；Debug/Release 全量各 1876 项通过。当前语法支持、兼容性及最新验证见 [审查修复与加固说明](IMP-16审查修复与加固说明.md)。下文版本、计数及 `verification/IMP-16/` 为初版实施记录；当前后备策略为完整谓词根没有结构化子节点时才解析文本。

实施日期：2026-09-09。完成当前步骤范围；不恢复未经实测校准的性能收益预测。前置基线为 IMP-15 审查后 Debug/Release 各 1766 项测试，本步新增 50 项，最终各 1816 项通过。

## 修复与计算契约

旧 `CostImpactSimulator` 在遍历每条语句时先增加分母、再计算本语句贡献。两条成本分别为 10 和 100 的语句仅交换顺序，即产生 100% 与 65% 的不同结果；父子子树成本还可能重复累计。旧沙盒取第一个匹配算子的默认值，访问顺序改变后，总行数/行宽/返回行数从 100/20/2 变为 200/40/4。两个反例已在修改前复现，见 [首次失败记录](verification/IMP-16/red-tests.log)。

成本模型现在执行两个阶段：先确定当前文档中全部已识别 QueryPlan/算子，并汇总完整分母；再逐项关联候选。分母使用 IMP-11 的 `OwnCost`：本算子子树估算成本减直接子算子子树成本，不再叠加 StatementSubTreeCost，也不把子算子的 Object 当作父算子的目标。直接子树成本与最终自身成本均按数值排序后求和，消除浮点累加的遍历顺序差异。

候选关联继续使用 IMP-15 的完整 Server/Database/Schema/Object 与捕获 QueryPlan 身份；缺少完整身份、跨文档陈旧位置、没有键列或无法定位时保持未知。结果保存候选键/INCLUDE 定义快照，后续编辑不会改写已有结果。

多个候选逐项独立评估；组合成本按 `PlanOperatorKey` 取并集，同一算子只计一次。**不相加评分，不叠加假设收益**。任一候选不能关联时，组合结果不能代表完整输入，组合成本保持未知；仍保留各项的证据状态。候选编号由目标与有序列定义的摘要生成，输出有确定的排列；文档/语句位置仍忠实反映当前捕获顺序，不伪装成跨文档稳定身份。

示例：两个根的子树成本为 10、100，目标扫描的成本为 6、60。全量自身成本为 110；第一个候选关联成本为 6，相关成本占比为 5.4545%；同时评估两个候选时关联并集为 66，占比为 60%。这些只是原计划成本分布，**不是可节省的成本或时间**。

| 结果字段 | 当前含义 |
| --- | --- |
| ModelVersion | `captured-cost-exposure/2.0.0` |
| DocumentId / QueryPlanCount / OperatorCount / InputScope | 实际捕获文档、总量与关联范围 |
| TotalOwnCost / RelatedOwnCost | 全部自身成本、关联访问算子自身成本；估算值及来源/状态 |
| RelatedCostSharePercent | 关联成本 ÷ 全量成本 × 100；分母为零或证据不完整时为 null |
| ReductionPercent | 始终为 null，不能解释为已测得 0% 改善 |
| IsCalibrated / CostBasis / CombinationPolicy / Limitations | 未校准状态、计算口径、组合规则及限制 |

真实 0 保留；缺失、非法数字、NaN/Infinity、负值、缺失子成本及求和溢出不会变成有效 0。成本不足显示 Incomplete/Invalid，目标不足显示 `SIMULATION_TARGET_UNRESOLVED`。不再使用 Scan 60%、Lookup 40%、Sort 30% 等固定折减猜测。

## SQL Server Impact、工具评分与输入假设

三类信息独立保存、显示：

1. `CapturedImpact` 仅保留有效的 MissingIndexGroup/@Impact，范围为 0–100，来源为 `CapturedMissingIndex`；缺失或无效为 null。SQL 语法推导候选标为 `SqlSyntax`，不再补造 Impact=80。沙盒和部署包都核对来源后显示 SQL Server Impact/N/A，且注明优化器估算。
2. `IndexScoreResult` 使用 `index-evidence-score/2.0.0`，公开分项、权重、位置、输入范围、谓词来源和未校准限制。评分范围为候选所属 QueryPlan 的完整对象；没有计划时，只给声明列角色的启发式分。
3. `SandboxInputSnapshot` 使用 `sandbox-input-assumptions/2.0.0`。总行数、行宽、预计返回行数分别取当前目标/QueryPlan 中有效计划估算的最大值，并明确标为假设。这三个最大值可能来自不同访问算子，不构成一次真实执行。无有效值分别使用 100000、200、1000 的工具默认值，编辑后改标“用户编辑”。真实零值不会被默认值覆盖。

评分权重：连续等值前导键每列 30 分，首个不等值键 15 分，一个兼容排序 15 分，已核验实体输出列的覆盖比例 × 40 分；键超过 4 列或 INCLUDE 超过 8 列，每超一列扣 2 分，最后限制到 0–100。**没有输出列证据时覆盖率为未知、覆盖分为 0**，不再默认赠送 40 分。多个排序不拼接或叠加。该公式只用于候选筛选，不是 SQL Server 的成本公式。

谓词优先读取当前访问算子中的结构化 Compare。无 Compare 时，用 ScriptDom 解析 ScalarString 的 WHERE 表达式，明确标为较低可信度；不再通过子串和固定空格匹配列名。支持直接列与常量/参数比较，保守跳过 OR、NOT、列函数、跨目标引用、列对列及多语句载荷。未识别到可评分谓词时明确标为证据不足。未覆盖的 Seek/范围结构不会被补造为等值证据。

覆盖仅指捕获且能核验归属的实体输出列；不证明所有内部表达式依赖或谓词均已覆盖。跨访问算子汇总的列证据仅用于启发评分，不证明它们构成同一个可使用的索引前缀。未读取完整索引目录、列类型、参数分布、写入代价、并发及整个工作负载，因此不能保证 Seek，或保证消除 Lookup/Sort。

沙盒在可滚动的评分、成本和参考区间面板中显示上述来源、权重、模型版本、成本范围及默认/编辑标记；收益预测持续显示 N/A。输入参考区间不参与原计划成本计算，也不改变捕获的 SQL Server Impact。

## 异常、DUMP 与日志

评分与模拟入口通过 `ExceptionPolicy` 分类异常；沙盒继续使用统一边界，失败时清空结果和 DDL，不留下上一次成功评分。候选枚举中途发生故障也不能返回半份成功结果。取消在候选枚举、分析和逐项计算处传播，预期 I/O、输入校验和取消不生成未知故障 DUMP。

未知错误记录原异常、生成 Windows minidump 与 `exception.json`，相同异常实例只捕获一次；捕获失败记录明确的 DUMP 失败信息，仍抛出原异常。默认目录为 `%LOCALAPPDATA%\SqlXmlAnalyzer\dumps`，失败时使用临时目录后备。测试实际调用 `WindowsMiniDumpWriter` 并验证文件结构，随后清理测试专用目录；不把可能包含进程数据的二进制 DUMP提交到仓库。

新增日志记录模型状态、算子/候选计数与评分分项，不记录 SQL 或目标标识符。沿用编译配置策略：Debug 输出 DEBUG/WARN/ERROR/CRITICAL；Release 仅 ERROR/CRITICAL，强制 verbose 也不会放开 Debug/Warning。CRITICAL 对应致命级别。CLI stdout 保持纯 JSON，日志走 stderr/文件。

## 兼容性

- `Simulate` 单候选入口保留，新增 `SimulateCandidates`。`CostImpactResult` 改为带范围/证据的结果；旧构造器不再适用，`ReductionPercent` 从 int 改为可空 int 且始终为 null。调用方必须处理未知，不能继续当作预测收益。
- 旧 `MissingIndexSuggestion.Impact` 数字字段保留用于兼容；未知情况下其 0 不代表已采集 Impact，应迁移到 `CapturedImpact` 和 `Source`。新增 `ScoreAssessment`，保留旧整数 `Score`。
- 评分可能下降，尤其是原先缺少覆盖证据或仅靠文本误匹配得分的候选。排序应使用新版评分，并保留关联身份和模型版本。
- RULE020/035 版本更新为 **2.1.0**；规则 ID、默认严重程度和 RuleConfiguration.json 保持兼容。RULE035 的原生证据包含评分版本、明细、权重、输入范围和谓词来源，CLI 与文本报告可追溯；没有新增规则 ID。

## 验证结果

| 检查 | Debug | Release |
| --- | --- | --- |
| 完整解决方案构建 | 0 警告、0 错误 | 0 警告、0 错误 |
| 全量 xUnit | 1816 通过，0 失败，0 跳过 | 1816 通过，0 失败，0 跳过 |
| 未知异常真实 DUMP、捕获失败、取消、日志过滤 | 通过 | 通过 |
| CLI 多语句夹具 | 退出 0，4 个候选，无规则失败，评分证据完整 | 同 Debug，日志过滤通过 |

新增 50 项回归覆盖语句/候选/算子重排、浮点求和、重复候选并集、父子成本、嵌套语句、身份和零/缺失/非法成本；同时覆盖谓词结构优先、紧凑文本、错误目标/函数/OR、缺失覆盖、Impact 来源和编辑输入。评分重排测试使用不同分数的两条语句，验证身份对应与排序一起保持不变。沿用真实 SQL Server 捕获夹具 `imp15_review_actual.sqlplan` 校验结构化谓词，不宣称已进行性能校准或新增覆盖率报告。

WPF 单元测试包含新增六个证据控件的浅色/深色资源切换与对比度检查。另用完整编译后的生产窗口运行布局/绑定探针：七处证据绑定、滚动位置、主题切换及输入编辑正常，绑定错误为 0。当前会话离屏位图导出全透明，故未保留空白图，也不将其报告为截图或人工视觉验收通过；窗口测量与绑定证据见 JSON。没有执行建索引后的工作负载性能基准。

证据：[验证清单与源码哈希](verification/IMP-16/validation.json)、[Debug 构建](verification/IMP-16/build-debug.log)/[测试](verification/IMP-16/test-debug.log)、[Release 构建](verification/IMP-16/build-release.log)/[测试](verification/IMP-16/test-release.log)、[CLI](verification/IMP-16/cli-validation.json)、[WPF 布局与绑定](verification/IMP-16/wpf-probe.json)。复现脚本：[CLI](verification/IMP-16/verify-cli.ps1)、[WPF](verification/IMP-16/verify-wpf.ps1)。R12 的本步顺序不变性与模型披露要求已满足；IMP-21 的后续 UI 整体改造及真实工作负载校准仍是独立事项。

## 依据

按 Microsoft 官方资料优先核对；网页检索连接失败时使用只读 HTTPS 获取正文。模型公式、最大值假设和评分权重均为本工具的保守设计，不声称来自 SQL Server 官方成本模型。

- [Microsoft Learn：缺失索引建议的限制](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/tune-nonclustered-missing-index-suggestions?view=sql-server-ver17)：建议基于单条查询优化时的估算，未在执行后验证，也不能保证整个工作负载收益。
- [Microsoft Learn：显示和保存执行计划](https://learn.microsoft.com/en-us/sql/relational-databases/performance/display-and-save-execution-plans?view=sql-server-ver17)：区分编译时估算与实际执行计划的运行信息，不能把估算成本当作实测耗时。
- [Microsoft SqlScriptDOM](https://github.com/microsoft/SqlScriptDOM/tree/b583737682dbb1682b97d0ac5a5261dc94279b8d)：项目既有解析器用于语法取证；语法树本身不提供数据库绑定或执行等价证明。
