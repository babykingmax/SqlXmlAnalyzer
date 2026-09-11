# IMP-08 展示纠错与证据边界

实施日期：2026-09-08。范围：R07、R12、R15、R22 对应的即时展示修正，区分运行观测、估算与工具假设。完整死锁模型、模拟校准和计划身份匹配仍由 IMP-13/16/17 完成。

审查修复更新：依赖推演及沙盒关键说明已改用动态主题文字资源；新增 8 项回归，Debug/Release 各 1183 项通过，真实窗口宿主的主题切换与对比度验证通过。见[审查修复与加固证据](IMP-08审查修复与加固说明.md)。下方保留初次实施的 1175 项测试及矩阵历史记录。

## 当前行为

| 场景 | 实施前 | 实施后 |
| --- | --- | --- |
| 实际 A 对估算 B | B 缺失运行指标按 0 计算负差值 | B 的指标及差值为 N/A；A 保留已观测值，但无可比较的另一侧时差值为 N/A |
| 真实 0 / 部分线程计数 | 与缺失混淆，可能使用部分汇总 | 完整真实 0 可计算差值；任一线程缺失或非法，则该指标未知；各指标独立判定 |
| A/B 成本 | 卡片称“性能变动幅度”，B 预设为“优化后” | 卡片称“估算成本变化”，B 为“待比较”；树明确标注估算子树成本，两处 Recost 列标注“工具估算” |
| 死锁合成步骤 | 描述为回放死锁形成过程 | 固定“依赖推演”说明，并明确步骤不代表真实锁事件顺序、播放间隔不是事件耗时 |
| REPEATABLE READ / SERIALIZABLE，无 Range 模式 | 仅据隔离级别确认范围锁死锁 | 不产生范围锁观测条目；必须存在合法 Range 锁模式 |
| 观测到 Range 模式 | 直接宣称 SERIALIZABLE / HOLDLOCK 及死锁原因 | 列出模式证据，不据此推断隔离级别、提示使用或根因 |
| 索引沙盒收益 | 未校准模型给出精确收益百分比 | UI 不再调用该收益模拟器，显示 N/A 和模型未校准说明 |
| 覆盖索引 / Tipping Point | “始终 Seek”“已触发退化”等确定性结论 | 候选列覆盖待验证；参考区间明确为工具假设，不能据此确定访问路径或性能收益 |

## 实现契约

`PlanComparisonRuntimeMetricsService` 仅读取当前算子的直属运行时计数，沿用耗时取线程最大值、逻辑读与读取行数累计的现有口径。属性按 Showplan unsignedLong 解析；缺失、空串、NaN、Infinity、负数、小数、溢出及部分线程缺失均返回 null。`RuntimeMetricDelta.Value` / `Delta` 改为 `double?`，两侧均已观测才相减。Elapsed、Logical reads 始终显示；Rows read 在任一侧存在有效值时显示。分别标注 ms、页、行。新日志只包含字段名和操作状态，不记录输入属性原文。

每个算子使用自己的 XML 命名空间读取；父算子不读取子算子运行数据。完整线程的先后顺序不改变结果。这里的完整指所有已提供计数均有效，不证明采集文件包含服务器上全部线程。数值接口仍为 double，不宣称已完成大整数精度模型或跨采集条件可比性校准。

`AnalysisDisplayText` 集中保存比较、依赖推演和沙盒说明。依赖推演说明直接固定在生产控件中，不随 ViewModel 当前步骤、播放状态或筛选状态变化；主工作区和旧视图入口均改名。范围锁观察支持标准及转换模式：RangeS-S、RangeS-U、RangeI-N、RangeX-X、RangeI-S、RangeI-U、RangeI-X、RangeX-S、RangeX-U；不通过 `Contains("RANGE")` 或隔离级别猜测。

