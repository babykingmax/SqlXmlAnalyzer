# 执行计划工作区：当前设计、竞品差距与后续方案

评估日期：2026-09-11。本文是执行计划工作区当前设计与后续待办的主文档；历史架构审查、历史失败和各阶段验证结果仍保留在原文档中。本次更新文档，不实施下列 PW-01–PW-06，也不产生新的实机或性能验收结果。

依据为当前源码、既有生产 WPF 离屏截图及本轮核验的官方产品文档。没有在本轮操作 Plan Explorer 或 dbForge 实机界面，不将文档功能说明当作实机体验结论。

## 1. 产品定位与设计原则

本项目已经具备较完整的离线执行计划诊断、问题定位、快照比较、索引审核、SQL 改写提案及报告能力。现阶段的主要差距是可读的画布空间、诊断范围表达、问题摘要、证据层级和工作流；在线采集与受控性能实验属于后续功能建设。

计划图继续采用 WPF＋Nodify，保留现有分析模型、完整 `PlanLocation`、规则 ID、报告契约和 64 节点分页。死锁与 A/B 工作区不因本轮计划图设计而整体改造。必须区分三类能力：展示已采集证据、主动采集证据、通过实验验证改善。读取实际计划中的运行计数不等于执行了查询；索引评分不等于实测收益；隔离场景通过不等于普遍语义等价或性能改善。

## 2. 已实现的工作区与图形设计

以下为已经落地的实现，与第 5 节的未实施待办分开记录。

