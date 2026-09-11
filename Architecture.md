# SqlXmlAnalyzer 系统架构指南 🏛️

## 执行计划桌面展示与导航（2026-09-11）

当前执行计划工作区继续使用 WPF＋Nodify。`PlanWorkspaceView` 组织语句选择、问题/索引/检查状态、计划图/热点算子、详情和按需辅助材料；`PlanWorkspaceViewModel.Presentation` 管理展示、筛选与选择。计划、语句及算子联动继续使用完整 `PlanLocation` 身份，不以裸 NodeId 代替跨语句身份。

`PlanGraphNodeMetrics` 为卡片与连线锚点提供统一尺寸。`PlanGraphLayoutService` 使用迭代式子树轮廓布局，支持水平/垂直、折叠和分页森林，保留输入顺序。图容器每页最多 64 个；定位与查找仍覆盖全部节点，通过分页提交进入目标节点所在范围。

`PlanGraphControl.Async/Presentation` 将数据提交和视口操作分开协调：适应意图保留到当前页和视口可测量时；后一次缩放、定位或实际画布平移使旧定位 continuation 失效。`PlanGraphEditor` 同步比例与平移变换，并处理 Nodify 定位动画和最低缩放约束。普通最小比例为 2%，明确适应操作可根据范围临时降低；恢复普通比例后还原范围。此策略保证几何容纳，不保证极小视口的文字可读性。

图、表和详情复用已有算子事实与指标状态；缺失运行数据不补零，估算成本不表达为耗时。`WorkspaceThemeService` 与局部 ToolTip 模板统一提示框背景及文字主题。显示偏好保存在本机；当前工作区默认布局属于已实现状态，评估提出的默认工作方式调整尚未实施。

能力边界：桌面读取已捕获的计划/参数/统计使用信息；真实直方图可手工导入。`LocalDbSqlSemanticRunner` 只用于受限隔离语义场景验证，不能作为通用在线采集或性能收益验证。CLI、分析规则 ID、报告契约和 XML 解析边界不因这次桌面优化而重新定义。

后续 PW-01～PW-06 见[当前设计与差距评估](DOCS/plan-workspace-design-and-gap-assessment.md)，验证状态见[任务矩阵](DOCS/verification/plan-workspace-redesign/acceptance-matrix.md)。尤其 `BuildOperatorReports` 将部分范围级诊断汇入首算子的兼容行为，目前会影响节点徽标含义；PW-02 必须按原始范围设计显示和验证，不能通过改变原诊断身份或默认严重度掩盖问题。

## IMP-30：发布与恢复边界

`Core/Deployment` 的 `ReleaseBundle` 以外部可信清单 SHA-256 校验版本、文件集合、长度、哈希和安全路径，限制文件/字节预算，拒绝重解析点。恢复使用私有同卷暂存目录、逐文件 Flush、双端复验和不覆盖的目录移动，调用方自行选择恢复副本。`ReleaseOperation` 统一脚本的异常分类、DUMP 与分模式日志；不改变 WPF/CLI 的业务命令契约。

`publish.ps1` 复用 IMP-29 构建证明，源码清单新增发布脚本和 `DOCS/release-materials`；发布/回退/单文件会话探针都绑定执行前后同一构建。候选清单与签名/正式分发审批分别记录。参见 [IMP-30](DOCS/IMP-30发布材料与回退验证.md)；可见窗口验收受桌面工具错误阻断，CLI 和启动钩子结果不替代 UI 验收。

[IMP-30 审查修复](DOCS/IMP-30审查修复与加固说明.md)将候选类型/ID、状态中的构建哈希及内嵌构建证据统一校验；阶段输入记录受测 EXE 哈希，完整绑定命令 stdout/stderr。路径先按调用者工作目录规范化；进程与两路输出共享取消期限，输出流式写盘。证据守卫使用明确的预期异常，未知脚本错误仍由既有异常边界生成 DUMP。

## IMP-29：验收执行与报告布局边界

审查修复后，`AcceptanceEvidence.ps1` 管理执行时源码清单、每种配置 17 个 DLL 的字节记录及只创建新文件的构建/阶段证明。`Run-BuildTests.ps1` 在测试前后校验快照，各场景通过 `-BuildEvidence` 拒绝过期构建，并对结果生成绑定。`Write-Validation.ps1` 先验证所有证明，再复制证据和发布摘要；源码记录不从当前目录重新生成来取代旧版本。构建使用 `-t:Rebuild -warnaserror` 和原生退出码，避免增量跳过编译掩盖警告。详见[加固说明](DOCS/IMP-29审查修复与加固说明.md)。

`DOCS/verification/IMP-29/Run-Acceptance.ps1` 顺序组织构建、全量测试、真实数据库/CLI、WPF、XEL 和性能探针，输出目录必须全新。测试宿主校验加载的生产程序集与构建产物字节一致；`AcceptanceProbeBoundary` 与单元测试共用代码，捕获未知异常到真实 minidump，并保证失败不能进入成功证据。验收结果、源码指纹和 R/D/UI 状态分开保存，自动化通过不授予发布许可。

`ReportReviewWindow` 使用显式滚动容器包裹有限的内容 Grid，避免通用可访问性包装器的无限测量把星号行扩展为整个报告高度。正文内部滚动保留全部内容，外部滚动负责紧凑窗口下的控件可达性。验证与限制见 [IMP-29](DOCS/IMP-29完整构建测试与用户场景验收.md)。

## IMP-28：兼容性与会话持久化

审查修复后，`WithCurrentSchema` 在已校验的不可变根对象文本中插入版本成员，先计算 UTF-8 合计字节再生成和重新校验副本；不重新序列化原成员，避免缩进、转义及未知值表示变化。真正超过 1 MiB 时返回 `CONFIG_MIGRATION_BUDGET_EXCEEDED`，详见[加固说明](DOCS/IMP-28审查修复与加固说明.md)。

`RuleConfigurationDocument` 用专用 `ConfigurationSchemaVersion` 区分旧无版本配置与当前字符串 `"1"`，通用 `schemaVersion` 继续作为旧扩展保留；`WithCurrentSchema` 只生成文档副本，实际迁移复用 `RuleConfigurationStore.SaveAsAsync`，不自动改写源配置。未知声明版本拒绝加载，未知非版本字段延用 DOM 保留机制。

