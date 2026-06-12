# SqlXmlAnalyzer 系统架构指南 🏛️

本文档深入解析 SqlXmlAnalyzer 的内部设计与代码架构。软件采用高度解耦的 **模块化多层架构**，确保在不断扩充高级诊断规则或重构可视化渲染层时，互相不产生破坏。

## 1. 宏观架构视图

整个系统拆分为三个主要项目（Project）：

1.  **SqlXmlAnalyzer.Core** 🧠
    *   无 UI 依赖的核心解析与诊断模块 (Class Library)。
    *   负责 XML 文件的解析、执行计划结构的提取、以及最核心的 `RuleEngine` (规则引擎)。
2.  **SqlXmlAnalyzer** 🖥️
    *   基于 WPF (Windows Presentation Foundation) 的桌面 UI 应用。
    *   利用 MVVM (部分) 和 Code-Behind 实现复杂树状图的动态渲染、自适应排版及事件交互。
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
3.  将规则产生的 `AnalysisResult` 按照预设的分类（如 "隐式转换", "回表查询", "经典 SQL 反模式深潜"）收集到字典中。
4.  将结果集合回传给 UI 层用于在侧边栏渲染出 **执行计划深度诊断报告**。

---

## 3. 可视化渲染引擎 (UI 层)

执行计划是一个复杂的树状图，WPF 的内建 `TreeView` 无法满足横向展开和算子间带权连线的需求。

### 3.1 PlanGraphControl (自定义控件)
这是本项目的 UI 渲染核心，位于 `PlanGraphControl.xaml` 及后置代码。

*   **PlanNodeViewModel**：每一个解析出的算子都被封装为 ViewModel。其中计算了极其丰富的显示属性：例如 `NodeSeverityColor` 会依据诊断结果决定节点边框是否标红；`PartitionRangeColor` 会在发生全分区扫描时将文字渲染为 `#FF0000`。
*   **坐标系与递归布局 (`MeasureNode`)**：
    *   采用 **后序遍历 (Post-order Traversal)** 算法，自底向上计算每一个子节点的宽高和相对位置（Bottom-Up）。
    *   确保无论是简单的链式计划，还是庞大的多路 Hash Join 计划，节点彼此之间都不会发生碰撞重叠。
*   **动态连线绘制**：利用 WPF 的 `<Path>` 元素和贝塞尔曲线 (Bezier Curve) 或正交折线，连接父子节点的中心点。连线的粗细 (`StrokeThickness`) 由两个节点间流动的 `ActualRows` / `EstimateRows` 的相对大小动态计算得出。

### 3.2 响应式视图 (MainWindow / PlanView)
*   界面采用了双入口设计：可以直接把 `.sqlplan` 扔进主窗体，也可以在已打开的多标签页环境 (`DocumentTab`) 中独立运作。
*   侧边栏和上方区域支持 **Expander 自动折叠**，最大化图形画布视野。

## 4. 关键交互流程总结
1.  **加载文件** -> `XDocument.Load` 解析 XML。
2.  **提取树状数据** -> 递归构建 `PlanNodeViewModel` 树层级。
3.  **运行规则引擎** -> 诊断各种反模式，给 `PlanNodeViewModel` 标记红绿灯状态，并生成报告文本。
4.  **UI 测量与排版** -> 计算节点 X/Y 坐标，生成连线。
5.  **渲染呈现** -> `Canvas.Children.Add()` 并挂载到 `ScrollViewer` 允许用户自由缩放漫游。
