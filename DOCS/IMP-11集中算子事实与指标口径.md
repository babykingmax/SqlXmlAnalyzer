# IMP-11：集中提取算子事实并统一指标口径

实施日期：2026-09-09（Asia/Taipei）。范围：当前工作树，承接 IMP-10 的身份模型；未提交或推送既有改动。

审查修复：线程编号现按 `xsd:int` 接受前导符号和 XML 空白，等价拼写统一去重；非法和角色不明的负编号保持保守弃权。新增 33 项回归，Debug/Release 完整构建各 0 警告/错误、各 1389 项测试通过。原初版的 44 项新增记录保留为历史基线，最新验证附件已刷新。详见 [修复、检索依据与加固说明](IMP-11审查修复与加固说明.md)。

## 实现

- Core 新增 `PlanOperatorFactsService`、`PlanOperatorFacts`、`PlanThreadFacts`、`PlanMetric<T>`。每个 RelOp 的对象、谓词、Seek 谓词、输出列、标量表达式、分区、执行模式和数值指标集中读取。
- 遍历算子载荷遇到子 RelOp 即停止，同时单独记录直接子算子的子树成本。外部命名空间、InternalInfo、Statements、QueryPlan 不进入本节点事实。运行计数只取本 RelOp 的直接 RunTimeInformation；Warnings/运行信息里的非标准谓词不能冒充载荷谓词。
- `PlanIdentityAdapter` 构建的 `PlanOperator.Facts` 与既有 XElement 调用共享同一不可变快照。弱引用缓存按源元素保存，子树变化使相关缓存失效；取消会传递到事实提取，已缓存结果也不能绕过取消。
- `PlanGraphRuntimeCountersService`、`PlanGraphRelOpDetailsService` 成为兼容适配器。图节点、表格绑定、属性详情、树、连线、比较运行指标及 JSON 导出消费同一事实。数值型旧接口保留布局兼容值，显示和诊断必须先判断可用状态。

## 指标契约

每项数值记录 `Value`、`State`、`IsPresent`、`IsAvailable`、`Unit`、`Source`、`Kind`、`Aggregation`。状态为 Available / Missing / Invalid / Incomplete / Ambiguous。不可用值为 null，显示 N/A；已观测的 0 仍是 Available。JSON 的状态、估算/实测分类和聚合方式使用可读字符串。

线程属性按原字符串只读保存；计数按 Showplan unsignedLong 解析，使用 decimal 精确求和，避免 double 对大整数的舍入。估算浮点数采用不随当前区域变化的解析规则，拒绝负数、NaN、Infinity 和溢出。缺失读取行不回填输出行；缺失执行次数不回填 1。

| 指标 | 单位、口径 |
| --- | --- |
| EstimatedRows / EstimatedRowsRead | rows/execution；RelOp 的 EstimateRows / EstimatedRowsRead，分别保存 |
| OutputRows / RowsRead | rows；当前算子全部线程计数之和，已包含线程内的多次执行 |
| ThreadExecutions | executions；线程 ActualExecutions 总和，用于描述累计工作量，不能直接作为并行基数比较的分母 |
| LogicalExecutions | executions；单个串行计数器的执行次数，或有效 worker 的共同执行次数；推导方式单独标注 |
| RowsPerExecution | rows/execution；总输出行 / 可确定的逻辑执行次数 |
| Rebinds / Rewinds | rebinds / rewinds；全部线程对应计数之和 |
| EstimatedExecutions | executions；两个估算字段均有效时，EstimateRebinds + EstimateRewinds + 1 |
| SubtreeCost / OwnCost | optimizer-cost；估算子树成本 / 子树成本减直接子节点子树成本，缺失子成本时不计算自身成本 |
| EstimatedCpuCost / EstimatedIoCost | optimizer-cost；原始 EstimateCPU / EstimateIO，不能解释为实测时间 |
| AverageRowSize | bytes/row；AvgRowSize，数据大小仍是依据估算行宽的推算 |
| CpuTime | ms；线程 ActualCPUms 求和 |
| ElapsedTime | ms；当前算子线程 ActualElapsedms 最大值，不对父子算子相加，不当作查询总耗时 |
| LogicalReads / PhysicalReads | pages；当前算子的线程读页计数求和 |

直接子成本求差只容忍相对于子树成本极小的浮点舍入误差；显著负值和非有限结果标 Invalid。任一线程缺少该计数时，该聚合项保持 Missing/Incomplete，不能把部分线程之和显示成完整总量。重复 Thread 编号保留两份原始记录，聚合标 Ambiguous，避免按线程字典覆盖数据。

## 串行、worker、协调线程与循环执行

1. 串行：输出 100,000、执行 1,000 次，对应单次输出 100；缺失执行次数或执行次数为 0 时不推导单次行数。
2. 并行：16 个 worker 各输出 1,000、各执行 1 次，总输出为 16,000、逻辑执行次数为 1，与估算 16,000 匹配。不能把分母取为 16。
3. 并行循环：有效 worker 都执行 E 次时，总输出除以 E；worker 执行次数不一致时，无法仅凭这些计数还原共同调用边界，标 Ambiguous，不直接使用 Max 或 Sum。
4. 协调线程：仅有 Thread 0 时作为单计数器处理。存在 worker 时，Thread 0 的实际输出仍保留在总输出中；若协调线程也有非零输出，单次比较的分母保持未知。零输出协调线程不增加 worker 数或共同执行次数。
5. 空闲 worker：输出 0 且执行 0 次，不进入共同执行次数计算，但在完整 worker 行分布中保留 0。执行 0 却输出非零的矛盾数据不能用于单次比较。
6. 线程倾斜只使用编号已知且完整、无重复的 worker 行分布，排除协调线程。线程顺序不影响总量、共同执行次数和分布指标。

