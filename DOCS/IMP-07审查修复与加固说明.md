# IMP-07 审查修复与加固

日期：2026-09-08。修复 IMP-07 审查确认的 P2 回归，并加固同一实际行数观测标记的消费链路。初次实施与历史证据保留在 [字段映射说明](IMP-07字段映射与绑定修正说明.md)。

## 原因与修复

缺失实际输出行在 IMP-07 中规范为 `N/A`，但 `PlanGraphCostUiActionService` 仍通过字符串是否非空判断实际数据是否存在。完整图加载在节点构建之后再次计算成本，因此估算计划会把数值占位 0 当作实际行数，将非零自身成本重算为 0。此前只验证节点构建及表格字段的测试没有覆盖这一阶段。

现在 `PlanGraphRuntimeCountersResult.HasActual` 经 `PlanGraphNodeBuildResult.HasActualRows`、`PlanGraphNodeUiActionService` 传递至 `PlanNodeViewModel.HasActualRows`，再用于成本和连线计算。显示字符串不承担事实判断：`N/A`、空串或本地化占位的变化不能改变成本与连线指标。

| 输入情况 | HasActualRows | 重算成本 | 连线行数和大小 |
| --- | --- | --- | --- |
| 无运行时信息、空运行时节点 | false | 保留扣除直接子节点成本后的 OwnCost | 使用估算行数 |
| 非法计数、部分线程缺失或非法 | false | 保留 OwnCost，不使用残缺汇总 | 使用估算行数，不输出实际指标及连线偏差提示 |
| 所有当前算子线程均有合法输出计数，合计为 0 | true | 估算行数为正时为 0 | 保留 0 行、0 字节 |
| 所有当前算子线程计数完整且有效 | true | 使用既有行数比例公式 | 使用实际输出行数 |
| 输出完整，但读取行缺失或非法 | true | 仍可使用实际输出行 | 输出与读取计数独立，不互相代替 |

父节点不能使用子节点的运行时信息。`HasActualRows` 只表示当前算子所有已提供线程计数均合法，不证明输入文件包含服务器上所有工作线程。

连线的数值选择、颜色和提示统一使用该标记，修复实际 0 行被替换成估算值以及部分有效计数被视为完整观测的问题。节点行数模式、`ActualRowsDisplay`、实际行数颜色和 `SkewWarning` 也使用该标记，未知总数不产生这些节点偏差提示。连线输入记录的第三个参数由显示字符串改为必传布尔值，仓库调用方与测试均已更新，防止继续从文本猜测。

## 异常与日志

沿用 IMP-07 的异常边界：未知映射故障经 `ExceptionPolicy` 和真实 `WindowsMiniDumpWriter` 生成经过格式校验的 DUMP，重新抛出原异常；诊断失败不能替换原异常。正常缺失不是错误；非法行计数记录 WARN，不记录原始属性值、SQL 或标识符。

成本重算新增 DEBUG 汇总，仅记录节点数量及完整实际行数节点数量。Debug 输出 DEBUG / WARN / ERROR / CRITICAL；Release 仅 ERROR / CRITICAL，强制 verbose 也不能打开低级别输出。原有日志回归已增加成本重算消息断言，两配置均验证通过。

## 检索依据

按用户要求优先核对 Microsoft 官方定义。搜索工具连接失败，改用 HTTPS 直接读取官方页面及其 GitHub 文档源文件，以下内容于 2026-09-08 核对：