范围锁条目由 High 诊断调整为 Info 观测；这不是对死锁严重度降级，而是撤销未经证明的范围锁根因判定。原专家级启发式条目改标“待验证假设”，两个诊断面板持续提示不能视作已证明根因；移除访问顺序建议中的 100% 根绝承诺。未改变这些模式的其他检测算法或规则 ID。

索引规则评分仍保留，并明确不代表性能收益。沙盒的行数、行宽来自计划估算或工具默认值，作为可编辑假设；无效、非有限或导致参考区间溢出的输入显示 N/A。覆盖列判断和参考区间算法未在此校准。`EstimatedCostReductionPercent` 展示属性已撤销，替换为非数值的 `CostReductionSummary`；底层 `CostImpactSimulator` 仍保留给 IMP-16 修复，不能把其数值重新用于收益承诺。

## 异常、DUMP 与日志

比较控制器新增可注入指标读取接口和未知异常边界：通过 `ExceptionPolicy` 生成 DUMP，重新抛出原异常，不伪造空比较结果。沙盒重算发生未知异常时记录 DUMP、清空旧 DDL 和评分、显示失败；即使属性通知再次失败，也保留原异常。范围锁展示故障同样进入统一异常策略。现有解析与应用全局异常策略继续有效。

复用真实 `WindowsMiniDumpWriter`、异常实例去重、DUMP 格式校验及 `exception.json` 托管堆栈。目录策略继承 IMP-04：默认 `%LOCALAPPDATA%\SqlXmlAnalyzer\dumps`，失败时尝试 `%TEMP%\SqlXmlAnalyzer\dumps`；无法生成时明确记录失败，诊断失败不能覆盖原异常。

Debug 输出 DEBUG / WARN / ERROR / CRITICAL；Release 仅 ERROR / CRITICAL，强制 verbose 不能放开低级别输出。未知故障与已知坏输入分开：非法指标及沙盒无效假设记录 WARN，正常缺失不当作错误。新增回归同时验证两个配置的日志级别和原始无效字段值不进入日志。

## 官方依据

2026-09-08 按 Microsoft 官方文档优先核对。搜索工具连接失败后，通过 HTTPS 直接读取官方页面。