`TuningSessionService` 接受无版本、2.0 和 2.1 的已知会话结构。`TuningSessionService.Persistence` 校验管理字段、快照 ID、比较引用和保存预算；通过可注入 `ITuningSessionWriter` 写入目标目录内私有临时文件，刷新并核对 SHA-256 后以不覆盖方式发布。服务无参构造及原 Save 重载保留，新重载提供取消令牌；已有目标明确失败。异常边界复用公共 DUMP 和日志机制。

旧计划、死锁、配置及诊断适配器继续服务现有消费者；新增冻结样例和双路径测试校验字段及规则语义。本步无新的报告导入或模型 JSON 恢复入口。[兼容矩阵](DOCS/IMP-28模型配置报告与CLI兼容性.md)列出版本策略、消费者和迁移限制。

## IMP-27：本地诊断包审核与保存

审查修复后，`RuleMetadataCatalog.Definition` 集中定义版本、分类、作用域和默认严重度，内置规则与包内 `RuleVersions` 共用该来源；`ReportRuleVersions` 保留原报告的实际版本。`MetricGaps` 只接受未分类字段中规范且已定义的非 Available 状态，包含 Incomplete/Ambiguous。日志解码异常先于路径异常分类，保留预期失败处理。详情见[加固说明](DOCS/IMP-27审查修复与加固说明.md)。

Core `Diagnostics/DiagnosticPackageBuilder` 从不可变 IMP-22 报告中读取生成时配置，并将当前会话配置单独记录；只收集允许的元数据和显式选择的附件。`DiagnosticPackage` 私有保存审核字节，提供只读条目、隐私状态、大小及 SHA-256。脱敏继续复用 `ReportRedactionService`，处理前样例不会进入包。`DiagnosticPackageExporter` 在私有临时目录写 ZIP、落盘、核对包清单和审核哈希，再不覆盖地发布；`DiagnosticPackageValidator` 无磁盘解压地校验版本、条目与预算。DUMP 附件按已读取快照复用 `MinidumpValidator` 的流校验。

WPF `DiagnosticPackageViewModel` 管理可选内容、异步准备/保存、预览失效和忙碌门禁；窗口负责本地文件对话框与实际内容呈现，`MainWindow.DiagnosticPackage` 捕获当前范围并在报告失效时降级为元数据包。未知错误与日志门禁复用公共诊断机制，无网络传输。包协议与验证边界见 [IMP-27](DOCS/IMP-27可审核的本地诊断数据包.md)。

## IMP-26：异步操作与视图提交

AnalysisSessionCoordinator 管理九态、请求号、Revision、令牌和阶段计时；AnalysisOperationViewModel 提供状态栏取消。DocumentAnalysisUiActionService 等待分析和首屏图提交后终结请求。PlanWorkspaceUiActionService 在后台准备模型，通过 UiBatchScheduler 提交有界更新；PlanGraphPageService 限制每页 64 个图容器，AllNodes 保留完整指标数据。XEL 初次选择复用打开请求，改写审核复用原诊断快照。异常、DUMP、日志及可测边界见 [IMP-26](DOCS/IMP-26异步状态取消与大图性能.md)。


## IMP-25：工作区交互与语义主题

审查加固后，`WorkspacePanelLayoutService.MinimumColumnWidth` 按 Pixel / Auto / Star 语义计算滚动所需宽度；计划视图在展开和侧栏尺寸变化时同步外层最小宽度。`WorkspaceAccessibility.Focus` 在布局后检查可见性并返回真实焦点结果，比较页通过有界 `MoveFocus` 跳过不可用目标。计划视图的节点详情事件统一处理属性展示、焦点进入及返回，新增事件边界沿用未知异常 DUMP 策略。见 [IMP-25 审查加固](DOCS/IMP-25审查修复与加固说明.md)。

`WorkspaceCommands` 定义共享 RoutedUICommand，主窗口在服务初始化后注册 CommandBinding，导航按钮与快捷键共用 CanExecute。`WorkspaceInteractionService` 管理 DIP 策略、循环选择及异常/DUMP 边界；`WorkspaceAccessibility` 处理焦点、对话框尺寸与返回。`AccessiblePlanNode` 提供 AutomationPeer，复用已有算子身份及选择链。`WorkspaceThemeService` 更新 20 种语义画刷，比较树使用动态资源；减少动画由 MainViewModel 传递到推演视图及计时器。此层不修改诊断规则、计划事实或配置格式。详见 [IMP-25 实现与验证](DOCS/IMP-25布局主题与键盘操作.md)。

## IMP-24 配置文档、活动快照与安全保存

审查加固引入 `PlanAnalysisSource`，在成功分析后同时提交 XML、路径和识别结果；新文件与历史快照加载期间不修改当前来源。重算读取这一对象，`ApplicationOrchestrator.Execute` 的可选 `planInput` 让 SQL 改写使用相同输入，避免再次打开磁盘文件。`AnalysisSessionCoordinator` 记录内容类型，配置切换只取消执行计划；按请求 ID 的取消和类型更新在锁内校验。JSON 遍历维护路径片段，仅在错误时生成受限长度的路径；属性名和所有字符串的 Unicode 解码错误在窄边界转为校验异常。验证见 [IMP-24 加固](DOCS/IMP-24审查修复与加固说明.md)。

Core 的 `RuleConfigurationDocument` 保留原 JSON 和未知项，提供不可变已知设置及有效设置指纹；旧 `RuleConfigurationRoot` 仅作为执行兼容投影。`RuleConfigurationSession` 在应用时原子替换文档，`PlanAnalysisService` 每次分析捕获一次，`RuleEngine` 继续把配置写入 `PlanAnalysisContext`。主窗口应用配置后取消旧执行计划请求、清除旧改写审核对象并标记结果需要重算。生产图通过 `PlanDiagnosticReport.ForOperator` 投影已有诊断，保留相同对象、证据及上下文，不另行执行规则。

