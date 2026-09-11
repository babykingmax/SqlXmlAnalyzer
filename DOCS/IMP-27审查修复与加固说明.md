# IMP-27 审查修复与加固说明

日期：2026-09-10。范围为本地诊断包的规则版本、缺失证据及日志附件错误提示。修复审查发现的两项 P2，并补充一项编码错误分类修复。

## 规则版本来源

原 `configuration.json.RuleVersions` 通过 `RuleMetadataCatalog.Get` 读取默认 `1.0.0`，而六个规则在自身 `Metadata` 属性中覆盖为 `2.0.0` 或 `2.1.1`，导致导出清单与执行版本不一致。

版本现在与分类、作用域、默认严重度一起保存在规则目录的 `Definition` 中。六个规则使用目录返回的版本，诊断包也读取同一元数据源。规则实现版本、ID、严重度、触发条件和阈值没有升级；修复的是版本记录的来源。

| 规则 ID | 正确版本 |
| --- | --- |
| `RULE_004_ESTIMATE_MISMATCH` | `2.0.0` |
| `RULE_006_RESIDUAL_PREDICATE` | `2.0.0` |
| `RULE_030_CARDINALITY_ERROR` | `2.0.0` |
| `RULE_034_RESIDUAL_PRED_OP` | `2.0.0` |
| `RULE_020_MISSING_INDEX` | `2.1.1` |
| `RULE_035_SARGABLE_INDEX_RECOMMENDATION` | `2.1.1` |

`RuleVersions` 表示当前程序内置规则的版本及默认严重度；`ReportRuleVersions` 继续保留分析快照中实际记录的版本，包括禁用规则的 Skipped 记录。二者允许不同，不能用当前目录覆盖旧报告版本。没有报告时只提供当前内置版本，历史版本保持不可用。严重度覆盖保留在配置中，不会改写默认严重度。

## 指标证据状态

原 `MetricGaps` 只匹配 `Missing/Invalid/Unsupported`，遗漏本项目已有的 `Incomplete/Ambiguous`，且 `Unsupported` 不是 `PlanMetricState` 成员。

现在只接受未分类的 `/State` 字段、已定义的 `PlanMetricState` 规范名称，并收集所有非 `Available` 状态。数字、未定义值、逗号组合、大小写变体、前后空格及被分类为自由文本的字段均不会进入元数据摘要。即使用户不附带报告正文，原报告的缺失指标仍被记录。

回归计划有两个工作线程：行数分别为 10、20，执行次数分别为 1、2；只提供 EstimateRebinds，不提供 EstimateRewinds。导出保留估算执行次数 `Incomplete`、逻辑执行次数和每次执行行数 `Ambiguous`。非法线程 CPU 计数保留线程级 `Invalid` 与聚合级 `Incomplete`，没有采集的读取行数保留 `Missing`；正常输出行数不列为缺口。本次没有改变指标提取、线程聚合或规则判定算法。

## 附件异常处理与日志

非法 UTF-8 日志原本被通用 `ArgumentException` 捕获分支误报为路径错误。现在先识别 `DecoderFallbackException`，明确提示选择有效 UTF-8 `.log`，保持预期输入失败分类、返回空包，并且不生成 DUMP。

所有未知异常仍由既有 `ExceptionPolicy` 和 `UnexpectedErrorReporter` 写 Critical 并生成真实 Windows minidump；转储失败保留原错误并报告诊断失败。Debug 保留 Debug/Warning/Error/Critical，Release 仅 Error/Critical，verbose 参数不能绕过编译门禁。默认包仍不附带日志、转储或原始 XML；保存仍使用审核时冻结的字节。

## 检索依据

优先核对 Microsoft Learn，再查阅 Microsoft 在 GitHub 公开维护的诊断工具。本次网页检索工具连接失败，改用 HTTPS 直接读取官方页面及 GitHub Contents API。未运行、安装或复制外部采集脚本。

- [Microsoft：显示实际执行计划](https://learn.microsoft.com/en-us/sql/relational-databases/performance/display-an-actual-execution-plan?view=sql-server-ver17)：实际计划包含运行时资源指标及运行时警告，支持在诊断材料中明确记录运行时证据限制。本项目具体状态名称仍以 `PlanMetricState` 为准。
- [Microsoft：Enum.TryParse](https://learn.microsoft.com/en-us/dotnet/api/system.enum.tryparse?view=net-8.0)：解析成功可以来自名称或数字，官方示例另用 `IsDefined` 验证。本实现进一步要求文本与规范枚举名称完全一致，避免数字及组合名称混入摘要。
- [Microsoft SQL LogScout](https://github.com/microsoft/SQL_LogScout)：面向 SQL Server 和 Microsoft CSS 的公开诊断采集工具；参考其显式选择采集场景、本地输出及环境检查方式。核对的[入口源码](https://github.com/microsoft/SQL_LogScout/blob/6cc6c9a78dc48e7044ae40e71de537f3dde06c8c/SQL%20LogScout/SQL_LogScout.ps1#L73)固定在提交 `6cc6c9a78dc48e7044ae40e71de537f3dde06c8c`。本项目继续只收集用户明确选择的本地材料。

## 验证结果

新增 **7 项回归测试**。版本测试先出现 1 项失败，缺失证据的两种报告选项先出现 2 项失败，UTF-8 提示先出现 1 项失败；修复后全部通过。补充快照版本与当前版本分离、禁用规则、默认严重度和状态名称筛选的测试。

Debug/Release 完整解决方案构建各 **0 警告、0 错误**；全量测试各 **2691 通过、0 失败、0 跳过**。其中诊断包相关测试各 46 项，包含真实 DUMP、转储失败、诊断组件失败、分模式日志、取消、隐私及 ZIP 完整性验证。未生成覆盖率报告。

命令、计数、失败复现、TRX/程序集/源码哈希见[加固验证记录](verification/IMP-27-hardening/README.md)。原 IMP-27 目录保留实施时的历史记录；本次没有修改 WPF 界面，也没有重跑 WPF 或人工交互验收。没有连接 SQL Server 或采集生产数据。工作区中此前 IMP-23～27 改动继续保留，未提交或发布。
