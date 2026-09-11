# Changelog

## 2026-09-11 · 执行计划文档同步与差距评估

- 同步设计、架构、使用指南、实施规划、检索来源及测试文档，统一已实现能力与待实施范围。
- 对照 Plan Explorer / dbForge 官方资料，明确导入证据、在线采集、受控实验验证三者的差异；索引评分、教学图与语义验证不等同实测收益。
- 登记 PW-01～PW-05 的实际画布、诊断范围徽标、问题摘要、详情表达和命令层级问题；PW-06 为后续在线采集与实验历史能力。本条仅记录评估和文档工作，不表示这些问题已修复。
- 将几何/绑定/回归通过与可用性、真实 DPI、硬件键盘和正式发布批准分别记录。详见[设计](DOCS/plan-workspace-design-and-gap-assessment.md)和[验收矩阵](DOCS/verification/plan-workspace-redesign/acceptance-matrix.md)。
- 提交前重新执行 Debug/Release 全量重建（警告即失败）及测试：构建均 0 警告、0 错误，各 3066 项通过、0 失败、0 跳过。结果及本地日志校验值见[本轮验证记录](DOCS/verification/plan-workspace-redesign/publish-validation.json)，不替代后续独立检出或实机验收。
- 源码提交 `d3c92c2` 的独立 Git 检出随后再次通过 Release 强制重建及全量 3066 项测试，测试后工作树干净。源码提交排除生成产物与原始日志；旧临时 worktree 已注销，目录清理遇到权限错误及自动审批拒绝，临时清理仍未完成。

## 2026-09-10 · 执行计划工作区与图形优化

- 重组问题/检查状态、图/热点、详情及辅助证据，复用完整位置身份、指标状态与偏好保存。
- 调整节点卡片及浅/深色提示框；引入统一节点几何和迭代式紧凑树布局，保留 64 节点分页与取消。
- 修复首次布局/分页/旧定位动画之间的缩放时序，支持保持中心的比例切换、当前比例反馈及极小视口适应。
- 当日 Debug/Release 全量各 3066 通过；Release 离屏 24 组矩阵、1284 项断言、0 绑定错误。一次既有自 DUMP 测试停滞后带诊断全量复跑通过，不宣称转储实现已修复。原始记录见[计划图验证](DOCS/verification/plan-workspace-redesign/graph-polish.md)。

## 2026-09-10 · IMP-30 审查修复

- 统一校验候选身份与内嵌构建证明；绑定受测 EXE 哈希和所有命令原始输出。
- 进程退出与两路管道共用超时期限，保留部分输出；规范化相对路径，不覆盖既有命令证据。
- 预期校验/JSON/I/O 错误只记录 Error，真正未知的 PowerShell 错误继续生成 DUMP。
- 新增 17 项回归，Debug/Release 全量各 2838 通过，零构建警告/错误。见[加固说明](DOCS/IMP-30审查修复与加固说明.md)与[新候选验证](DOCS/verification/IMP-30-hardening/README.md)。可见窗口验收仍待补齐，未批准正式分发。

## 2026-09-10 · IMP-30 发布材料与回退

- GUI/CLI 同步生成 win-x64 自包含候选，强制重建、警告即失败、检查进程退出码和超时；整个目录及配置/依赖/文档参与 SHA-256 校验。
- 新增封存/验证/恢复服务：拒绝篡改、越界、重解析点、已有目标，私有暂存并复验后恢复新目录。未知异常生成 DUMP，日志严格按 Debug/Release 门禁输出。
- 用上一已验证源提交重建应用，演练旧配置/会话和原 SQL 文件恢复；应用回退不自动撤销数据库 SQL 或覆盖当前 SQL。
- 新增 30 项测试，双配置各 2821 通过、零构建警告/错误。实际窗口验收仍受桌面访问错误阻断；本地候选不批准正式分发。详见[实施说明](DOCS/IMP-30发布材料与回退验证.md)及[证据](DOCS/verification/IMP-30/README.md)。

## 2026-09-10 · IMP-29 验收审查修复

- 构建前冻结源码、测试前冻结生产/测试 DLL，所有场景执行前后校验并绑定构建证明；禁止用旧结果重新生成当前版本的通过结论。
- 改用 `-warnaserror` 和原生退出码，消除 10 个警告被判为零警告及本地化输出依赖；双配置证据目录增加精确 Git 例外。
- 新增 25 项脚本回归，Debug/Release 全量各 2791 通过；版本绑定、真实 DUMP、日志和场景证据见[修复说明](DOCS/IMP-29审查修复与加固说明.md)。

## 2026-09-10 · IMP-29 原实施验收