WPF 的 `RuleConfigurationViewModel` 管理草稿、过滤、变更预览、载入/保存/应用的独立状态及取消；窗口仅处理控件和文件对话框事件。`RuleConfigurationStore` 复用应用层 `SqlFileSnapshot` / `SqlWritebackService` 的原字节校验、备份、落盘和替换逻辑，另存路径使用不覆盖的新文件提交。所有失败保留草稿和活动配置，已发生的提交单独说明；未知错误走统一 DUMP 和日志策略。兼容字段、预算、别名、严重度合并语义及验证见 [IMP-24](DOCS/IMP-24兼容规则配置界面.md)。

## IMP-23 XEL 检索集合与原始事件

审查加固后，结构标签通过长度前缀及分块 SHA-256 固定大小，再统一编号并复用整数缓冲区做完整拓扑比较；标签/属性/比较量预算在昂贵操作前检查。无死锁的可读 XEL 保留为零事件来源；`XelSearchSource` 的首次接受基准跨目录重建保留，导入和导航检查 XML、采集元数据、源字节和事件成员身份。首次载入期间由 ViewModel 和按钮绑定共同阻止查询/导入抢占，详见 [IMP-23 审查加固](DOCS/IMP-23审查修复与加固说明.md)。

Core 的 `XelSearchService` 通过 `IDiagnosticDocumentReader` 读取有预算的快照，构建 `XelSearchCatalog`。`XelStructureKey` 对资源/持有/等待图做有界的精确规范化；身份不足或超出聚合预算的事件单独列出。`XelSearchEvent` 保留所属输入、DeadlockInput、原始位置和固定预览；分组和重复标记都不移除来源。

`XelSearchViewModel` 管理筛选、请求取消/版本、完整结果提交和异常呈现；窗口只处理控件事件。`XelDeadlockUiActionService.SelectSearchEventAsync` 校验源未变化并同步选择器，复用 IMP-20 分析和证据链。未知错误交给统一 DUMP 诊断，日志执行构建模式门禁。键规则、时区/范围语义、预算和验证见 [IMP-23](DOCS/IMP-23XEL搜索聚合与事件追溯.md)。

## IMP-22 统一报告与脱敏预览

审查加固后，`ReportSizeBudget` 在字段收集、不可变快照构造和 XML 写入时共享 800 万内容字符、10 万条总记录预算；JSON/HTML/SVG 编码输出另有限额。完全缺失指标沿用 `PlanOperator.ExportFacts` 的紧凑策略，以 Missing/N/A 表示；有指标时保留完整投影。正文和 XML 在追加前检查，JSON 缓冲区在扩容前检查，预览按隐私模式缓存不可变正文。CLI 超预算返回明确失败，并清除大型旧模型字段，避免再次序列化放大。

`PlanReportXml` 按源元素身份流式裁剪选择范围，保留语句选项、游标祖先、Operation 和命名空间。完整 RECEIVE 必须保留两个操作，因此单操作范围明确拒绝。取消令牌贯穿工厂及渲染入口；回归、XSD 校验和分配量记录见 [IMP-22 审查加固](DOCS/IMP-22审查修复与加固说明.md)。

Core 的 `DiagnosticReport` 保存脱离 XML/控件的不可变选择快照，工厂复用计划范围过滤并验证计划版本或死锁事件指纹。GUI 预览、HTML/PDF/Word 规范正文、JSON 及静态图消费同一模型；CLI 计划扫描新增同名字段。`ReportRedactionService` 分类处理正文、元数据、图标签和源 XML，未知 XML 字段阻止脱敏快照；预览原文样例不进入输出。桌面 `DiagnosticReportExportService` 完成暂存、刷盘、禁止覆盖发布及失败清理。实现、兼容接口边界和浏览器离线验收限制见 [IMP-22](DOCS/IMP-22统一报告与脱敏预览.md)。

## IMP-21 A/B、改写与索引审核界面

审查修复：索引失败状态以独立订阅者调用发布，避免多播通知异常使其他绑定保留旧脚本；选项通知纳入刷新异常边界。候选输出统一带未脱敏消息，只描述输出动作；改写应用错误与导出错误分开保存，复制/保存不能清除应用诊断。设计依据与回归见 [IMP-21 审查加固](DOCS/IMP-21审查修复与加固说明.md)。

比较窗口提供可见 A/B 命令及各侧未匹配清单，仍由 `PlanComparisonController` 判定身份和采集条件。审核窗口以 `RewriteProposalItem` 投影步骤前提、风险、证据、输入 hash 和精确 diff；完整报告与 SQL 对照分页展示。`ReviewOutputService` 负责复制及 UTF-8 新文件输出，以同目录临时文件和禁止覆盖的移动发布候选，失败不授予应用凭据；原文件应用继续经过 `ReviewedSqlApplyService` 与可靠写回。应用边界意外抛出时保守显示提交未知并禁止重试。

`IndexSandboxViewModel` 将名称和环境选项交给同一个 `IndexDdlCompiler`，创建/回滚脚本来自同一编译结果；无效输入清除两者并禁用复制。`IndexDdlOptions.IndexName` 为空时保留既有指纹命名，非空按单个原始标识符转义。完整对象、原计划优化器估算、工具评分及假设输入分区展示。诊断沿用统一 DUMP 和分模式日志，详见 [IMP-21 实施与验证](DOCS/IMP-21审核界面.md)。

## IMP-20 工作区选择与证据导航

审查加固后，手动死锁选择显式清除证据选中值，返回证据同步恢复诊断详情；节点定位仅展开祖先，并在显示前重新计算图坐标。`PlanAnalysisOutput.RefactoringNotices` 独立传递文档级重构提示到工作区和文本报告，既有 `WarningsText` 保持兼容。验证与设计依据见 [IMP-20 审查加固](DOCS/IMP-20审查修复与加固说明.md)。

`MainViewModel` 持有 `PlanWorkspaceViewModel` / `DeadlockWorkspaceViewModel`。计划以完整层级身份生成当前选择，保留原始 XElement，按相同范围投影 SQL、算子、索引、诊断及运行状态；WPF 服务仅负责应用选择和高亮。图的原生选中绑定 `SelectedNode`，重新布局/折叠限定当前节点集合。文档级诊断可通过证据身份切换目标 Batch / Statement / QueryPlan。复制文本使用完整选中范围诊断；IMP-22 的桌面报告入口使用同一选择范围创建不可变快照。

