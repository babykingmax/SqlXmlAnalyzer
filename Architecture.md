# SqlXmlAnalyzer 系统架构指南 🏛️

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
最新加入的杀手级特性，它深度解析 XML 中的 `<MissingIndexes>` 节点，构建了一个虚拟的索引调优沙盒：
*   **三栏式现代调优面板**：左侧为表内可用列选择器，中间为已选定 Key Columns（键列，支持上下调整顺序）与 Include Columns（包含列），右侧为智能评分与决策面板。
*   **沙盒模拟 (Sandbox) 与评分 (Index Scoring)**：在不动生产库的情况下，分析补齐索引能带来的预估性能提升（Impact）。结合表大小、读写比例等维度进行智能打分（配合多彩进度圆环），防止“伪缺失索引”带来的写惩罚。
*   **回表临界值分析器 (Tipping Point Analyzer)**：集成了 SQL Server 优化器回表阈值算法。支持输入表数据行数（Total Rows）、平均行宽（Avg Row Size）与预估返回行数（Returned Rows），动态计算 Tipping Point 区间并分析当前查询是否会因超出阈值而导致 Seek-Lookup 计划退化为 Table Scan。
*   **CREATE INDEX 脚本生成**：在配置完成后，自动生成标准 T-SQL 索引创建脚本，支持一键复制代码。

---

## 3. 可视化渲染引擎 (UI 层)

`MainWindow` 只负责 WPF 事件协调。浏览器启动、Mermaid 临时页、PDF/Word 导出、临时文件生命周期和异步分析会话分别由 `BrowserLauncher`、`PdfWordReportService`、`TemporaryFileManager` 和 `AnalysisSessionCoordinator` 管理。

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

### 3.2 死锁可视化与回放系统 (Deadlock Visualization)
除了查询执行计划，处理 `.xdl` 死锁图谱是另一大杀手锏。
*   **`DeadlockTimelineParser` (时序反编译器)**：内部执行有向图深度优先搜索算法（DFS），计算并找出成环（Cycle）的致命阻塞链路。
*   **`DeadlockPlaybackViewModel` (动态帧渲染与水平步进时间轴)**：
    *   **水平步进时间轴 (Horizontal Stepper Timeline)**：界面上方集成了横向 ListBox 步进指示器，每个事务状态演化节点均渲染为独立的气泡。气泡支持不同激活状态（已回放/未回放/当前执行帧/受害者 💀 节点高亮，伴随柔软发光阴影）。
    *   **现代交互操作**：移除原有纯文字按钮，升级为带有 PackIcon 的多功能控制栏（重置、上一步、播放/暂停、下一步），并提供播放速度微调（Slow/Medium/Fast）与一键“聚焦关键环”交互。
    *   像播放录像带一样与 `DeadlockGraphCanvas` 高频联动，逐帧还原资源的 Request 与 Grant 历史。

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