- 修复长报告预览的无限布局尺寸，普通/紧凑窗口均能到达保存按钮；可见原生保存对话框已验证。
- 新增 12 项场景、异常边界和布局回归；Debug/Release 全量各 2766 通过，solution build 各零警告/错误。
- 新增独立目录验收入口，复验 SQL Server 2019/2025、CLI、8 组 WPF、真实多事件 XEL、文件故障及冷/热性能；未知探针异常生成 DUMP，沿用双模式日志门禁。
- 更新 [R/D/UI 矩阵](DOCS/verification/IMP-29/acceptance-matrix.md)、[证据及复跑说明](DOCS/verification/IMP-29/README.md)。38 项范围内关闭、5 项缓解、4 项部分验收；M7 未全面关闭，未执行发布/回滚。

## 2026-09-10 · IMP-28 审查修复

- 修复配置版本迁移重写整个 JSON 导致合法大配置超限的问题；只插入新字段，保留未知内容的原始表示、Unicode、数字、字段名及规则别名。
- 在生成副本前核对 UTF-8 字节预算，超限返回 `CONFIG_MIGRATION_BUDGET_EXCEEDED`，保持原文件和活动配置；不放宽 1 MiB 限制。
- 新增 15 项回归；Debug/Release 全量各 2754 通过，构建零警告/错误。真实 DUMP 和分模式日志回归继续通过，见[修复说明](DOCS/IMP-28审查修复与加固说明.md)及[验证记录](DOCS/verification/IMP-28-hardening/README.md)。

## 2026-09-10 · IMP-28

- 冻结旧配置、2.0/2.1 会话、报告、CLI 输出及 34 条规则目录；交付兼容矩阵、转换样例与失败恢复记录，保留仍有消费者的旧适配器。
- 配置以专用 `ConfigurationSchemaVersion` 识别版本，支持无版本和字符串 `"1"`；保留旧通用 `schemaVersion` 扩展、未知规则及字段，拒绝未来声明版本。
- 会话读取校验版本和结构，旧格式提示另存；保存增加私有临时目录、落盘校验、取消与并发目标保护。行为变化：已有会话目标不再覆盖，请使用新名称。原方法重载及任意扩展名仍兼容。
- 保持 CLI 参数、退出码及 JSON 旧字段，修正默认配置目录的帮助信息；会话未知异常复用 DUMP 与分模式日志。
- 新增 48 项测试；Debug/Release 全量各 2739 通过，构建零警告/错误，实际进程与文件转换每配置 22 项检查通过。见[迁移说明](DOCS/IMP-28模型配置报告与CLI兼容性.md)及[验证记录](DOCS/verification/IMP-28/README.md)。

## 2026-09-10 · IMP-27 审查修复

- 将六个规则的版本统一到元数据目录，纠正包内默认版本误报，保留报告快照的实际版本及默认严重度。
- 缺失证据纳入 Incomplete/Ambiguous；限制为已定义的规范指标状态，保留自由文本排除边界。
- 非法 UTF-8 日志明确报编码错误，继续按预期输入失败处理且不生成 DUMP。
- 新增 7 项回归；Debug/Release 各 2691 项通过、构建零警告/错误，见[修复说明](DOCS/IMP-27审查修复与加固说明.md)及[验证记录](DOCS/verification/IMP-27-hardening/README.md)。

## 2026-09-10 · IMP-27

- 新增本地诊断包审核入口：明确选择内容，逐文件预览实际字节、大小、隐私状态和 SHA-256，保存固定快照为不覆盖已有文件的 ZIP。
- 默认收集版本、采集条件、报告实际配置、当前会话配置、错误/缺失证据及脱敏报告；原始选中 XML、日志、DUMP 均需显式选择，无自动发送。
- 增加包结构版本、流式校验、附件预算、取消/临时文件清理、过期报告降级；复用未知异常 DUMP 与 Debug/Release 日志门禁。
- 新增 39 项测试，两配置全量各 2684 通过；WPF 截图、真实 DUMP 与日志验证见 [IMP-27](DOCS/IMP-27可审核的本地诊断数据包.md)。

## 2026-09-10 · IMP-26 审查修复

- 修复连续切换语句导致忙碌状态无法结束，以及首屏翻页误清空整个计划图。
- 大图布局同步所有连线方向；后台模型提交时使用当前显示选项。
- 加固最新任务等待、失败清理、取消传播和翻页视口定位，新增 16 项单元回归并补充主窗口交互验证；见 [修复说明](DOCS/IMP-26审查修复与加固说明.md)。

## 2026-09-10 · IMP-26

- 增加分析阶段、状态栏取消、迟到结果隔离和分批呈现取消清理。
- 图准备移入后台，计划图每页最多 64 个节点，指标表保留全部算子；复用诊断快照，移除改写路径的临时 SQL 读取及重复分析。
- 新增 32 项单元回归，验证未知异常 DUMP、分模式日志、实际 WPF 和性能前后数据；详见 [IMP-26](DOCS/IMP-26异步状态取消与大图性能.md)。


All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### IMP-25 审查修复与加固（2026-09-10）