死锁选择使用原始 `DeadlockInput`，不再序列化再解析；`OriginalElement` 引用源元素以保留原始命名空间与行号，分析副本继续规范化。证据位置由事件上下文映射回原始源，事件切换清除旧状态，并沿用会话请求号/取消保护。源修改导致旧证据失效，未知错误复用 DUMP/日志基础设施。清空通知在释放状态前取消请求及清空事件选择器。实现、兼容性和验收见 [IMP-20](DOCS/IMP-20语句事件选择与诊断证据.md)。

## IMP-19 语义场景证据与应用凭据

联合审查加固：字符串比较保留原始 UTF-16 码元；sql_variant 在列模式阶段明确拒绝，避免 CLR 投影丢失内部属性。`SqlBoundedFileReader` 支撑新快照重载及写回的有界校验，语义入口先完成字节/字符检查再执行 AST。`SqlSemanticDatabaseLifetime` 在建库发出前保护清理流程；清理由新的管理员连接完成，并有独立期限。桌面通过同一实际应用结果派生状态标题与详情。接口迁移与验证记录见 [联合审查修复](DOCS/IMP-18-19联合审查修复与加固说明.md)。

审查修复后，`ParseObserverBatches` 对观察器执行独立只读 AST 检查，应用准备和执行入口均不能接受有副作用的观察器。受测 SQL 之后先运行只读 ObserveSql，再采集内部事务/选项及对象状态，避免辅助 SELECT 改写 ROWCOUNT/ERROR；观察器错误阻止应用。`SqlSemanticAffectedRows` 按驱动契约合并批次计数，-1 不参与数值累计，零行 DML 保持 0，非法值与溢出明确失败。详见 [审查修复与证据](DOCS/IMP-19审查修复与加固说明.md)。

Application 新增 `SqlSemanticSuite` / `SqlSemanticReport` 与 `ISqlSemanticRunner`。`SqlSemanticSandboxPolicy` 对严格 JSON、SQL 大小、批次和 AST 白名单设限；`LocalDbSqlSemanticRunner` 每个场景/每侧创建专用 LocalDB 新库，以无登录用户且 `WITH NO REVERT` 执行，禁用跨库信任与连接池，最终清理自有数据库。`SqlSemanticComparison` 比较类型、NULL、重复行、顺序策略、错误、事务与对象状态，`SqlSemanticValueReader` 有界读取文本/二进制并保留 decimal(38) 精度。报告公开数据库版本、设置、全部场景及未证明边界。

`ReviewedSqlApplyService.PrepareAsync` 重审所选组合并执行真实数据库验证，绑定源文件快照、完整提案/选择摘要、精确预览和场景证据，签发不可由 JSON 恢复的一次性 `PreparedSqlRewrite`。`Apply` 要求显式场景确认，重新审核选择与源字节，再调用 IMP-03 写回服务；保留实际提交、备份和不确定结果。静态 `RewriteReview.CanApply` 仍为 false；有限场景证据与应用授权属于不同对象。

CLI 三个新命令和 WPF 审核窗口共用上述服务；CLI 应用额外要求审核过的源/预览/场景三份 hash，并重新执行数据库验证。桌面在选择改变或关闭时废弃凭据/取消执行，已写入或提交不确定后禁止重复应用。未知异常使用既有 DUMP 捕获与编译模式日志；新增连接依赖 `Microsoft.Data.SqlClient 6.1.6`。详见 [契约、隔离边界与验证](DOCS/IMP-19SQL语义验证与可靠应用.md)。

## IMP-18 SQL 改写审核契约

审查加固：终端与文本文件共用 SQL 对比渲染，从 `Review.PreviewSql` 输出实际选择且保留原文。对象事件检查覆盖 `SelectStatement.Into`、完整对象名/大小写、DROP IF EXISTS 和 IF/WHILE/TRY-CATCH 分支，组合时重新检查；完整生命周期仍未证明。见 [修复设计与验证](DOCS/IMP-18审查修复与加固说明.md)。

Core 的 `RewriteProposal` / `RewriteReview` 保存不可变的源与步骤 hash、diff、规则版本、依赖、前提/风险/证据、选择及验证性质。Refactoring 的 `RewriteProposalService` 分离候选生成与组合审核；每个步骤按前序候选形成保守依赖链，组合后再次检查源/diff/hash、语法和临时对象/批次结构。`SqlRewriteScope` 建立批次符号清单和归属于源 hash 的名称预留，不生成或清理临时对象；不可靠作用域明确跳过。

ApplicationOrchestrator 不再由 `IsSuccess` 或 `isDryRun=false` 触发写回；IMP-03 写回服务由上述 IMP-19 独立审核应用流程调用。`RefactorResult.OutputSql` 是兼容候选，`Review.PreviewSql` 为正式所选预览，静态提案的 `CanApply` 始终 false。GUI/CLI 共用 `RewriteReviewFormatter`；桌面审核窗口由可测试 ViewModel 处理依赖选择/取消，旧快速替换路径关闭。未知异常复用统一 DUMP 与固定编译模式日志。详见 [实现、兼容性及验收边界](DOCS/IMP-18可审核SQL改写提案.md)。

## IMP-17 多语句比较与可比性

Core/Comparison/PlanComparisonEvidence 基于 PlanDocument、语句源码和 IMP-11 指标事实建立不可变的匹配/采集证据。先匹配语句，再匹配所属 QueryPlan 和算子；唯一词法/对象/结构匹配与候选、人工、未匹配状态分开。PlanComparisonController 在对应范围内生成可空指标差值，检查版本、参数、DOP 和执行口径，未知故障经统一 DUMP 边界。

完整结果由 PlanComparisonResult.Statements 发布，旧 PlanA/PlanB 单根字段不能代表多根文档。WPF 显示配对列表、占位、来源及人工选择，失败原子清空旧结果。PlanComparisonSelection 独立持有 A/B 范围，SetComparisonPlans 原子发布两侧快照和选择；同一快照可分别比较不同语句。模型版本 `plan-comparison/1.2.0`；PlanComparisonPredicateEvidence 保留 BuildResidual/ProbeResidual 等角色、范围列/运算符和有序表达式，PlanComparisonSortEvidence 保留 Sort/TopSort 的排序属性，PlanComparisonObjectEvidence 核对对象完整性。IncompleteStructureOperators 沿父子树传播并参与多 QueryPlan 匹配；对象未知时人工配对也不能生成数值差值。详见[证据完整性加固](DOCS/IMP-17比较证据完整性加固.md)。无规则 ID/配置变化。

