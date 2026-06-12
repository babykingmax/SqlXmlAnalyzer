# SqlXmlAnalyzer 软件架构文档

## 1. 系统概述
**SqlXmlAnalyzer** 是一款面向 SQL Server 数据库管理员 (DBA) 和性能优化工程师的开源诊断工具。系统分为**富客户端界面 (WPF)** 和 **核心诊断服务库 (.NET Core)** 两大部分。其主要职责是解析 SQL Server 导出的 XML 执行计划 (`.sqlplan`) 与死锁日志 (`.xdl`)，进行图形化可视化以及自动化的专家诊断。

## 2. 架构设计原则
系统整体遵循 **MVVM (Model-View-ViewModel)** 架构与**高内聚低耦合**的原则，分离了底层分析引擎与上层展示界面。
1. **解耦性**: 所有的 XML 解析、死锁诊断、性能反模式识别规则都封装在 `SqlXmlAnalyzer.Core` 类库中。WPF 层仅负责数据的可视化呈现。
2. **可扩展性**: 采用了访问者模式 (Visitor Pattern) 和管道模型设计的 `RuleEngine`（规则引擎），新增诊断规则时无需触碰主逻辑代码。
3. **双重驱动 (Dual-Drive)**: 支持通过 CLI（命令行参数）以无头模式运行批处理诊断任务，也可以正常启动 WPF GUI 进行可交互式分析。

## 3. 核心模块与物理层级拆分

### 3.1 表现层 (UI / WPF) - `SqlXmlAnalyzer` 项目
采用 WPF (Windows Presentation Foundation) 开发。
- **UI 框架**: 引入了 `Nodify` 库构建复杂的有向无环图 (DAG) 节点系统，用以绘制类似 SQL Sentry Plan Explorer 的交互式执行计划树。
- **视图模型 (ViewModels)**: 
  - `PlanNodeViewModel`: 将解析后的底层 XML 节点数据包装为绑定在 UI 上的属性（如：`NodeSeverityColor`, `CostText` 等）。
  - `ConnectionViewModel`: 描述执行计划节点之间数据流向 (Row counts) 与线段粗细逻辑。
- **Web 视图交互**: 引入了 `CefSharp` (Chromium Embedded Framework) 用于渲染复杂的死锁图表 (基于 Mermaid.js 的 `DeadlockGraph`) 与生动的诊断报告。

### 3.2 核心业务与引擎层 - `SqlXmlAnalyzer.Core` 项目
包含所有不依赖 UI 的分析诊断核心组件，能在控制台程序、CI/CD 管道等任何 `.NET` 环境中复用。
- **`XmlPlanParser.cs`**: 负责将 SQL Server 的 Showplan XML 递归降维为 `RelOpNode` 树。
- **`PlanDiagnosticAnalyzer.cs`**: 高阶诊断总线，汇总缺失索引、基数估计误差、死锁分析等并生成专家报告。
- **`DeadlockGraph.cs`**: 专门处理 `.xdl` 文件，提取所有事务进程并基于 `LogUsed` 字段测算回滚成本，生成供前端网页渲染的 Mermaid 有向图代码。
- **`Logger.cs`**: 高性能、无依赖的静态本地文件日志系统，确保应用程序即使发生致命崩溃也能保存诊断案发现场。

### 3.3 规则引擎 (Rule Engine Architecture)
位于 `SqlXmlAnalyzer.Core.Rules` 命名空间下。为了解决检测代码的“意大利面条式”耦合，我们设计了可插拔的规则引擎：
- **`IPlanAnalyzerRule`**: 接口层。定义了 `RuleId`, `Severity` 以及 `Analyze()` 契约方法。
- **`AnalysisResult`**: 载体层。携带检查出的 Warning 或 Critical 等级，抛向 UI 并转换为节点边框的红圈/橙圈。
- **`RuleEngine`**: 引擎执行器。在遍历 XML 节点树时，调用已注册的所有分析规则。
- **当前挂载的核心规则**:
  1. `ImplicitConversionRule`: 检测导致索引 Scan 降级的隐式类型转换 (CONVERT_IMPLICIT)。
  2. `KeyLookupRule`: 捕捉影响查询性能的书签查找/键查找。
  3. `ParameterSniffingRule`: 比对编译时与运行时的参数列表差异，揭露参数嗅探问题。

### 3.4 命令行服务 (CLI Service)
位于 `CliService.cs`。它通过拦截 Windows 消息 (`kernel32.dll AttachConsole`)，在 GUI 被拉起前接管进程，实现诸如 `--batch` 多文件扫描功能。

---

## 4. 关键数据流 (Data Flow)

### 4.1 GUI 执行计划渲染流
1. 用户拖拽 `.sqlplan` 文件进入主界面。
2. `XmlPlanParser` 收到指令，解析 XML 获取表象数据。
3. 构建树形结构时，触发 `RuleEngine.AnalyzeNode()`。
4. 规则引擎并行跑完所有探测规则，将异常结果标记（Severity）。
5. `PlanGraphControl.xaml.cs` 接收模型，包装成带有警告色框和数据的 `PlanNodeViewModel`。
6. `Nodify` Canvas 在界面渲染生成拓扑节点。

### 4.2 死锁回滚诊断数据流
1. 用户导入 `.xdl` 文件。
2. 提取 `<process-list>` 并装载进 `DeadlockProcess` 实体。
3. 从 XML 读取 `logused` 字段，作为各个挂起事务的“回滚成本评估”指标。
4. 在构建 Mermaid 图表字符串时，自动判定 `logused` 最小的作为最佳 Victim 牺牲者。
5. 通过 `CefSharp` 注入 Chromium 内核引擎，最终呈现出直观的带有死锁闭环和锁类型 (IX, X, U) 的图形。