- 计划滚动范围计入恢复后的侧栏列宽和后续拖动，并在收起后缩回。
- F6 跳过禁用、隐藏、不可聚焦和拒绝焦点的目标，支持反向循环；加固循环索引溢出边界。
- Enter、Automation Invoke 和双击共用属性入口，展开侧栏并滚动聚焦；Escape 返回原节点。
- 新增 22 项单元回归，复验异常 DUMP、分模式日志及每配置 54 种 WPF 矩阵；详见 [修复说明](DOCS/IMP-25审查修复与加固说明.md)。

### IMP-25 布局、主题与键盘操作（2026-09-10）

- 新增紧凑布局、可滚动标签/导航、语义主题和减少动画；修复折叠侧栏仍占宽度、内部展开事件冒泡及深色文字对比度问题。
- 导航按钮和快捷键共用命令，提供 F6/F8 焦点与诊断导航、图节点 AutomationPeer、证据及对话框焦点返回；报告文件选择纳入异常处理。
- 新增 26 项回归并验证真实 DUMP、分模式日志、生产 WPF 组合矩阵和 HTML 导出；实体键盘、真实 DPI 与屏幕阅读器验收仍待人工完成。见 [IMP-25](DOCS/IMP-25布局主题与键盘操作.md)。

### IMP-24 审查修复与加固（2026-09-10）

- 执行计划文档、路径和输入来源只在成功后一起提交；重算及 SQL 改写复用已读取的快照，历史记录打开不提前改写来源。
- 配置切换仅取消执行计划请求；旧请求不能取消或重新分类新请求。
- JSON 校验改用路径片段并限制错误路径长度，支持取消；非法 Unicode 按已知输入错误处理，未知程序异常仍生成 DUMP。
- 新增 22 项回归，Debug/Release 全量各 2549 通过、构建各零警告/错误；WPF 交错取消、真实 DUMP、日志门禁和受限内存验证见[加固说明](DOCS/IMP-24审查修复与加固说明.md)。

### IMP-24 兼容规则配置界面（2026-09-10）

- 新增 34 条执行计划规则的配置窗口：筛选、启用、严重度覆盖、默认草稿恢复、变更预览、文件保存及独立会话应用。
- 未知规则由拒绝加载改为只读保留并警告，保留未知 JSON 字段及旧别名；严格校验定位、编码与预算，不新增规则 ID 或修改默认级别。
- 分析捕获不可变配置，切换后取消旧请求并提示重算，图节点复用本次诊断报告；保存复用备份、源校验和同目录替换，提交确认失败明确报告实际状态。
- 新增 47 项回归，Debug/Release 全量各 2527 通过、构建各零警告/错误。DUMP、分模式日志及实际 WPF 窗口流程见 [IMP-24 说明](DOCS/IMP-24兼容规则配置界面.md)。

### IMP-23 审查修复与加固（2026-09-10）

- 修复聚合嵌套序列化导致的内存放大，采用 `structure-v2` 紧凑拓扑表示及标签/比较量预算，超预算事件独立保留。
- 补齐 `currentdbname` 搜索与结构身份；接受可读但无死锁的 XEL 滚动文件；阻止追加重置已修改来源的追溯基准。
- 初始化期间禁用筛选/添加并在入口防御抢占，保留取消恢复和旧请求不能覆盖新结果的行为。
- 新增 21 项回归，Debug/Release 全量各 2480 通过、构建各零警告/错误。真实 XEL、受限堆及 WPF 宿主复验见[加固说明](DOCS/IMP-23审查修复与加固说明.md)。

### IMP-23 XEL 搜索、聚合与事件追溯（2026-09-10）

- 新增多文件检索窗口，支持带偏移时间、数据库/对象/文本筛选和原始事件定位。
- 按资源身份、锁模式和完整依赖拓扑精确聚合，保留重复副本和原始索引，不单凭 SPID 合并。
- 增加集合预算、取消和过期结果保护；复用未知异常 DUMP、Debug 四级/Release 错误及致命日志。
- 新增 45 项回归；Debug/Release 全量各 2459 通过、构建均零警告/错误。真实 XEL 和 WPF 测试宿主验证通过，行为及交互验收限制见 [IMP-23](DOCS/IMP-23XEL搜索聚合与事件追溯.md)。

### IMP-22 报告预算与游标导出加固（2026-09-10）

- 在事实收集、快照构造和渲染期间执行共享预算，压缩全缺失指标，缓存预览正文并传递取消；CLI 超预算返回精简失败结果。
- 保留所选游标的 CursorPlan/Operation、语句选项及命名空间；原始/脱敏 sqlplan 通过官方 XSD。RECEIVE 单操作范围明确拒绝，完整范围仍可导出。
- 新增 26 项回归，Debug/Release 全量各 2414 项通过、构建各零警告/错误；分配量对比、行为边界与全量验证见[加固说明](DOCS/IMP-22审查修复与加固说明.md)。

### CLI 扫描与报告加固（2026-09-09）