会话 2.1 在根层保存双侧选择，快照保存原始指纹及与当前 XML 的绑定。`xml-representation/1` 为源根元素以 DisableFormatting 序列化后的 SHA-256；不包含 XML 声明或根外空白/注释。载入校验当前格式的 XML 哈希，再恢复原始指纹关联；无法核验的旧会话指纹仅为历史记录。XML 修改使关联失效，原始指纹不冒充修改后内容的来源。快照总成本为可空的顶层唯一根摘要。详见 [比较契约](DOCS/IMP-17多语句与可比性检查的AB比较.md)及[审查加固与兼容性](DOCS/IMP-17审查修复与加固说明.md)。

## IMP-16 成本范围与模型证据

CostImpactSimulator 先快照所有已识别 QueryPlan/算子并汇总 OwnCost，再按完整目标和捕获 QueryPlan 关联候选。多候选按算子身份取并集，不重复累加子树成本或假设收益；缺失/非法成本保持未知。CostImpactResult 公开版本、范围、口径和限制，ReductionPercent 为可空且始终未知。

IndexScoringCalculator.Evaluate 返回独立的 IndexScoreResult；IndexPredicateEvidence 只沿当前访问算子完整谓词根的 AND 合取项取证，跳过 CASE/IF、OR、NOT 等计算子树。结构化根没有子节点时，才允许 ScriptDom 文本后备；支持括号/带符号数值与结构化 NULL 测试，引用为列的参数名称必须有本 QueryPlan 证据。未知覆盖为 0 分，未支持的 Convert/ConstExpr 不推断成常量。CapturedImpact/Source 独立于工具评分，SandboxInputSnapshot 对当前目标估算取最大值并保留默认/编辑来源。评分模型为 2.0.1，RULE020/035 为 2.1.1，统一异常与 DUMP、Debug/Release 日志契约继续适用。详见 [模型契约](DOCS/IMP-16模拟顺序依赖与模型限制.md)及[审查修复与验证](DOCS/IMP-16审查修复与加固说明.md)。

## IMP-15 索引目标与 DDL

审查加固：列引用必须有完整实体归属，Column 属性按原始名称保留，DDL 生成时再引用。Sort 从捕获 QueryPlan 中独立读取完整列身份和方向，不要求 Sort 自身带 Object；多个排序独立比较。沙盒与覆盖评分共用实体列过滤。见 [审查修复与验证](DOCS/IMP-15审查修复与加固说明.md)。

IndexDdlCompiler 分开单名引用和多段组装，输出包含同一名称/目标的创建与回滚快照，并完成语法验证。IndexTargetResolver 统一 SQL 候选、评分和沙盒的 QueryPlan/完整对象关联；RULE035 原生诊断改为 Statement 范围。隔离 XML 副本共享原 Envelope，保留证据身份。详见 [实现、兼容性与验证](DOCS/IMP-15索引目标身份与DDL标识符.md)。

## IMP-14 基数与残差规则

审查加固：RuleAnalysisContext 执行前快照 SourceNodeId；PlanDiagnostic/RuleRun 保留局部 NodeId，合并和兼容转换保持该值，诊断范围与调用范围分别约束。完整 Location 继续作为跨语句身份。详见 [修复与验证](DOCS/IMP-14审查修复与加固说明.md)。

四条规则的原生 Evaluate 和旧 Analyze 入口共用 `RowCountRuleEvaluator`；原生路径只读取 IMP-11 算子事实，旧适配器移除对应重复推断。RULE004/030 共享单次行数、显式分母、差值和完整谓词证据；RULE006/034 共享当前 Scan/Seek 的残差及实测读放大判断。相同语义/位置/证据沿用协议去重，保留全部 Origins 和配置后的最高严重度，不修改全局身份算法。未知错误由既有执行边界发布 Failed 和 DUMP 结果，直接 XML 调用捕获后重新抛出。

规则版本升为 2.0.0，RuleId、配置和数值阈值保留；RULE034 增加 Scan 支持，RULE006 实测读放大为 Warning。HasResidualPredicate 区分无残差与缺少 ScalarString；ScriptDom 列参数检查替代函数关键词误判。详见 [IMP-14 口径、兼容变化与验证](DOCS/IMP-14基数与残差谓词规则修复.md)。

## IMP-09 至 IMP-13 联合审查加固

`PlanCapabilityService` 为输入读取与默认诊断提供同一内容归属口径，保留空 Statements，排除 InternalInfo 及错误位置的运行计数；按 XML 根弱缓存，树变更后失效。平行死锁边按最终偏移直线求节点矩形交点，并在几何边界统一处理非法数值与未知异常。`MainViewModel.ClearResults` 先释放两份输入及原始快照引用，再清理结果和比较状态。38 项新增回归及最新双配置全量验证见[修复说明](DOCS/IMP-09至13联合审查修复与加固说明.md)。

## IMP-13 共享死锁分析

审查加固：进程 ID 集合复用于受害者校验，推演按事件一次建索引；`DeadlockPlaybackEdgeKey` 包含 EvidenceId 并贯穿 WPF 绘图缓存、拖动及步骤标记。RANGE 从实际 mode 字段提取，未连通的原始关系不冒充等待边。详见 [IMP-13 审查修复](DOCS/IMP-13审查修复与加固说明.md)。

`DeadlockXmlParser → DeadlockGraphBuilder → DeadlockAnalysisOutput` 一次提取选定事件事实。迭代 SCC 决定完整环成员，独立预算限制解释环枚举；`DeadlockTimelineParser.FromGraph`、WPF 布局/推演、模式诊断和报告读取同一关系与身份。RawFields/RawXml、SourceId/ResourceKey、LinkId/EdgeId、受害者集合及来源定位保留；GUI HTML 复用 CurrentDeadlockAnalysis。事实、候选原因和建议分开，实际 Range 模式才触发范围锁。预算、兼容性、异常边界和 43 项新增验证见 [IMP-13 实施说明](DOCS/IMP-13统一死锁图环与诊断依据.md)。

## IMP-12 诊断协议与运行边界