1. [事务隔离级别](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-transaction-isolation-level-transact-sql)：REPEATABLE READ 允许幻读；SERIALIZABLE 的范围保护不能反推某次死锁已经发生范围锁冲突。
2. [锁与行版本控制指南](https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-transaction-locking-and-row-versioning-guide)：标准 Range 锁模式及转换模式的依据。
3. [索引设计指南](https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-index-design-guide)：覆盖可以减少访问基础表的需要，但不能据此承诺特定访问路径或精确收益。
4. [死锁指南](https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-deadlocks-guide)：死锁图提供受害者、进程、资源以及持有/等待关系。应用合成步骤没有额外锁事件时间证据，因此只作依赖解释。
5. [Showplan SQL Server 2019 XSD](https://schemas.microsoft.com/sqlserver/2004/07/showplan/sql2019/showplanxml.xsd)：ActualElapsedms、ActualLogicalReads 和 ActualRowsRead 均为可选 unsignedLong。复核仓库原始 XSD，SHA-256 `845B3FAA55748D442DFC71071315FF1EBB611E4E298E8D06A21EBF946B8C93A8`。

## 验证与证据

新增 **40 项单元测试**：25 项展示反例、12 项指标独立性/作用域/顺序/状态/假设输入测试、3 项真实 DUMP 与分配置日志测试。更新旧测试中明确要求误导文案的断言，数值与结构断言保留。

首批 25 项在实施前 **23 失败、2 通过**，见 [红灯 TRX](implementation/IMP-08/tests/imp08-red.trx)和[原测试源码](implementation/IMP-08/RedMisleadingDisplayTests.cs.txt)。定向回归 **93 / 93** 通过，见 [TRX](implementation/IMP-08/tests/imp08-focused.trx)。

| 最终验证 | 结果 | 证据 |
| --- | --- | --- |
| Debug 完整非增量构建、全套测试 | 0 警告、0 错误；1175 / 1175 通过、0 跳过；42 / 42 验收工具回归通过 | [摘要](acceptance/IMP-02/runs/20260908T132838998Z_c5b1e0766cfc_a92698f8/summary.json)、[构建](acceptance/IMP-02/runs/20260908T132838998Z_c5b1e0766cfc_a92698f8/build.stdout.txt)、[TRX](acceptance/IMP-02/runs/20260908T132838998Z_c5b1e0766cfc_a92698f8/test-output/existing-suite.trx) |
| Release 完整非增量构建、全套测试 | 0 警告、0 错误；1175 / 1175 通过、0 跳过；42 / 42 验收工具回归通过 | [摘要](acceptance/IMP-02/runs/20260908T132939519Z_c5b1e0766cfc_d40aa08b/summary.json)、[构建](acceptance/IMP-02/runs/20260908T132939519Z_c5b1e0766cfc_d40aa08b/build.stdout.txt)、[TRX](acceptance/IMP-02/runs/20260908T132939519Z_c5b1e0766cfc_d40aa08b/test-output/existing-suite.trx) |
| 真实 WPF 独立进程 | 3 个比较场景、9 个推演状态及沙盒编辑后绑定通过；exit 0 | [观察值](implementation/IMP-08/ui/observed.json)、[进程与程序集指纹](implementation/IMP-08/ui-process.json)、[探针源码](implementation/IMP-08/DisplayUiProbe.cs.txt) |

WPF 使用真实 App 资源、DI、MainWindow、比较刷新服务、死锁文件分析及事件选择入口。比较验证“实际 A 对估算 B → 真实 0 → 再次估算 B”；推演覆盖初态、自动播放、暂停、首步、末步及关键环筛选、重置、两个死锁事件切换和旧视图空事件。固定说明有非零布局尺寸且未折叠，截图已人工核对：[估算 B 为 N/A](implementation/IMP-08/ui/comparison-estimated.png)、[末步/筛选仍显示推演说明](implementation/IMP-08/ui/inference-last-filtered.png)、[沙盒编辑后无收益百分比](implementation/IMP-08/ui/sandbox.png)。

前两轮宿主运行失败保留记录：第一轮未显式布局比较视图，高度为 0；第二轮业务断言完成后在退出阶段出现字体布局异常。宿主补齐离屏布局，并在 App 资源释放前优先结束自己拥有的 Dispatcher，最终正常退出。未忽略验证阶段异常或为此修改产品代码。[首次记录](implementation/IMP-08/ui-attempt1-stderr.txt)、[退出阶段记录](implementation/IMP-08/ui-attempt2-stderr.txt)。所有验证均未显示用户窗口或执行 SQL Server，不等同于全部 DPI/主题验收。

两配置矩阵为 **13 项最低条件满足、9 项 NotMet、0 项探针执行失败**。新增通过 R07/R15/R22；R12 仍观测到模拟顺序依赖（100 与 65），UI 已撤下该预测，算法修复保留给 IMP-16。没有更改 v2.5 判定条件，整体退出码仍为 1，不宣称全部问题关闭。运行中保护快照无漂移，文档更新后另行核对构建源码指纹，见 [verification.json](implementation/IMP-08/verification.json)。未生成覆盖率报告。

复验（PowerShell 7）：

```powershell
$env:UseArtifactsOutput = 'true'
$env:ArtifactsPath = Join-Path $PWD 'bin/imp08-20260908'
./DOCS/acceptance/IMP-02/Run-AcceptanceMatrix.ps1 -Configuration Debug
./DOCS/acceptance/IMP-02/Run-AcceptanceMatrix.ps1 -Configuration Release
./DOCS/implementation/IMP-08/Run-DisplayUiProbe.ps1
```

独立 bin 产物目录避开默认 WPF 中间文件的同步程序锁定；编译配置和依赖版本未调整。全部模型、规则、报告出口和导出文案的一致性继续由后续 IMP 步骤验收。