- 修复扫描输出覆盖源计划/硬链接/既有报告，以及 console/未知格式写出空文件；JSON/JUnit 仅发布完整的新文件。
- 拒绝无效阈值、缺少值及未知选项；非有限/负数/非法源开销返回失败，缺失开销不再冒充 0 通过阈值。JSON 新增 `MaxSubtreeCostState`。
- 修复包含 SeekPredicates 的扫描算子漏检（包括合法的分区 Clustered Index Scan），保留脱敏报告的 `optimizer-cost` 单位。
- 新增 46 项回归，Debug/Release 全量各 2388 项通过，构建均零警告/错误；行为变化与 Microsoft / .NET 官方依据见[加固说明](DOCS/CLI扫描与报告加固-2026-09-09.md)。

### IMP-22 统一报告模型与脱敏预览（2026-09-09）

- GUI、HTML/PDF/Word/JSON 共用不可变选择快照，CLI 扫描 JSON 新增 `DiagnosticReport`；验证源版本及事件指纹，保留指标状态、规则与范围。
- 新增默认脱敏预览、类别/数量/未覆盖项和前后样例，覆盖正文、图标签、元数据及 XML；禁止覆盖并清理取消/失败的暂存文件。
- 新增 31 项回归，两配置全量各 2342 项通过；真实 WPF、PDF/Word 渲染、标记扫描与浏览器安全策略造成的验收限制见 [IMP-22](DOCS/IMP-22统一报告与脱敏预览.md)。

### IMP-21 审查修复与加固（2026-09-09）

- 修复索引失败通知被订阅者异常中断、三个输出入口遗漏未脱敏说明、应用后导出误报尚未应用。
- 加固选项通知异常边界；导出成功保留原应用错误及 DUMP 信息。
- 新增 15 项回归，Debug/Release 全量各 2311 项通过，构建各零警告/错误；真实 WPF 及证据见[加固说明](DOCS/IMP-21审查修复与加固说明.md)。

### IMP-21 A/B、改写和索引审核界面（2026-09-09）

- A/B 增加可见设置按钮及各侧未匹配清单，保留采集条件和指标可比性检查。
- 改写提供逐项前提、风险、证据、精确 diff、完整报告和 SQL 对照；区分复制、新文件导出与验证后备份应用。候选导出拒绝覆盖已有文件并避免发布部分内容。
- 索引支持完整对象审核、键序/INCLUDE、自定义名称、环境选项及配套回滚；非法选项清除旧脚本，原优化器指标与沙盒假设分区展示。
- 新增 23 项回归，验证真实 DUMP、诊断失败降级和分模式日志；两配置完整验证与 WPF 截图见 [IMP-21](DOCS/IMP-21审核界面.md)。

### IMP-20 语句 / 事件选择与诊断证据（2026-09-09）

- 审查修复：消除死锁手动选择后的陈旧证据、折叠定位后的节点重叠及重构失败/DUMP 提示丢失；加固清空与过期结果保护。再增 15 项回归，两配置全量各 2273 项通过；见[加固记录](DOCS/IMP-20审查修复与加固说明.md)。

- 执行计划按 Batch / Statement / QueryPlan 同步 SQL、图、树、指标表、索引与问题列表；诊断严重度、置信度和运行状态分别显示，证据可定位原始 SQL/XML。
- 死锁事件保留原始采集位置，快速切换只应用最新结果；诊断联动进程、资源和图，修复同名资源串选。无算子、缺少证据、规则失败有明确空态；清空会取消请求并释放事件选择器。
- 新增 30 项回归，覆盖未知异常真实 DUMP、失败降级及分模式日志；生产 WPF 主窗口、截图和完整验证见 [IMP-20 实施记录](DOCS/IMP-20语句事件选择与诊断证据.md)。

### IMP-0 到 IMP-19 增强（2026-09-09）

- 汇总阶段 0 基线、反例矩阵、输入与输出保护、统一事实及诊断协议、死锁图、规则与索引模拟、多语句 A/B 比较、可审核 SQL 提案和语义验证后可靠应用；具体范围与限制见 [实施规划](DOCS/软件改善实施规划.md)。
- 提交前从暂存文件导出独立目录验证，Debug/Release 构建各 0 警告、0 错误，测试各 2229 项全部通过，见 [发布验证摘要](DOCS/verification/IMP-0-19-publish/publish-validation.json)。保留可复现附件及原始字节；原始 TRX、日志、DUMP、备份和构建产物不纳入 Git，历次测试计数和摘要另行归档。

### IMP-18 / IMP-19 联合审查修复（2026-09-09）

- 无损保留 SQL 返回的未配对 UTF-16 码元；读取任何行前拒绝不能完整比较内部属性的 sql_variant，包括空结果、全 NULL 和对象快照。
- SQL 字节/字符预算前移至 AST 和规则执行之前；应用复查及写回校验同样有界。建库确认失败或取消后仍以独立连接清理本轮库，失败保留诊断。
- WPF 顶部、预览和详情同步显示真实提交结果及备份。新增 47 项回归，Debug/Release 全量各 2229 项通过，完整构建各 0 警告/错误。见 [修复说明及证据](DOCS/IMP-18-19联合审查修复与加固说明.md)。