`IPlanAnalyzerRule.Evaluate` 接收只读 `RuleAnalysisContext`，共享 IMP-10 身份、IMP-11 算子事实、配置快照、能力、版本与取消令牌。`RuleEngine` 发布 `PlanDiagnosticReport`：Runs 记录四种运行状态，Diagnostics 按语义/位置/证据去重并保留 Origins。旧 Analyze 接口通过适配器执行，禁止改变 XML；异常集中转为带规则身份的 Failed，未知错误进入 DUMP 捕获。

AnalysisReport、PlanAnalysisOutput、节点 ViewModel 和 CLI JSON 保留同一协议；`DiagnosticTextFormatter` 统一证据、推断与状态文本，HTML 优先复用已完成的 GUI 分析。输入 Success 和规则 Failed 分开；后者阻止自动重构。旧列表 API 继续提供命中和失败兼容项，取得 Skipped 必须使用 Detailed API。首批四个行数/残差规则发布结构化指标，其他规则明确保留兼容解释及限制。契约及初版 45 项回归见 [IMP-12](DOCS/IMP-12有证据和运行状态的诊断协议.md)。

审查加固后，诊断 `RuleId` 保留受限的旧结果分支，Origins / RuleRun 保留实际执行规则及配置归属；合并以执行来源排序保持版本一致。默认能力包含已有 XML 的 PreservedSource；明确串行的线程倾斜检查标为不适用。节点 `DiagnosticStatusText` 经服务、构建结果、ViewModel 映射到独立状态区域，不污染性能警告文本。新增 47 项回归、Debug/Release 各 1481 项通过，详见[修复说明](DOCS/IMP-12审查修复与加固说明.md)。

本文档深入解析 SqlXmlAnalyzer 的内部设计与代码架构。软件采用高度解耦的 **模块化多层架构**，确保在不断扩充高级诊断规则或重构可视化渲染层时，互相不产生破坏。

## 1. 宏观架构视图

系统按依赖方向拆分为以下项目：

1.  **SqlXmlAnalyzer.Core** 🧠
    *   无 UI 依赖的核心解析与诊断模块 (Class Library)。
    *   负责 XML 文件的解析、执行计划结构的提取、以及最核心的 `RuleEngine` (规则引擎)。
2.  **SqlXmlAnalyzer** 🖥️
    *   基于 WPF (Windows Presentation Foundation) 的桌面 UI 应用。
    *   引入了强大的 **Nodify** 节点图框架，取代了传统的 TreeView，利用 MVVM 和 Code-Behind 实现复杂树状图的动态渲染、自适应排版、智能折叠及事件交互。
3.  **SqlXmlAnalyzer.Analysis / SqlXmlAnalyzer.Refactoring / SqlXmlAnalyzer.Application**
    *   Analysis 将核心诊断结果适配为应用层接口。
    *   Refactoring 是唯一公开的 SQL 重构实现，提供多轮 fixed-point 重写、规则隔离和输出语法验证。
    *   Application 负责文件处理、分析与重构编排以及结果报告。
4.  **SqlXmlAnalyzer.CLI**
    *   通过 Application 层执行扫描和重构，不直接承载领域逻辑。
5.  **SqlXmlAnalyzer.Tests** 🧪
    *   基于 xUnit 和 FluentAssertions 的单元测试网。涵盖核心逻辑与所有三十余条 P0/P1/P2 诊断规则的正确性校验。

---

## 2. 核心模块详解 (SqlXmlAnalyzer.Core)

### IMP-11 算子事实与统一指标

`PlanOperatorFactsService` 按本节点载荷遍历，遇子 RelOp 停止；数值通过 `PlanMetric<T>` 记录值、存在/可用状态、单位、来源、估算/实测分类和聚合方式。`PlanThreadFacts` 保留只读原始属性，unsignedLong 计数用 decimal 求和；缺失、非法、不完整或歧义值不降为 0。

`PlanOperator.Facts` 与旧 XElement 消费者共用按源修订失效的弱引用缓存。规则和图表采用同一输出/读取行、线程累计次数、逻辑次数、worker 分布和自身/子树成本。并行共同执行次数不是线程次数总和；不均匀执行和缺失数据不强行计算单次基数。CPU 求和、算子 elapsed 取线程最大值，均不能跨算子相加当作查询总耗时。CLI 的算子 JSON 导出包含相同 Facts，空事实省略以控制输出大小。

Thread 按 `xsd:int` 和固定区域设置解析，允许前导符号及 XML 首尾空白，按归一化 Int32 编号去重，另行保留原文。角色推导只接受零和正编号；负编号保留数值但不推导逻辑次数或 worker 分布。边界、来源与测试见 [IMP-11 审查加固](DOCS/IMP-11审查修复与加固说明.md)。

未知提取异常复用 `ExceptionPolicy`、经校验且去重的 minidump/异常侧车，Debug 记录 DEBUG/WARN/ERROR/CRITICAL，Release 仅 ERROR/CRITICAL。迁移范围、规则行为差异、计算约定和验证见 [IMP-11 实施说明](DOCS/IMP-11集中算子事实与指标口径.md)。

### IMP-10 计划层级与身份适配

`PlanDocumentBuilder` 构建只读 `PlanDocument → PlanBatch → PlanStatement → PlanQueryPlan → PlanOperator` 元数据，定位键包括 DocumentId、各层源序号、NodeId 和用于缺失/重复 ID 消歧的 OperatorOrdinal。StatementId、QueryHash 仅为元数据。`SqlObjectIdentity` 保留 Server/Database/Schema/Object，Alias/Index 独立保存；未知不补造，名称不统一忽略大小写。

`PlanIdentityAdapter` 连接既有 XDocument/XElement 调用并缓存当前版本。XML 变更使旧源码定位失效，新模型不复用旧哈希。RuleEngine 在副本运行临时上下文，报告和图节点保留原始 Location/Objects，连线用完整 SelectionKey。CLI read/scan 输出层级及归属；多语句自动重构需先选定单条语句。IMP-17 的比较默认覆盖所有语句，界面由 `PlanComparisonSelection` 保存双侧人工范围；`PlanSnapshot.SelectedQueryPlan` 仅作为旧调用/会话兼容入口。语句/事件工作区选择由 IMP-20 接通；后续完整会话与审核工作区改造仍按后续步骤实施。实现、迁移差异及验证见 [IMP-10](DOCS/IMP-10语句算子与对象身份.md)。