| 部分 | 当前实现 | 主要代码或证据 |
| --- | --- | --- |
| 信息架构 | 顶部摘要与语句选择；左侧问题／索引／检查状态；中央计划图／热点算子；右侧详情；底部 SQL／XML／参数统计／改写材料按需展开 | [工作区 XAML](../Views/PlanWorkspaceView.xaml)、[展示状态](../Core/ViewModels/PlanWorkspaceViewModel.Presentation.cs) |
| 选择与诊断 | 真实问题与 Skipped／Failed 分开，支持筛选；问题、语句和节点使用完整身份联动；保留全部检查记录 | [工作区 ViewModel](../Core/ViewModels/PlanWorkspaceViewModel.cs) |
| 指标 | 区分实际总行、每次执行行、估算行及可比偏差；未知值与零值分开；热点表与图复用事实模型 | [算子事实](../SqlXmlAnalyzer.Core/Models/PlanOperatorFacts.cs)、[节点展示](../ViewModels/PlanNodeViewModel.cs) |
| 节点外观 | 260×88 DIP 卡片，24 DIP 图标、13 DIP 标题；名称、对象简称、主指标分层；严重度徽标与蓝色选中轮廓分开；折叠按钮置于卡片内 | [图 XAML](../PlanGraphControl.xaml)、[共享尺寸](../Core/Services/PlanGraphNodeMetrics.cs) |
| 提示框 | 浅色主题使用浅底，深色主题使用灰蓝底；主题文字、细边框、圆角与留白 | [提示框验证](verification/plan-workspace-redesign/README.md#节点提示框背景调整2026-09-10) |
| 缩放与定位 | 适应意图保留到节点与视口可测量；缩放和平移同步更新；新操作终止旧动画并使过期定位失效；100% 与加减围绕视口中心，页脚反馈比例 | [图展示与视口时序](../PlanGraphControl.Presentation.cs)、[视口几何服务](../Services/PlanGraphViewportUiActionService.cs) |
| 紧凑布局 | 相邻子树按轮廓压紧，父节点位于输入分支中心；横纵布局保留 XML 输入顺序；尺寸与锚点统一；折叠和分页森林不虚构连接；迭代遍历支持深链 | [布局服务](../Core/Services/PlanGraphLayoutService.cs)、[连接几何](../Core/Services/PlanGraphConnectionGeometryService.cs) |
| 大图导航 | 保留每页 64 节点上限、隐藏数量和跨页关系入口；支持查找、定位、分页与取消 | [分页提交](../PlanGraphControl.Async.cs)、[计划图优化记录](verification/plan-workspace-redesign/graph-polish.md) |

最近一次计划图代码交付的历史记录为：Debug／Release 构建均 0 警告、0 错误，两种配置全量测试各 3066 通过；Release 离屏探针有 1284 项断言、24 组矩阵、0 绑定错误和 36 张截图。该结果属于 2026-09-10 代码交付，来源为[计划图验证记录](verification/plan-workspace-redesign/graph-polish.md)及[结果摘要](verification/plan-workspace-redesign/graph-polish-test-results.json)，不是本次文档更新重新运行的成绩。过程中的一次 Debug 自转储附近停滞及其复跑结果继续保留，不将其写成已修复。

本轮提交前的构建、测试及文件证据另见[publish-validation.json](verification/plan-workspace-redesign/publish-validation.json)，与上述历史离屏结果分开记录。发布前全量回归通过也不能替代待实施功能、人工可用性或真实设备验收。

已有适应测试证明节点边界位于视口内、连续两次适应结果一致；它不证明适应后的文字可读。工作区高度比例测试测量整个图控件，包含工具栏和分页区，不能替代裸画布空间及用户阅读任务的验收。

## 3. 功能边界

| 能力 | 当前状态 | 事实与限制 |
| --- | --- | --- |
| 离线计划与规则诊断 | 已具备 | 读取 ShowPlan、语句和算子，默认注册 34 条规则，涵盖基数偏差、转换、Lookup、Spill、并行、参数、统计、内存、索引与改写提示；规则数量不证明准确率或竞品优势。[规则注册](../SqlXmlAnalyzer.Core/Rules/RuleEngine.cs) |
| 实际运行指标 | 部分具备 | 读取输入计划已有的行数、CPU、耗时、读数和线程信息。按字段定义求和或取最大值，并保留缺失／不完整状态；没有额外采集源时不能补造数据。[事实读取](../SqlXmlAnalyzer.Core/Services/PlanOperatorFactsService.cs) |
| 通用在线获取与执行 | 未找到实现 | 未找到通用 SQL Server 连接、获取实际／估算计划、STATISTICS IO／TIME、实时查询采集及 Query Store 取数工作流。现有数据库执行器仅服务于隔离语义验证，不应据此标为在线性能分析能力。[LocalDB 验证器](../src/SqlXmlAnalyzer.Application/Services/LocalDbSqlSemanticRunner.cs) |
| A/B 与版本历史 | 已具备快照比较；实验闭环未具备 | 支持会话保存恢复、语句／QueryPlan 配对、算子变化、置信度和可比性检查。运行差异比较的是捕获记录，未建立相同环境下的重复性能实验。[比较控制器](../Core/Services/PlanComparisonController.cs)、[调优会话](../Core/Services/TuningSessionService.cs) |
| 索引分析 | 部分具备 | 有优化器缺失索引、语法候选、评分、列调整、DDL、部署与回滚审核；未在线核验目录或建索引，收益预测为 N/A，原始 Impact 为优化器估算。[索引沙箱](../ViewModels/IndexSandboxViewModel.cs)、[成本暴露分析](../SqlXmlAnalyzer.Core/Simulation/CostImpactSimulator.cs) |
| 参数与统计 | 部分具备 | 读取捕获参数和统计使用信息，生成 DBCC 模板并接受手工粘贴直方图；默认教学示意图不是真实数据分布，不能当作数据库证据。[统计控件](../StatisticsHistogramControl.xaml.cs) |
| SQL 改写与审核 | 已具备；语义验证受限 | 有 AST 提案、依赖、diff、hash、选择、隔离验证与审核后文件写回。专用 LocalDB 的场景结果、错误及副作用比较不证明任意输入等价，也不证明更快；支持版本、兼容级别及语句有明确限制。[提案服务](../src/SqlXmlAnalyzer.Refactoring/RewriteProposalService.cs)、[语义限制](../src/SqlXmlAnalyzer.Application/Models/SqlSemanticValidation.cs) |
| 报告与 CLI | 已具备 | 桌面支持 HTML／JSON／SVG／SQLPLAN／PDF／DOCX，CLI 支持扫描门禁、JSON／JUnit、脱敏副本、改写与语义验证。普通报告并非全部支持脱敏共享，需使用相应审核或副本入口。[报告导出](../Core/Services/DiagnosticReportExportService.cs)、[CLI](../SqlXmlAnalyzer.CLI/Program.cs) |

## 4. 竞品依据与可借鉴方向

SolarWinds Plan Explorer 是商业厂商提供的免费专有工具；SQL Sentry 是另一个监控产品。下表只使用 Plan Explorer 的功能作为对照，不把 SQL Sentry 的实例监控、告警等平台能力算入。dbForge Studio for SQL Server 为商业软件，其 profiler 功能用于补充工作流参考。

| 官方依据 | 可借鉴能力 | 对本项目的设计含义 |
| --- | --- | --- |
| [Plan Explorer 产品页](https://www.solarwinds.com/free-tools/plan-explorer) | 计划指标表达与调优分析入口 | 节点主指标、成本表达和实际／估算口径须清楚 |
| [Plan Explorer 使用概览](https://documentation.solarwinds.com/en/success_center/sqlsentry/content/planexplorer/installation-overview.htm) | 在线获取／执行计划，以及适用条件下的等待和调用栈信息 | 后续采集工作流应同时记录环境、来源和可用数据，不能仅增加“运行”按钮 |
| [Plan Explorer Results](https://documentation.solarwinds.com/en/success_center/sqlsentry/content/planexplorer/results.htm) | 图形伸展／压平／过滤、按条件提供视图、图表与 SQL 联动 | 先解决阅读与调查路径，再增加次要工具；完整适应与可读视图需要不同语义 |
| [Plan Explorer Index Analysis](https://documentation.solarwinds.com/en/success_center/sqlsentry/content/planexplorer/index-analysis.htm) | 获取实际计划后的索引分析、真实统计及参数 Test Value | 保留评分与实测收益的区别；本项目手工统计导入不能写成同等在线验证能力 |
| [Plan Explorer Sessions](https://documentation.solarwinds.com/en/success_center/sqlsentry/content/planexplorer/sessions.htm) | 调优版本历史与评论保存 | 本项目已有快照基础，后续可把采集条件、变更理由和实验结果绑定到同一版本 |
| [Plan Explorer Live Query Profile](https://documentation.solarwinds.com/en/success_center/sqlsentry/content/planexplorer/live-query-profile.htm) | 查询执行期间采集及其回放 | 属于 PW-06 的后续能力，不把静态计划图或当前分页视为实时执行回放 |
| [dbForge SQL Query Profiler](https://www.devart.com/dbforge/sql/studio/sql-query-profiler.html) | 执行数据、I/O、互补计划视图与比较 | 将“查找问题—获取证据—比较候选”的操作连接起来，避免只扩展展示列 |

以上是官方文档确认的能力及本项目的设计推论，不构成双方功能、准确率、性能或可用性的实测排名。

## 5. 尚未实施的工作区待办

以下五项为本轮评估发现的未解决 UX 问题，统一使用 PW 编号；不得因已有节点美化、适应修复或全量测试通过而标记完成。PW-06 是后续功能建设，不计入这五项 UX 修复。

| ID／状态 | 当前发现及证据 | 目标设计 | 完成标准 |
| --- | --- | --- | --- |
| **PW-01 画布可读性／待实施** | 640×360 DIP 的 64 节点页适应比例约 0.1666%，节点不可读；工具与分页 WrapPanel 挤占裸画布。既有几何断言通过但不能代表可用。[极小视口截图](verification/plan-workspace-redesign/runs/20260910T234426-Release/large-plan-small-viewport.png) | 将“完整概览”和“可读查看”分开；窄窗优先保留画布，次要工具收纳；低比例提示放大、定位或热点表。是否增加局部分支视图由可读任务验证决定，不直接解除分页上限 | 分别量测裸画布、工具区与分页区；在指定窗口完成辨认算子、定位问题、查看当前指标任务。几何完整、文字可读及操作可达分别记录 |
| **PW-02 诊断范围徽标／待实施** | `BuildOperatorReports` 将没有 Operator 的诊断分配到首算子；示例 Stream Aggregate 的 3 个警告实际包含 2 条语句级索引建议和文档 CPU 提示，易误认为聚合算子有 3 个问题。[分配逻辑](../SqlXmlAnalyzer.Core/Rules/DiagnosticProtocol.cs)、[示例工作区](verification/plan-workspace-redesign/runs/20260910T234426-Release/light-1920x1080-100.png) | 节点徽标只表示明确归属该算子的诊断；语句／计划／文档诊断在对应范围导航中显示。跨范围相关线索单列，保留原始位置，不强行绑定首节点 | 文档／语句问题不增加首算子问题数；算子问题可一次定位；问题总数及检查记录不丢失；跨语句相同 NodeId 正确消歧 |
| **PW-03 问题摘要／待实施** | 基数偏差等同名条目缺对象；索引摘要截断在 CREATE 与带 GUID 的 DDL 名称，用户不能从列表识别调查对象。[问题列表模板](../Views/PlanWorkspaceView.xaml) | 卡片固定展示“对象＋问题＋一条关键事实”，另列严重度、置信度与范围；SQL／DDL 留在详情及审核入口。摘要来自结构化事实，不截取长报告冒充摘要 | 同名诊断能够区分对象；窄侧栏仍看到对象和事实；索引项首屏不被 DDL 淹没；完整证据与脚本仍可访问 |
| **PW-04 详情与统计证据表达／待实施** | 0 个相关问题的节点仍展示原因／建议／空证据／未采集等多段；出现 `optimizer-cost` 等原始单位；统计标题与默认教学模拟图容易被读作真实直方图。[详情生成](../Core/ViewModels/PlanWorkspaceViewModel.Presentation.cs)、[统计控件](../StatisticsHistogramControl.xaml.cs) | 无诊断时以节点事实为主，空原因／建议／证据不占默认版面；技术单位提供可读名称及来源。统计明确区分“未导入”“已导入真实统计”“教学示意”，教学模式显式进入 | 0 问题节点首屏无重复空段；估算／实际／缺失／无效状态可区分；用户无需阅读小字就能判断统计来源；导入统计保留来源与适用范围 |
| **PW-05 命令和工作流／待实施** | 顶部“计划图”与中央页签重复；“问题／详情”蓝按钮缺少展开状态；顶部“布局”实际恢复默认，横纵布局却在“显示设置”内。[工作区入口](../Views/PlanWorkspaceView.xaml)、[图工具栏](../PlanGraphControl.xaml) | 同一任务有清晰主入口；侧栏开关显示选中状态；“恢复默认布局”与“水平／垂直排列”明确命名和分组；将问题、证据、索引审核、A/B、导出串成可追踪流程 | 用户能从标签预测行为；侧栏按钮状态与面板一致；键盘可完成调查链；调整布局不意外重置筛选、选中或个人偏好 |

### PW-06 在线采集与实验历史：后续功能，尚未实施

在离线工作区稳定后，再设计通用 SQL Server 连接、估算／实际计划获取、执行数据采集及实验历史。此能力与现有 LocalDB 语义验证分开，不扩展现有验证器承担生产查询执行。

拟保存的实验记录至少包括来源与时间、服务器和数据库环境、引擎及兼容级别、参数、会话设置、SQL／计划身份、采集方式、实际指标可用性、取消／错误结果、版本评论和比较条件。应区分单次观察与重复实验，禁止把估算成本下降直接表述为性能提升。实时采集／回放、在线统计信息、索引试验的支持范围及权限模型须另行确定，不作为本轮文档发布的已交付功能。

## 6. 实施顺序与兼容边界

| 阶段 | 待办 | 实施重点与进入下一阶段的条件 |
| --- | --- | --- |
| 第一阶段：首屏可读与归属正确 | PW-01、PW-02、PW-03 | 优先解决“看不清、误归属、认不出对象”；同时保留既有缩放幂等、分页、取消、完整身份与主题回归。三个问题的任务验收分别通过后再扩展流程 |
| 第二阶段：证据与操作清晰 | PW-04、PW-05 | 详情按数据条件呈现，统计来源显式表达，命令语义和状态一致；用鼠标与键盘走完问题→证据→审核／比较→导出 |
| 第三阶段：采集与实验闭环 | PW-06 | 另行评审在线执行、数据采集、实验上下文与版本历史；必须具备真实 SQL Server 环境和可复现实验记录，不能沿用离屏截图宣称完成 |

PW-02 涉及诊断展示投影，应先核对图、热点表、详情、报告消费者的数量语义；保留规则本身的 ID、严重度和事实，不用删除诊断来修正徽标。PW-03／04 优先复用结构化事实，不引入从展示文本反解析身份的新路径。PW-05 使用已有审核和导出服务，不绕过适用条件、验证或写回保护。

## 7. 验收设计与结果使用规则

逐项任务脚本、既有自动检查、已知不足及待实机状态见[执行计划工作区任务型验收矩阵](verification/plan-workspace-redesign/acceptance-matrix.md)。该矩阵按本文 PW 编号管理，每项分别关闭。

后续验收应覆盖浅／深色、1280×720／1366×768／1920×1080 与常用比例，同时保留 640×360 DIP 极端边界；100／1000／5000 节点、64 节点分页、折叠、跨页及多语句身份均需回归。对极小窗口，应验收可达的概览／阅读替代路径，不能用一次全图缩小替代阅读任务。

验收材料须标明输入类型、人工合成或真实采集、主题、物理分辨率或等效 DIP、实际缩放比例、可见节点数、裸画布尺寸和操作步骤。既有离屏软件渲染只证明相应控件、变换与绑定行为；真实显示器 DPI、实体键盘、屏幕阅读器、GPU 以及生产数据库性能实验仍需要相应环境验证。

PW-01–PW-06 只有在实现、针对性验证和任务验收均有证据后才能改为完成。不得将历史 3066 项测试、1284 项离屏断言或当前竞品文档核验自动算作这些待办的验收。测试总数、截图数量和规则数量均不能替代诊断准确率或用户任务成功率。

## 8. 相关文档

- [软件详细设计与问题分析](软件详细设计与问题分析.md)：保留 2026-09-08 架构审查及后续实施注记。
- [UI 改进和功能改善文档](UI改进和功能改善文档.md)：保留 UI-01–UI-16 历史规划，当前计划工作区待办以本文 PW 编号为准。
- [工作区重构验证](verification/plan-workspace-redesign/README.md)：信息架构、提示框及历史验收。
- [计划图优化验证](verification/plan-workspace-redesign/graph-polish.md)：节点、缩放、布局及未完成实机范围。
- [任务型验收矩阵](verification/plan-workspace-redesign/acceptance-matrix.md)：PW-01–PW-06 的输入、任务、已知不足与完成标准。
- [本轮发布前验证](verification/plan-workspace-redesign/publish-validation.json)：本次提交对应的构建、测试及文件证据；与历史离屏验证分开。