### IMP-19 审查修复与加固（2026-09-09）

- 修复观察器可通过 DELETE/UPDATE 等写操作擦除真实数据差异的问题；ObserveSql 独立限制为只读 SELECT，阻断 SELECT INTO、嵌入 DML、赋值、事务和控制语句，验证/应用入口在数据库工作前检查。
- 将只读观察器移到内部状态查询之前，保留受测 SQL 的 ROWCOUNT/ERROR；相同错误不能作为可应用证据。修正跨批次 RecordsAffected=-1 的哨兵值累计，保留“不适用”与零行 DML 的区别，非法/溢出计数明确失败。
- 新增 41 项回归；Debug/Release 各 2182 测试通过，构建各 0 警告/错误。SQL Server 2019/2025 每配置新增 26 项场景及真实应用阻断检查通过，未知异常 DUMP 和分模式日志回归通过。见 [修复、兼容性与验证记录](DOCS/IMP-19审查修复与加固说明.md)。

### IMP-19 SQL 语义验证与可靠应用（2026-09-09）

- 新增专用 LocalDB 原 SQL/候选 SQL 比较，记录版本、兼容级别、排序规则、固定会话设置、结果/重复行/类型、错误、事务及对象观测；每侧独立新库、无登录用户、不可恢复身份、有界流读取和清理失败阻断。
- 新增 `semantic-compare`、`rewrite-validate`、`rewrite-apply`；桌面接入验证、场景审核确认及带备份应用。应用绑定源字节、提案版本/选择/预览和场景，使用一次性进程内凭据与既有可靠写回服务；保留提交不确定及报告输出失败后的实际写入状态。`refactor` 仍只输出提案。
- 新增 58 项回归；Debug/Release 各 2141 测试通过、构建各 0 警告/错误，两版本 SQL Server 每配置 42/42 场景回归通过，CLI 与 WPF 实际验证/写回/备份及四张截图通过。未知异常 DUMP、日志模式和失败降级已测试。场景通过不宣称一般等价或性能改善，TRIM/表变量转换维持限制。见 [实现、使用与证据](DOCS/IMP-19SQL语义验证与可靠应用.md)。

### IMP-18 审查修复与加固（2026-09-09）

- 修复终端/文本文件输出完整候选而忽略选择的问题，统一展示 `Review.PreviewSql` 并保留原始空白与注释；更新 `--show-sql` 帮助。
- 补齐 SELECT INTO 创建目标检查，加固完整标识符、大小写、DROP IF EXISTS、批次与 IF/WHILE/TRY-CATCH 分支；明确完整对象定义与生命周期尚未证明。
- 新增 36 项回归，Debug/Release 完整构建各 0 警告/错误，全量测试各 2083 通过、0 失败、0 跳过。保留真实 DUMP、诊断失败和分模式日志验证。见 [检索依据与验收记录](DOCS/IMP-18审查修复与加固说明.md)。

### IMP-18 SQL 改写提案（2026-09-09）

- 引入源/步骤 hash、SQL diff、规则版本、前提/风险/证据、验证性质、依赖和选择状态；未证明等价的候选默认未选择。
- CLI `refactor` 停止自动写回，新增可重复 `--select <ID>` 仅供审核预览；GUI/快速修复共用审核窗口，选择后重验组合，取消全部选择恢复原文。
- 建立批次符号与临时名称预留，限制未知作用域和清理归属；旧表变量访问器不能绕过提案安全边界。
- 新增 35 项回归，Debug/Release 各 2047 项通过、构建各 0 警告/错误；包含真实 DUMP 和分模式日志验证。截图受当前桌面限制未完成，数据库等价性与可靠应用留待 IMP-19。见 [交付说明](DOCS/IMP-18可审核SQL改写提案.md)。

### Fixed

- IMP-17 evidence hardening: fingerprint BuildResidual separately from ProbeResidual, include ordered Sort/TopSort semantics, and reject incomplete object identities even for manual comparisons. Propagate incomplete evidence through ancestor and QueryPlan matching; preserve valid local and object-free plans. Add 55 regressions; Debug/Release each pass 2012 tests with zero build warnings/errors, including native DUMP and logging policy checks. See [behavior and verification](DOCS/IMP-17比较证据完整性加固.md).

- IMP-17 review: include ordered range columns, comparison bounds and typed predicate expressions in operator identity; keep incomplete evidence at candidate confidence without numeric operator deltas. Separate A/B scope selection from shared snapshots and roundtrip it in session 2.1. Retain recorded source hashes with XML revision binding, reject changed current-format content, and label unverifiable legacy hashes as historical metadata. Add 29 regressions; Debug/Release each pass 1957 tests with zero build warnings/errors. See [fixes, compatibility and evidence](DOCS/IMP-17审查修复与加固说明.md).