这些是保守的分析约定。新增夹具是用于复现语义反例的合成 XML，不宣称来自真实 SQL Server 运行；更多真实并行/循环计划的 DBA 核验继续按 IMP-14/20 的验收矩阵推进。

## 消费迁移与行为差异

- RULE004、RULE030 使用相同的 EstimatedRows / RowsPerExecution。RULE011、参数嗅探的行偏差、RULE012、RULE016 同样使用明确的执行口径；缺失运行证据不再触发基数或零行误报。原有严重度阈值未改动，但修正输入口径可能减少或改变告警。
- RULE009/RULE033 与图的 worker 分布使用同一事实；阈值仍由各条规则决定。
- RULE001 及图的本节点隐式转换只读取本算子标量表达式。对象/残差/Seek 读取共享服务，RULE002、RULE006、RULE034 不再从子节点借取信息。RULE034 在缺少完整输出/读取计数时不宣称已测得读取放大；直接或 IndexScan 载荷中的残差均可识别。
- RULE022、图中自身成本和节点详情使用同一成本事实。无对象证据的 Scan 不再被推断为堆表。缺失估算行、读取行、执行次数、行宽、自身成本显示 N/A。
- 图中行数仍展示总输出；节点偏差颜色、连线偏差使用单次可比行数。连线说明同时列明线程累计次数、逻辑次数和输出口径；无法比较时不显示偏差倍数。
- CLI `read` / `scan` 的 Plan → Batches → Statements → QueryPlans → Operators 下新增 `Facts`。无任何可用或原始事实的空算子省略该字段，防止损坏输入反复展开空指标；内存模型仍持有完整缺失状态。
- 未增加规则 ID，也未更改 RuleConfiguration.json。规则诊断协议、语义去重、根因措辞和完整报告模板仍按 IMP-12/14/22 实施；计划级规则摘要仍可出现在当前兼容节点入口，不应当作本算子的事实。

`read` / `scan` 原本属于未脱敏结果。Facts 中的对象、谓词及线程原始属性也属于源计划内容；需要分享时使用已有的脱敏导出入口。新日志只写字段名、数量和状态，不写 SQL、对象名或非法字段原值。

## 异常、DUMP 和日志

- `PlanOperatorFactsService.Read` 接入 `ExceptionPolicy`，未知错误通过 `UnexpectedErrorReporter` 写 Windows minidump 和 `exception.json`，校验 DUMP 后才报告成功；同一异常跨边界去重，仍抛出原始异常。
- XML/文件输入错误和取消属于预期错误，不生成 DUMP；字段格式错误记录 Warning 并保留未知状态。DUMP 写入失败记录 Error，保留异常侧车和原始错误，不能伪报 DUMP 成功。
- Debug：DEBUG、WARN、ERROR、CRITICAL；Release：仅 ERROR、CRITICAL。沿用统一 Logger 的编译模式约束，verbose 参数不能在 Release 打开 Debug/Warning。CRITICAL 对应致命信息。
- 默认日志：`%LOCALAPPDATA%\SqlXmlAnalyzer\log`。DUMP：`%LOCALAPPDATA%\SqlXmlAnalyzer\dumps`，失败后尝试 `%TEMP%\SqlXmlAnalyzer\dumps`。测试输出转储保存在独立临时目录并清理，仓库不保存进程 DUMP。

## 验证和复现

新增 44 项回归（相对开始时的 1312 项），覆盖父子/命名空间边界、直接与载荷谓词、缺失/零/非法/大整数、16 worker、重复执行、协调线程、空闲 worker、线程重排、歧义、成本、缓存失效、取消、跨图表/详情/JSON 一致性、真实 DUMP、DUMP 失败和日志分级。旧测试中依赖虚构默认值的断言改为未知；需要比较单次基数的合成夹具补充显式 ActualExecutions。

运行：

```powershell
.\DOCS\implementation\IMP-11\Verify-IMP11.ps1
.\DOCS\implementation\IMP-11\Run-FactsUiProbe.ps1
```

完整 Debug/Release 构建、测试数及程序集指纹见 [verification.json](implementation/IMP-11/verification.json)；逐项测试见 [Debug TRX](implementation/IMP-11/debug.trx) / [Release TRX](implementation/IMP-11/release.trx)。

生产 WPF 模板的 STA 绑定核验见 [ui-observed.json](implementation/IMP-11/ui-observed.json)：父节点无子对象/谓词，子节点显示输出 100、读取 1,000、Batch，缺失节点显示 N/A。当前环境的 RenderTargetBitmap 在 SoftwareOnly、DrawingVisual 和隐藏呈现宿主尝试中均产生空像素；已移除空白图片，`ScreenshotCreated=false`，**未完成可用截图及完整窗口交互验收**。脚本保留像素校验，在可渲染环境重跑成功后才生成 PNG。CLI 示例和命令退出码见 [cli-observed.json](implementation/IMP-11/cli-observed.json)。