1. [显示估算执行计划](https://learn.microsoft.com/en-us/sql/relational-databases/performance/display-the-estimated-execution-plan)：生成估算计划不会执行查询，因此不包含运行时信息。[MicrosoftDocs 源文件](https://github.com/MicrosoftDocs/sql-docs/blob/live/docs/relational-databases/performance/display-the-estimated-execution-plan.md)。
2. [显示实际执行计划](https://learn.microsoft.com/en-us/sql/relational-databases/performance/display-an-actual-execution-plan)：实际计划在执行后生成，包含运行时指标及警告。[MicrosoftDocs 源文件](https://github.com/MicrosoftDocs/sql-docs/blob/live/docs/relational-databases/performance/display-an-actual-execution-plan.md)。
3. [Microsoft Showplan SQL Server 2019 XSD](https://schemas.microsoft.com/sqlserver/2004/07/showplan/sql2019/showplanxml.xsd)：`RunTimeInformation` 可缺省；线程内 `ActualRows` 为必需 unsignedLong，`ActualRowsRead` 为可选 unsignedLong。复核仓库原始 XSD，SHA-256 为 `845B3FAA55748D442DFC71071315FF1EBB611E4E298E8D06A21EBF946B8C93A8`。

这些官方资料已足以确定“未提供运行时计数”与“实际为 0”的差别，无需用 CSS 案例或社区实现推断该字段。本次没有引入外部代码或依赖。

## 验证

新增 29 项回归：25 项完整加载/重算链路测试和 4 项连线指标测试。覆盖估算计划、空节点、缺失/非法/负数/小数/溢出、部分线程、真实 0、完整多线程、输出/读取独立、父子成本及作用域、显示占位变化、连线与节点未知状态。

首批 25 项测试在修复前得到 **17 失败、8 通过**，保留 [失败记录](implementation/IMP-07/hardening/tests/hardening-red.trx)和[当时测试源码](implementation/IMP-07/hardening/RedPlanRuntimeObservationFlowTests.cs.txt)。最终定向回归 **335 / 335** 通过，见 [TRX](implementation/IMP-07/hardening/tests/hardening-focused-final.trx)。

| 最终验证 | 结果 | 证据 |
| --- | --- | --- |
| Debug 完整非增量构建与全套测试 | 0 警告、0 错误；1135 / 1135 通过，0 跳过；42 / 42 验收工具回归通过 | [摘要](acceptance/IMP-02/runs/20260908T130755683Z_c5b1e0766cfc_a4518124/summary.json)、[构建](acceptance/IMP-02/runs/20260908T130755683Z_c5b1e0766cfc_a4518124/build.stdout.txt)、[TRX](acceptance/IMP-02/runs/20260908T130755683Z_c5b1e0766cfc_a4518124/test-output/existing-suite.trx) |
| Release 完整非增量构建与全套测试 | 0 警告、0 错误；1135 / 1135 通过，0 跳过；42 / 42 验收工具回归通过 | [摘要](acceptance/IMP-02/runs/20260908T130847895Z_c5b1e0766cfc_e528e6ee/summary.json)、[构建](acceptance/IMP-02/runs/20260908T130847895Z_c5b1e0766cfc_e528e6ee/build.stdout.txt)、[TRX](acceptance/IMP-02/runs/20260908T130847895Z_c5b1e0766cfc_e528e6ee/test-output/existing-suite.trx) |
| 独立 WPF 进程 | 实际 App 资源、DI、MainWindow 文件分析入口及旧 PlanView 加载通过；两处表格各 5 个运行时场景通过，原输出/读取/模式/详情检查仍通过；进程 exit 0 | [进程与程序集指纹](implementation/IMP-07/hardening/ui-process.json)、[观察值](implementation/IMP-07/hardening/ui/observed.json)、[探针源码](implementation/IMP-07/hardening/FieldMappingUiProbe.cs.txt) |

WPF 场景依次为估算、真实 0、部分线程、250 实际行/100 估算行、再次打开估算计划。单元格使用真实生产 ItemsSource 和 Binding，校验实际输出、OwnCost、Recost 以及绑定状态。离屏渲染截图已核对：[估算计划保持成本](implementation/IMP-07/hardening/ui/PlanWorkspaceView-estimated.png)、[真实 0 保持 0](implementation/IMP-07/hardening/ui/PlanView-zero.png)。没有显示用户窗口或执行 SQL；此检查不等于所有 DPI、主题的视觉验收。

矩阵仍为 **10 项最低条件满足、12 项 NotMet、0 项探针执行失败**，R06/R11/R21 最低条件保持通过。退出码 1 表示后续改善仍未完成；没有放宽判定器或宣称所有评审问题关闭。两次运行中受保护输入无漂移，文档更新后另行核对构建源码指纹。统一结果见 [verification.json](implementation/IMP-07/hardening/verification.json)。未生成覆盖率报告。

复验命令（PowerShell 7）：

```powershell
$env:UseArtifactsOutput = 'true'
$env:ArtifactsPath = Join-Path $PWD 'bin/imp07-hardening-20260908'
./DOCS/acceptance/IMP-02/Run-AcceptanceMatrix.ps1 -Configuration Debug
./DOCS/acceptance/IMP-02/Run-AcceptanceMatrix.ps1 -Configuration Release
./DOCS/implementation/IMP-07/hardening/Run-FieldMappingUiProbe.ps1
```

独立 bin 产物目录延续已有验证布局，避开默认 WPF 中间文件的同步程序锁定。编译配置与依赖版本未调整。

## 范围

Recost 沿用应用既有估算公式，不能解释为 SQL Server 实测成本或耗时。已知计数的颜色阈值、偏差方向文案、其他规则计算、double 精度和全部出口的一致性没有在此扩展，仍按后续 IMP 步骤验收。本次加固限定为实际输出行观测标记的传递及上述图形消费者。