审查加固后，结果可通过 `AnalysisResult.ResultScope` 覆盖调用范围，R035 全局汇总只附文档位置且每份报告保留一次。NodeId 按 Showplan `xsd:int` 规范化，缺失/无效值退回结构序号，原值保存在 XML。比较 UI 处理选择和未知异常，使 PropertyChanged 不打断连续 A/B 更新；CLI read 将取消传入身份构建并在输出前检查。详见[审查修复说明](DOCS/IMP-10审查修复与加固说明.md)。

### IMP-09 统一文档读取与能力契约

审查加固：每次识别独立缓存兄弟序号，循环和结果提交检查取消；空 XE 载荷计入 Diagnostics 与源序号；CLI 使用单一取消源传递至所有子命令；编码声明在既有预算内扫描至结束。[修复依据与全量验证](DOCS/IMP-09审查修复与加固说明.md)。

`InputRecognitionService` 实现 `IDiagnosticDocumentReader`，GUI 打开、XEL、CLI read/scan、refactor 辅助计划复用共享输入契约。文件与流先建立受预算约束的单份字节快照，SHA-256、严格解码和 XML/XEL 识别使用同一来源。XML 经 `SafeXmlHelper`，保留禁止 DTD、禁止外部资源解析，并在构建树前/过程中限制字节、字符、深度与节点。

`InputRecognitionResult` 包含来源 Envelope、Capabilities、Diagnostics、事件定位和原始快照。Success/Partial/Invalid/Unsupported/TooLarge/Cancelled 与既有 MalformedXml/ReadError/UnexpectedError 分离；`IsSuccess` 只表示 Success，`HasUsableContent` 允许显式消费 Partial 的有效死锁。未知字段和源位置保留，不把能力存在当作指标完整或语义正确的保证。

`DocumentOpenService` 为 GUI 提供共享结果，事件选择器和 ViewModel 保留输入上下文；XEL 事件保存带时区时间、源序号、字节偏移与长度。`IAnalysisEngine.AnalyzeInput` 传递来源和能力至 `AnalysisReport`，`ApplicationOrchestrator` 在重构前拒绝失败/Partial 辅助计划。CLI scan 继续输出 Passed/Failed 和非零失败码，read 输出契约 JSON；批量单文件失败后继续，取消停止。

未知异常复用 `ExceptionPolicy` / `UnexpectedErrorReporter`，产生去重且经校验的 DUMP 与异常侧车。Debug 记录 DEBUG/WARN/ERROR/CRITICAL，Release 只记录 ERROR/CRITICAL。完整结构、预算默认值测量依据、兼容性与测试边界见 [IMP-09](DOCS/IMP-09统一文档读取与能力契约.md)。语句/算子身份已由 IMP-10 接入，事件选择与来源定位已由 IMP-20 接通，跨启动的多事件会话持久化仍待后续实施。

### IMP-07 字段事实与展示

审查修复将运行时解析器的 `HasActual` 经节点构建结果和 UI 映射传入 `PlanNodeViewModel.HasActualRows`，成本输入和连线输入显式消费布尔标记，禁止从显示字符串判断数据是否存在。完整实际 0 可以参与计算，缺失或部分总数回退到既有估算；节点行数颜色及 SkewWarning 在未知总数时不输出偏差。连线输入记录第三参数为 `bool HasActualRows`，调用方须显式提供；详见 [加固及完整链路测试](DOCS/IMP-07审查修复与加固说明.md)。

`SqlXmlAnalyzer.Core.Services.PlanExecutionFactsService` 是执行模式、Parallel 与 Ordered 的统一读取器，限定当前算子与标准字段作用域。`IPlanExecutionFactsReader` 供节点构建使用；未知异常在构建边界进入 `ExceptionPolicy` 和真实 DUMP 诊断。属性面板复用同一事实读取和行计数服务，不再次猜测模式或布尔值。

行计数保留完整性标记，按 unsignedLong 解析、decimal 汇总用于精确显示；现有 double 数值仍供成本和布局计算。两处 XAML 分别绑定 `ActualRows` / `ActualRowsRead`；节点 `IsParallel` 为 `bool?`，只有 true 才显示并行徽标。死锁优先级仍兼容原字符串接口，但缺失使用空串，显示层转为 N/A。后续完整证据与导出模型继续由 IMP-11/20/22 完成。详见 [IMP-07 契约与验证](DOCS/IMP-07字段映射与绑定修正说明.md)。

### 2.1 专家规则引擎 (RuleEngine)

受启发于编译器中的 Linter 与商业软件的架构，SqlXmlAnalyzer 的核心竞争力在于其强大的 `RuleEngine`。

*   **`IPlanAnalyzerRule` 接口**：规则通过 `RuleMetadata` 声明稳定 RuleId、分类、默认严重级别和 `Plan/Statement/Operator` 作用域。
*   **引擎注册机制**：在 `RuleEngine` 初始化时，集中注册了如 `ImplicitConversionRule`, `KeyLookupRule`, `SpillDetectionRule`, `UdfAndTableVariableRule` 等规则类。
*   **高可扩展性**：如果要新增一个 SQL 反模式检测（例如并行死锁或残余谓词等），只需在 `Rules` 目录下新建一个实现了该接口的类，并在 `RuleEngine` 中 `RegisterRule` 即可，**无需修改任何现有业务逻辑**。

### 2.2 计划分析器 (PlanDiagnosticAnalyzer)
它作为规则引擎与 UI 之间的桥梁：
1.  将完整计划交给 `RuleEngine`。
2.  引擎根据规则元数据，在计划、语句或算子边界执行规则。
3.  将规则产生的 `AnalysisResult` 按元数据分类，不再通过 RuleId 字符串推断。
4.  将结果集合回传给 UI 层用于在侧边栏渲染出 **执行计划深度诊断报告**。