- IMP-17 verification probe: catch screenshot/result I/O failures at the process entry point, preserve existing artifacts with create-only writes, and return explicit failure codes instead of leaking managed exceptions. Capture unknown failures as native DUMPs with independent diagnostics; verify exclusive file locks and diagnostic sink failures in real child processes. See [probe fix and evidence](DOCS/IMP-17验证探针异常处理修复.md).

- IMP-17: compare all captured statements before matching QueryPlans and operators, using unique SQL/object/structure evidence with explicit candidate, manual and unmatched states. Gate nullable metric deltas on captured versions, SET options, parameters and execution basis; retain absolute changes with a zero baseline and remove ungated total-cost improvement claims. Add manual scope selectors, paired statement trees, source metadata and session selection roundtrips. Add 52 regressions; Debug/Release each pass 1928 tests with clean builds, native DUMP/log checks and production WPF binding/screenshot validation. See [comparison contract and evidence](DOCS/IMP-17多语句与可比性检查的AB比较.md).

- IMP-16 review: recognize parenthesized and signed numeric values and structured NULL predicates; read only independent AND conjuncts so CASE branches cannot earn predicate points. Require local evidence for quoted parameter names and reject malformed or foreign scalar shapes. Version the score model at 2.0.1 and RULE020/035 at 2.1.1. Add 60 regressions; Debug/Release each pass 1876 tests with clean builds, real DUMP/logging and CLI checks. Eight native SQL Server plan cases and the production WPF layout/binding probe pass; converted ConstExpr values remain explicitly unsupported. See [hardening and validation](DOCS/IMP-16审查修复与加固说明.md).

- IMP-16: compute the complete captured own-cost denominator before evaluating index candidates; union overlapping operators without stacking subtree costs or predicted gains. Publish model versions, evidence, weights and input assumptions; separate captured SQL Server Impact from tool scores and retain unknown forecasts. Missing coverage no longer earns 40 points, and predicate text uses syntax parsing. Add 50 regressions; Debug/Release each pass 1816 tests with clean builds, real DUMP/logging and CLI checks. Full WPF layout/bindings pass; off-screen bitmap capture remains unavailable. See [implementation and validation](DOCS/IMP-16模拟顺序依赖与模型限制.md).

- IMP-15 review: require entity ownership before offering plan columns for index DDL; retain raw ColumnReference names, including literal brackets. Resolve Sort independently within its captured QueryPlan, validate every order column and direction, and keep multiple orders separate. Add 46 regressions and a captured SQL Server plan; Debug/Release each pass 1766 tests with clean builds, native DUMP/logging and CLI checks. Real LocalDB CREATE/rollback and entity/Sort validation pass. See [hardening evidence](DOCS/IMP-15审查修复与加固说明.md).

- IMP-15: quote identifier parts without splitting embedded dots; retain database/server context, validate options and generate one deterministic SHA-256 based name for create/rollback scripts. Scope index extraction, SQL binding, scoring and sandbox evidence to captured object/QueryPlan identities; publish RULE035 candidates once per statement and retain source identities in isolated rule XML. Prevent deployment-comment breakout, reject unresolved/ambiguous targets, and retain explicit schema-validation limits. Add 36 regressions; Debug/Release each pass 1720 tests with clean builds, DUMP/log/CLI checks and five real LocalDB target/key/include/rollback/server-guard checks. See [implementation and evidence](DOCS/IMP-15索引目标身份与DDL标识符.md).

- IMP-14 review fix: preserve standalone/detached RelOp NodeId in native diagnostics, merged findings, all run outcomes, legacy results and JSON/text reports. Capture the source identifier before evaluation; keep invocation/diagnostic scopes separate and full Location identity authoritative. Add 26 regressions; Debug/Release each pass 1684 tests with zero build warnings/errors, including real DUMPs, logging and CLI checks. See [hardening and verification](DOCS/IMP-14审查修复与加固说明.md).

- IMP-14: use one native evaluation contract for RULE004/030 cardinality and RULE006/034 residual checks, with shared per-execution measurements, explicit denominators and semantic deduplication retaining all origins/predicates. Support local Scan/Seek residuals without requiring Seek ScalarString; distinguish missing/inconsistent counters and undefined zero-output ratios. Replace asserted root causes and function-keyword matches with bounded ScriptDom column-argument hints. Keep IDs/configuration/numeric thresholds, version these rules at 2.0.0; measured RULE006 read amplification is Warning and RULE034 now covers scans. Add 79 regressions; Debug/Release each pass 1658 tests with zero build warnings/errors, validated native DUMPs, capture-failure handling, build-specific logging and CLI process checks. See [implementation and evidence](DOCS/IMP-14基数与残差谓词规则修复.md).

