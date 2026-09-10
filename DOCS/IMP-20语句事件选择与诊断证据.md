# IMP-20：语句 / 事件选择与诊断证据

**审查加固更新（2026-09-09）：** 三项 P2 问题已修复，新增 15 项回归，Debug/Release 全量各 2273 项通过、构建各 0 警告/错误；真实 WPF 验证均零绑定错误。当前修复、技术依据和截图见[审查修复与加固说明](./IMP-20审查修复与加固说明.md)及[加固验证摘要](./verification/IMP-20/hardening-summary.json)。下文初次实施的 29 项新增、2258 项全量及截图保留为历史验收记录。

实施日期：2026-09-09。范围对应[实施规划](./软件改善实施规划.md) IMP-20 的四项要求；不改变规则 ID、默认严重度、SQL 改写资格或输入预算。

## 行为与边界

执行计划工作区顶部显示文档来源、可用采集时间、引擎/Schema 版本、文档能力、读取状态和当前选择。Batch 下列出全部 Statement / QueryPlan；没有算子的语句仍可选择。首次打开默认选择第一 Batch 的第一语句，游标等多 QueryPlan 语句按计划序号分别列出。

选择同步完整 StatementText、算子图、算子树、指标表、属性、缺失索引、SQL 预览、参数/统计信息和问题列表。SQL 不再截断为 800 字符。算子始终来自原始 XDocument，图的加载、折叠和重新布局都限定当前 QueryPlan；不会把复制 XML 后重新生成的身份当作原始身份。不存在算子的语句明确显示“未采集”，并清除旧图、属性、证据和统计状态。

问题列表分别显示运行状态、严重度和置信度。Hit 诊断及 Skipped / Failed 记录按当前语句/计划过滤；文档级诊断保留并标注范围，NoHit 进入计数。无匹配、缺少运行证据和规则失败有不同说明，空列表不等于健康。

选择问题高亮对应算子，并同步指标表与属性。选择证据可跨 Batch / Statement / QueryPlan 定位其实际来源；“查看原始 SQL/XML”显示完整身份、XML 路径、可用行列、原 SQL 和原始元素片段。图或指标表选择节点时，诊断详情也切换至同一节点。定位使用 DocumentId、BatchOrdinal、StatementOrdinal、QueryPlanOrdinal、NodeId 和 OperatorOrdinal；局部 NodeId、对象名、SQL 文本都不是查找键。

死锁 XML/XEL 的事件选择直接传递原始 DeadlockInput，保留事件/子事件序号、采集时间、来源路径与 XEL 字节位置。规范化的分析副本与原始带命名空间的 XML 分别保留；证据路径映射回原始节点，并使用原始行号。快速切换会取消旧请求，只有最后一次请求可以提交。切换和清空会移除旧进程、资源、图、诊断、证据及推演状态。

死锁诊断的证据下拉框定位进程、资源及可用关系；图中以蓝色光晕突出目标，保留受害者与环成员本身的颜色。修复了同一对象上不同锁资源双击时按名称取首项的问题。依赖推演始终显示“死锁快照不包含原始时间序列”，不冒充真实回放。

当前选择的复制/便携文本报告保留诊断 ID 和完整证据。既有 HTML 报告仍以整份计划文档生成，诊断 ID 与原始身份一致；完整报告范围与脱敏预览改造继续归 IMP-22。多语句选择不会自动生成或应用改写，也不会借用之前单语句的审核提案；既有单语句审核流程保留。跨启动的工作区/事件选择持久化、XEL 聚合、全面键盘与布局改造不在本步范围。

## 实现位置

| 组件 | 职责 |
| --- | --- |
| `Core/ViewModels/PlanWorkspaceViewModel.cs` | 范围选择、诊断投影、原始证据解析、陈旧身份拒绝 |
| `Core/ViewModels/DeadlockWorkspaceViewModel.cs` | 事件上下文、诊断证据、原始路径/行列映射、源修改失效 |
| `Services/PlanWorkspaceUiActionService.cs` | 将同一选择应用到 WPF SQL、图、树、表、属性和统计区域 |
| `Services/DeadlockWorkspaceUiActionService.cs` | 清理旧事件视图，同步列表及图上的证据高亮 |
| `Services/DocumentAnalysisUiActionService.cs` / `XelDeadlockUiActionService.cs` | 原始事件传递、取消及过期结果保护 |
| `PlanGraphControl` / `PlanTreeService` | 当前范围的原始节点、完整身份选中及布局 |

`DeadlockInput.OriginalElement` 现在引用读取结果中的原始元素，以保留行信息；消费者仅用于读取。图解析继续使用单独的去命名空间副本。任一来源修改后拒绝旧证据定位，要求重新分析。

## 异常、DUMP 和日志

选择/证据边界使用已有 `ExceptionPolicy` 和 `UnexpectedErrorReporter`。取消、陈旧来源、无效范围、I/O 等预期异常给出错误或取消说明，不要求 DUMP；未知异常记录 CRITICAL，生成并校验 Windows minidump 和异常侧车，同一异常实例去重。DUMP 无法创建或诊断组件自身失败时保留原异常和明确失败说明，不显示成功。

默认 DUMP 目录为 `%LOCALAPPDATA%\SqlXmlAnalyzer\dumps`，失败时尝试 `%TEMP%\SqlXmlAnalyzer\dumps`；日志目录沿用 `%LOCALAPPDATA%\SqlXmlAnalyzer\log`。Debug 记录 DEBUG / WARN / ERROR / CRITICAL；Release 只记录 ERROR / CRITICAL，调用参数不能放开限制。新增正常路径日志只含阶段和计数，不记录 SQL、参数值或对象名。