### 2.3 索引分析沙盒与评分系统 (Index Analysis Sandbox) *[New]*
索引沙盒读取 XML 中的 `<MissingIndexes>` 节点，支持候选索引配置与脚本生成：
*   **三栏式现代调优面板**：左侧为表内可用列选择器，中间为已选定 Key Columns（键列，支持上下调整顺序）与 Include Columns（包含列），右侧为智能评分与决策面板。
*   **沙盒与评分**：保留工具规则评分，明确不代表性能收益。IMP-08 已停止 UI 调用未校准的 `CostImpactSimulator`，使用非数值收益说明；底层模型留待 IMP-16 修复。
*   **回表参考区间**：使用行数、行宽及返回行数假设计算启发式区间，不是 SQL Server 优化器的确定性阈值，不预测必然 Seek/Scan。无效或非有限输入显示 N/A。
*   **CREATE INDEX 脚本生成**：在配置完成后，自动生成标准 T-SQL 索引创建脚本，支持一键复制代码。

---

## 3. 可视化渲染引擎 (UI 层)

`MainWindow` 只负责 WPF 事件协调。浏览器启动、Mermaid 临时页、临时文件生命周期和异步分析会话由 `BrowserLauncher`、`TemporaryFileManager` 和 `AnalysisSessionCoordinator` 管理。IMP-22 的报告按钮进入 `ReportReviewViewModel`，由 `DiagnosticReportExportService` 输出统一快照；旧 `PdfWordReportService` 保留兼容接口。

执行计划是一个复杂的树状图，WPF 的内建 `TreeView` 无法满足横向展开和算子间带权连线的需求。

### 3.1 基于 Nodify 的新一代渲染架构 (PlanGraphControl)
这是本项目的 UI 渲染核心，底层强力驱动引擎从手绘 Canvas 升级为了工业级节点库 **Nodify**。

*   **PlanNodeViewModel**：每一个解析出的算子都被封装为 ViewModel。其中计算了极其丰富的显示属性（如 `NodeSeverityColor`, `PartitionRangeColor` 等）。
*   **坐标系与递归布局 (`MeasureNode`)**：
    *   采用 **后序遍历 (Post-order Traversal)** 算法，自底向上计算每一个子节点的宽高和相对位置（Bottom-Up）。
    *   完美解决了节点折叠/展开动态触布局变更时的重叠与碰撞问题。
*   **事件隔离防穿透 (Hit-Testing Fix)**：针对 Nodify 画布会全局拦截 `MouseDown` 事件用于平移的特性，在节点的 `[+]`/`[-]` 按钮处创新性地使用了隧道事件 (`PreviewMouseLeftButtonDown`) 配合 `e.Handled = true` 强行阻断冒泡，实现了完美的精确点击伸缩体验。
*   **智能折叠 (Smart Collapse)**：
    *   算法在生成计划图时，会自底向上扫描所有算子的 `SubtreeCost` 和是否存在 `Warning/Critical`。
    *   当发现某分支既不包含任何性能告警，且其累计成本低于整个查询总成本的 5% 时，工具会将其自动收起，极大减少了 DBA 的视觉噪音。

### 3.2 死锁可视化与依赖推演 (Deadlock Visualization)
`.xdl` 提供持有、等待及受害者等快照信息。
*   **`DeadlockTimelineParser`**：从 IMP-13 共享图投影依赖解释步骤、环成员与全部受害者；未提供锁事件的真实时间序列。
*   **`DeadlockPlaybackViewModel` (动态帧渲染与水平步进时间轴)**：
    *   **水平步进器**：显示已展示/未展示/当前合成步骤及受害者。`AnalysisDisplayText` 的固定依赖推演说明不受播放或筛选状态影响。
    *   **现代交互操作**：移除原有纯文字按钮，升级为带有 PackIcon 的多功能控制栏（重置、上一步、播放/暂停、下一步），并提供播放速度微调（Slow/Medium/Fast）与一键“聚焦关键环”交互。
    *   与 `DeadlockGraphCanvas` 联动解释当前快照的 Request / Grant 关系，不还原不存在的事件历史；播放速度只控制展示间隔。

IMP-08 另以 `PlanComparisonRuntimeMetricsService` 提取当前算子完整运行指标，并用可空 `RuntimeMetricDelta.Value/Delta` 传播未知状态。比较、推演和沙盒的证据说明集中于 `AnalysisDisplayText`；比较与沙盒未知故障进入统一 DUMP 边界。范围锁只报告合法模式观测，根因仍需验证。详见 [契约、日志及验证](DOCS/IMP-08展示纠错与证据边界说明.md)。

### 3.3 参数嗅探并排对比卡片 (Parameter Sniffing side-by-side cards)
针对 SQL Server 严重的参数嗅探问题（Parameter Sniffing），在统计直方图面板引入了并排对比卡片：
*   **并排对比卡片 (Side-by-Side Cards)**：编译期参数（Compiled Parameter）和运行期参数（Runtime Parameter）的参数值分别采用淡蓝色（主题主色）与淡红色（告警色）并排展示。
*   **估算偏离比徽章 (Ratio Badge)**：计算由于编译参数和运行时参数的基数不一致导致的偏离比例，超出安全阈值时自动以醒目的橙色徽章进行性能警示。

### 3.4 无边框窗口与最大化自适应 (Borderless Window Maximization)
主界面采用了 Material Design 风格的无边框设计：
*   **标题栏鼠标拖动**：通过 TitleBar 拦截 `MouseLeftButtonDown` 实现双击最大化/还原以及按住拖拽移动窗口。
*   **任务栏工作区适配（Win32 钩子）**：为了防止普通 borderless 窗口在最大化时覆盖 Windows 任务栏，重写了 `OnSourceInitialized` 并注入 `WM_GETMINMAXINFO` 窗口消息钩子。动态查询当前显示器的工作区范围（WorkArea），精确限制最大化后的窗口边界。

## 4. 关键交互流程总结
1.  **加载文件** -> `XDocument.Load` 解析 XML。
2.  **提取树状数据** -> 递归构建 `PlanNodeViewModel` 树层级。
3.  **运行规则引擎** -> 诊断各种反模式，给 `PlanNodeViewModel` 标记红绿灯状态，并生成报告文本。
4.  **UI 测量与排版** -> 触发 Nodify 的坐标计算与连线路由逻辑。
5.  **渲染呈现** -> 通过 `ItemsSource` 呈现节点集合，允许用户自由缩放漫游，并利用隧道事件精准响应用户的折叠树图操作。