- IMP-09–13 joint review fixes: share ownership-aware plan capabilities across input and default diagnostic entries, including empty Statements and opaque extensions; intersect offset deadlock lines with node boundaries and reject non-finite geometry; release retained inputs and source snapshots when clearing results. Add 38 regressions; Debug/Release each pass all 1579 tests with zero build warnings/errors, including native DUMP, log filtering, CLI output, input lifetime and production WPF rendering. See [fixes and verification](DOCS/IMP-09至13联合审查修复与加固说明.md).
- IMP-13 review fixes: replace quadratic victim validation with indexed membership; carry EvidenceId through WPF drawing, drag, playback and badge caches, with bounded parallel offsets and indexed event projection. Build Range evidence from typed mode fields, including dangling raw relations, and restore Mermaid index labels. Preserve cancellation through safe XML reads. Add 17 regressions; Debug/Release builds have zero warnings/errors and each full suite passes 1541 tests, including native DUMP, log filtering and production WPF rendering. See [hardening and evidence](DOCS/IMP-13审查修复与加固说明.md).
- IMP-13: share parsed deadlock facts, resource/link identities, the complete victim set and iterative SCC membership across graph, dependency playback, diagnostics and reports. Normalize explanatory cycles while preserving distinct resource edges; bound path count, length and search work with visible truncation. Require observed Range modes, attach structured evidence and qualify root-cause hypotheses. Preserve cycle/victim styling after playback and reuse completed GUI analysis for HTML export. Add 43 regressions including a 10,000-node graph, real DUMP, log filtering and production WPF component rendering. Debug/Release builds have zero warnings/errors; each full suite passes 1524 tests. See [implementation and verification](DOCS/IMP-13统一死锁图环与诊断依据.md).
- IMP-12: introduce a versioned diagnostic protocol with immutable analysis context, evidence, confidence, hypotheses, recommendations, limitations, complete locations and semantic deduplication retaining every origin. Record Hit/NoHit/Skipped/Failed for rule invocations; stop swallowing rule failures, preserve DUMP results, and block refactoring on Failed. GUI, both CLI entries, JSON/HTML/text/JUnit reports share the protocol. The initial implementation added 45 regressions. See [contract, compatibility and verification](DOCS/IMP-12有证据和运行状态的诊断协议.md).
- IMP-12 review fixes: preserve the four existing result-ID branches while validating their owning implementation; retain original configuration ownership and matching versions when merging. Separate node execution status from performance warnings, mark serial thread-skew checks as not applicable, and include PreservedSource in default capability inference. Add 47 regressions, including production WPF tooltip rendering; Debug/Release builds have zero warnings/errors and each full suite passes 1481 tests, including real DUMP and log-filter checks. See [hardening and evidence](DOCS/IMP-12审查修复与加固说明.md).
- IMP-11 review: accept signed xsd:int thread IDs with invariant parsing and XML whitespace; normalize equivalent spellings before duplicate detection, retain raw values and abstain from negative thread roles. Add 33 regressions including schema-valid cross-output checks and safe thread diagnostic logging. Full Debug/Release builds have zero warnings/errors and each suite passes 1389 tests. See [hardening evidence](DOCS/IMP-11审查修复与加固说明.md).
- IMP-11: centralize operator-local facts and metric aggregation across rules, graph/table/details, comparison runtime metrics and CLI JSON. Stop child RelOp objects, predicates and scalar conversions leaking into parent facts; preserve unknown versus zero and exact thread counts. Use a common execution denominator for cardinality, worker-only skew, and shared own/subtree costs. Missing evidence can suppress previous diagnostics; no rule IDs or configured thresholds change. Add 44 regressions including real DUMP, failure handling and Debug/Release logging; both full suites pass 1356 tests. See [metric contract and evidence](DOCS/IMP-11集中算子事实与指标口径.md).
- IMP-10 review: bind R035 global summaries to document scope and deduplicate them while retaining operator suggestions; contain comparison failures at the UI boundary so swap/load/clear finish; propagate CLI read cancellation through identity construction and check before output; normalize RelOp NodeId as xsd:int to bound repeated parent keys. Add 22 regressions; Debug and Release each pass 1312 tests with validated native DUMP and logging. See [hardening evidence](DOCS/IMP-10审查修复与加固说明.md).
- IMP-10: prevent repeated NodeId values from sharing graph selection, stop parent operators borrowing child objects, preserve nested statement ownership, and require exact source/object identity for missing-index association. Multi-query comparison requires explicit selection; multi-statement analysis preserves all SQL and withholds automatic rewriting until a statement is selected. Classify invalid identity/selection data as expected errors without DUMP.
- IMP-09 review: index sibling positions once per read and honor cancellation during traversal; retain empty XE payload diagnostics and source ordinals; propagate CLI cancellation through redact/refactor; read complete XML encoding declarations within existing budgets. Add 20 regressions; Debug and Release each pass 1247 tests. See [hardening evidence](DOCS/IMP-09审查修复与加固说明.md).
- IMP-09: unify bounded XML/XEL document reads, capture metadata, capability flags and explicit Partial/TooLarge/Cancelled outcomes. Preserve source snapshots, unknown fields and skipped event locations; reject incomplete input before SQL writeback. GUI retains partial event context; CLI failures remain nonzero and batches continue after individual input failures. See [contract and verification](DOCS/IMP-09统一文档读取与能力契约.md).
- IMP-08 review: make dependency inference and sandbox evidence text follow the active theme, including unknown-benefit and invalid-assumption states. Add eight resource-replacement regressions and real WPF theme/contrast verification; Debug and Release each pass 1183 tests. See [hardening evidence](DOCS/IMP-08审查修复与加固说明.md).
- IMP-08: preserve unknown runtime metrics in A/B comparisons, label estimated costs and dependency inference explicitly, require observed Range lock modes, and distinguish diagnostic hypotheses. Suspend uncalibrated sandbox benefit percentages and absolute Seek/Scan promises. Add 40 regressions, native dump/logging checks and real WPF state verification. See [implementation and evidence](DOCS/IMP-08展示纠错与证据边界说明.md).
- IMP-07 review: propagate explicit runtime-row availability through graph loading, recosting and connections. Preserve estimated own cost for missing/invalid/partial counts and preserve observed zero rows. Add 29 regressions, cost debug logging and WPF reload verification. See [hardening evidence](DOCS/IMP-07审查修复与加固说明.md).
- IMP-07: read standard deadlock priority before compatibility aliases; preserve missing versus zero. Read execution mode, Parallel and Ordered in their correct scopes without invented defaults. Separate output/read rows in both WPF tables, graph tooltips and details, preserving exact large counts; repair legacy PlanView event handlers. Add field, WPF binding, real dump and build-specific logging regressions. See [implementation and evidence](DOCS/IMP-07字段映射与绑定修正说明.md).
- IMP-06 review: reject nested ShowPlan namespace mismatches and malformed encoded bytes; use strict BOM retry on the same stream while preserving InternalInfo extensions. Read refactor auxiliary plans as XML byte streams and retain real source paths when selecting deadlock events. See [hardening and verification](DOCS/IMP-06审查修复与加固说明.md).
- IMP-06: Share minimum XML input recognition across GUI, scan, desktop CLI and refactor plan analysis. Reject unrelated XML, unsupported ShowPlan namespaces and damaged structures with explicit input status; invalid scan inputs return Failed and exit code 1.
- Preserve prefixed ShowPlan and schema-valid statements without RelOp. Expose every deadlock XML event through the existing selector; single-event parsers reject ambiguous collections.
- Propagate input failures before SQL refactoring/writeback, and route unexpected failures through the existing DUMP and Debug/Release logging policy.