## 单元测试与验收

新增 29 项测试：`WorkspaceSelectionTests` 21 项、`WorkspaceSelectionDiagnosticTests` 7 项，以及 `DeadlockEventSelectionTests` 中的类型化选择回归 1 项。

覆盖重复/缺失 NodeId、多 Batch、多 QueryPlan、嵌套语句、多根、无算子语句、长 SQL、精确索引归属、跨范围文档证据、Skipped / Failed / NoHit、陈旧/外来选择、源 XML 修改、同名资源、多事件同进程 ID、清空、重新布局隔离、DUMP 成功/失败/去重、诊断组件失败及 Debug/Release 日志过滤。

生产 MainWindow 验证通过真实文件打开和 WPF 选择控件执行；不是重画的示意图。脚本核对单语句审核保留、多语句不串审核、重复 NodeId、图的可见选中、指标表、SQL/原始 XML、无算子清空、快速事件切换、原始带命名空间路径/行号、图高亮、清空事件选择器和零绑定错误。夹具为仓库内的合成回归样例，本步不声称已验证真实数据库并发时序。

最终构建、全量测试计数、TRX/日志 SHA-256 和两配置 WPF 截图目录见[验证摘要](./verification/IMP-20/summary.json)。原始构建日志、TRX 和 DUMP 不纳入 Git。

最终结果：Debug / Release 完整构建各 0 警告、0 错误；全量各 2258 项通过、0 失败、0 跳过。两配置生产 MainWindow 验证均通过，绑定错误均为 0。

| 关键状态 | Debug 截图 | Release 截图 |
| --- | --- | --- |
| 第二语句与问题选中 | [图与问题](./verification/IMP-20/wpf-Debug-440ab44ab45d4e1ba2a3e8b1a8ead4ae/plan-statement-2.png) | [图与问题](./verification/IMP-20/wpf-Release-b89d779d5bc340618617c0b5d9f4d163/plan-statement-2.png) |
| 对应原 SQL / XML | [原始位置](./verification/IMP-20/wpf-Debug-440ab44ab45d4e1ba2a3e8b1a8ead4ae/plan-evidence-source.png) | [原始位置](./verification/IMP-20/wpf-Release-b89d779d5bc340618617c0b5d9f4d163/plan-evidence-source.png) |
| 无算子语句 | [未采集空态](./verification/IMP-20/wpf-Debug-440ab44ab45d4e1ba2a3e8b1a8ead4ae/plan-no-operators.png) | [未采集空态](./verification/IMP-20/wpf-Release-b89d779d5bc340618617c0b5d9f4d163/plan-no-operators.png) |
| 第二死锁事件 | [进程、资源与图](./verification/IMP-20/wpf-Debug-440ab44ab45d4e1ba2a3e8b1a8ead4ae/deadlock-event-2.png) | [进程、资源与图](./verification/IMP-20/wpf-Release-b89d779d5bc340618617c0b5d9f4d163/deadlock-event-2.png) |
| 死锁证据原始位置 | [命名空间与行号](./verification/IMP-20/wpf-Debug-440ab44ab45d4e1ba2a3e8b1a8ead4ae/deadlock-source.png) | [命名空间与行号](./verification/IMP-20/wpf-Release-b89d779d5bc340618617c0b5d9f4d163/deadlock-source.png) |

定位断言分别保存在 [Debug 记录](./verification/IMP-20/wpf-Debug-440ab44ab45d4e1ba2a3e8b1a8ead4ae/verification.json)和 [Release 记录](./verification/IMP-20/wpf-Release-b89d779d5bc340618617c0b5d9f4d163/verification.json)。可用 `Write-VerificationSummary.ps1` 核对两配置的构建、TRX、WPF 结果和图片摘要。

## 操作脚本

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug
dotnet build SqlXmlAnalyzer.sln -c Release
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release
& DOCS/verification/IMP-20/Run-WpfVerification.ps1 -Configuration Debug
& DOCS/verification/IMP-20/Run-WpfVerification.ps1 -Configuration Release
```

人工复核：

1. 打开 `SqlXmlAnalyzer.Tests/TestData/imp14_cardinality_residual.sqlplan`，选 Statement 2；SQL 为 `FROM U`，图和指标表只有当前 Node 0，输出行 10,000、读取行 100,000。
2. 选择“基数估计偏差”，核对严重度与置信度两列；选择 OutputRows / RowsRead 证据并打开原始位置，路径指向第二 StmtSimple，XML 对象为 U。
3. 打开 `imp10_scoped_identities.sqlplan`，切至 Batch 2 的 StmtUseDb；SQL 保留 USE，图/表/属性为空且说明未采集算子。选择游标的 QueryPlan 2，确认只显示该计划节点。
4. 打开 `deadlock_multiple_events.xdl`，快速切换事件 2 → 1 → 2；最终进程只有 p3/p4，受害者为 p3，诊断证据路径包含 `event[2]`。在“证据 SQL/XML”选择关系，核对进程、资源光晕及原始行号。
5. 开关依赖推演，性质说明始终可见。清空结果后，图、表、诊断、SQL、证据和事件选择器均清除；待完成请求不会重新填回结果。
