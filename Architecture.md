# SqlXmlAnalyzer 系统架构指南 🏛️

本文档深入解析 SqlXmlAnalyzer 的内部设计与代码架构。软件采用高度解耦的 **模块化多层架构**，确保在不断扩充高级诊断规则或重构可视化渲染层时，互相不产生破坏。

## 1. 宏观架构视图

整个系统拆分为三个主要项目（Project）：

1.  **SqlXmlAnalyzer.Core** 🧠
    *   无 UI 依赖的核心解析与诊断模块 (Class Library)。
    *   负责 XML 文件的解析、执行计划结构的提取、以及最核心的 `RuleEngine` (规则引擎)。
2.  **SqlXmlAnalyzer** 🖥️
    *   基于 WPF (Windows Presentation Foundation) 的桌面 UI 应用。
    *   引入了强大的 **Nodify** 节点图框架，取代了传统的 TreeView，利用 MVVM 和 Code-Behind 实现复杂树状图的动态渲染、自适应排版、智能折叠及事件交互。
3.  **SqlXmlAnalyzer.Tests** 🧪
    *   基于 xUnit 和 FluentAssertions 的单元测试网。涵盖核心逻辑与所有三十余条 P0/P1/P2 诊断规则的正确性校验。

---

## 2. 核心模块详解 (SqlXmlAnalyzer.Core)

### 2.1 专家规则引擎 (RuleEngine)
受启发于编译器中的 Linter 与商业软件的架构，SqlXmlAnalyzer 的核心竞争力在于其强大的 `RuleEngine`。

*   **`IPlanAnalyzerRule` 接口**：所有的分析规则必须实现此接口。它要求返回 `RuleId`、`Name` 以及执行具体的 `Analyze(XElement relOp, XNamespace ns)` 方法并输出 `AnalysisResult`。
*   **引擎注册机制**：在 `RuleEngine` 初始化时，集中注册了如 `ImplicitConversionRule`, `KeyLookupRule`, `SpillDetectionRule`, `UdfAndTableVariableRule` 等规则类。
*   **高可扩展性**：如果要新增一个 SQL 反模式检测（例如并行死锁或残余谓词等），只需在 `Rules` 目录下新建一个实现了该接口的类，并在 `RuleEngine` 中 `RegisterRule` 即可，**无需修改任何现有业务逻辑**。

### 2.2 计划分析器 (PlanDiagnosticAnalyzer)
它作为规则引擎与 UI 之间的桥梁：
1.  递归遍历 SQL XML 的每一个 `<RelOp>` 算子节点。
2.  调用 `RuleEngine` 让所有规则同时在当前节点上运行（"扫描式" 诊断）。
3.  将规则产生的 `AnalysisResult` 按照预设的分类收集到字典中。
4.  将结果集合回传给 UI 层用于在侧边栏渲染出 **执行计划深度诊断报告**。

### 2.3 索引分析沙盒与评分系统 (Index Analysis Sandbox) *[New]*
最新加入的杀手级特性，它深度解析 XML 中的 `<MissingIndexes>` 节点，构建了一个虚拟的索引调优沙盒：
*   **沙盒模拟 (Sandbox)**：在不动生产库的情况下，分析补齐索引能带来的预估性能提升（Impact）。
*   **索引评分 (Index Scoring)**：结合表大小、读写比例等维度进行智能打分，防止“伪缺失索引”带来的写惩罚。

---

## 3. 可视化渲染引擎 (UI 层)

执行计划是一个复杂的树状图，WPF 的内建 `TreeView` 无法满足横向展开和算子间带权连线的需求。

### 3.1 基于 Nodify 的新一代渲染架构 (PlanGraphControl)
这是本项目的 UI 渲染核心，底层强力驱动引擎从手绘 Canvas 升级为了工业级节点库 **Nodify**。

*   **PlanNodeViewModel**：每一个解析出的算子都被封装为 ViewModel。其中计算了极其丰富的显示属性（如 `NodeSeverityColor`, `PartitionRangeColor` 等）。
*   **坐标系与递归布局 (`MeasureNode`)**：
    *   采用 **后序遍历 (Post-order Traversal)** 算法，自底向上计算每一个子节点的宽高和相对位置（Bottom-Up）。
    *   完美解决了节点折叠/展开动态触发布局变更时的重叠与碰撞问题。
*   **事件隔离防穿透 (Hit-Testing Fix)**：针对 Nodify 画布会全局拦截 `MouseDown` 事件用于平移的特性，在节点的 `[+]`/`[-]` 按钮处创新性地使用了隧道事件 (`PreviewMouseLeftButtonDown`) 配合 `e.Handled = true` 强行阻断冒泡，实现了完美的精确点击伸缩体验。
*   **智能折叠 (Smart Collapse)**：
    *   算法在生成计划图时，会自底向上扫描所有算子的 `SubtreeCost` 和是否存在 `Warning/Critical`。
    *   当发现某分支既不包含任何性能告警，且其累计成本低于整个查询总成本的 5% 时，工具会将其自动收起，极大减少了 DBA 的视觉噪音。

### 3.2 死锁可视化与回放系统 (Deadlock Visualization)
除了查询执行计划，处理 `.xdl` 死锁图谱是另一大杀手锏。
*   **`DeadlockTimelineParser` (时序反编译器)**：内部执行有向图深度优先搜索算法（DFS），计算并找出成环（Cycle）的致命阻塞链路。
*   **`DeadlockPlaybackViewModel` (动态帧渲染架构)**：支持“死锁可视化回放”模式！引擎像播放录像带一样与 `DeadlockGraphCanvas` 高频联动，通过调节帧率（SpeedIntervalMs），在界面上逐个还原资源的 Request (请求边) 与 Grant (持有边) 历史事件。

## 4. 关键交互流程总结
1.  **加载文件** -> `XDocument.Load` 解析 XML。
2.  **提取树状数据** -> 递归构建 `PlanNodeViewModel` 树层级。
3.  **运行规则引擎** -> 诊断各种反模式，给 `PlanNodeViewModel` 标记红绿灯状态，并生成报告文本。
4.  **UI 测量与排版** -> 触发 Nodify 的坐标计算与连线路由逻辑。
5.  **渲染呈现** -> 通过 `ItemsSource` 呈现节点集合，允许用户自由缩放漫游，并利用隧道事件精准响应用户的折叠树图操作。