### Added
- IMP-10: add document/batch/statement/query-plan/operator keys, four-part object references, source locations and revision-aware legacy adapters. Propagate identity to reports, graph nodes, snapshots and CLI outputs; render all query plans. Add 43 regressions and real DUMP/logging/WPF verification; Debug and Release each pass 1290 tests. See [implementation and evidence](DOCS/IMP-10语句算子与对象身份.md).
- IMP-09: add `IDiagnosticDocumentReader`, configurable `DocumentReadOptions`, CLI `read` and `--read-options`, contract/budget/XEL/WPF regressions, native DUMP and Debug/Release logging verification.
- Input recognition, schema-validated fixture, CLI, event selection, cancellation and native DUMP regression tests. See [IMP-06 implementation and verification](DOCS/IMP-06统一最低输入识别说明.md).

## [2.0.0] - 2026-06-24

### Fixed
- **Core Rules**: Reduced duplicate warnings in parameter sniffing rule (`ParameterSniffingRule.cs`) by restricting execution strictly to `NodeId = 0` (Plan level).
- **Core Rules**: Eliminated false positives in parameter sensitivity detection (`QueryRewriteRule.cs`) by removing the assumption that statistics usage alone implies parameter sniffing; compile-time and runtime parameter value differences are now required.
- **Core Rules**: Filtered out healthy statistics info alerts (with `Info` severity) from `StatsUsageRule.cs`, raising warnings/critical flags only for actual issues (e.g. stale stats, low sampling, high modifications).

### Added
- **SQL Refactoring**: Added `TryRewriteSelectedSubquery` to `ScalarSubqueryToJoinRule.cs` to enable targeted, single-expression scalar subquery-to-join rewrites by offset and length.
- **WPF UI**: Introduced inline quick-fixes for rewriteable subqueries within the original SQL diff viewer. Clicking the lightbulb icon launches a side-by-side comparison in `QuickFixWindow` and allows applying the rewrite localized to that subquery.
- **WPF UI**: Added SQL tokenization and syntax highlighting in the quick-fix and diff viewer (comments, strings, standard keywords, and generated aliases `t_sub_*` and `agg_*`).
- **Tests**: Added suite of unit tests for parameter sensitivity rules (`ParameterSensitivityRuleTests.cs`) and targeted scalar subquery rewrites (`ScalarSubqueryQuickFixTests.cs`).
