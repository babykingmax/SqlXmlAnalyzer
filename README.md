# SqlXmlAnalyzer 🚀

SqlXmlAnalyzer 是一款专为 SQL Server 资深 DBA 和高阶开发者打造的桌面级辅助诊断工具。它可以深度解析 SQL Server 导出的死锁图 (`.xdl`) 和执行计划 (`.sqlplan`) 文件，结合行业专家经验库，提供可视化的高级性能诊断、死锁根因剖析以及自动化的 SQL 优化建议。

---

## ✨ 核心特性 (Features)

### 1. 深度死锁分析 (Deadlock Analysis)
针对复杂的 XML 死锁日志进行智能降噪与模式匹配，帮你在一秒钟内找准牺牲品和“真凶”。
* **模式识别诊断**：内置 `DeadlockPatternAnalyzer`，自动识别经典死锁场景（如：书签查找回表死锁 Bookmark Lookup、反向更新死锁 Reverse Order Update、范围扫描死锁 Range Scan 等）。
* **SARG 级锁源提取**：搭载了采用 `.NET 8 [GeneratedRegex]` 高性能编译的 `SargAnalyzer`。它可以智能清理凌乱的 SQL 语句，提取 WHERE 条件、JOIN 条件，并输出引发死锁的确切表名和索引名。
* **Mermaid 流程图生成**：一键生成结构清晰的死锁进程树形图，包含死锁节点优先级（Deadlock Priority）展示，Victim 牺牲品高亮。

### 2. 执行计划可视化与诊断 (Execution Plan Visualizer & Diagnostics)
媲美商业软件（如 SQL Sentry Plan Explorer）的节点图可视化体验与智能巡检。
* **专业级可视化节点图**：基于 `Nodify` 构建的可交互图形界面（`PlanGraphControl`），展示操作符节点、CPU/IO 估算成本、并行标记、以及自适应的连接数据流向线。
* **17 项专家启发式巡检 (`PlanDiagnosticAnalyzer`)**：
  * **缺失索引自动推导**：针对高优先级 Missing Index 生成精确的覆盖索引 DDL，并附带 DBA 生产环境建库防坑提示（如 INCLUDE 字段 1023 字节上限限制）。
  * **残差 I/O 自动检测与警告**：独创高级算法，智能甄别由于**残差谓词 (Residual Predicate)** 带来的无用大量存储层读取，计算“实际读取行数/实际返回行数”爆炸比率，以最醒目的 ⚠️ 警告展现底层 I/O 灾难。
  * **回表代价诊断**：识别 Key/RID Lookup 滥用，提醒审查 `SELECT *` 和覆盖索引设计。
  * **隐藏毒药揪出**：全自动扫描 隐式类型转换 (`CONVERT_IMPLICIT`)、内存溢出落盘 (Spills)、线程数据倾斜 (Thread Data Skew)、内存分配不合理等计划硬伤。

### 3. 一键 HTML 诊断报告 (HTML Report Generation)
分析完毕后，可通过 `HtmlReportGenerator` 输出格式精美的脱机 HTML 网页报告。无论是用于团队内部分享、故障复盘 (Post-mortem)，还是邮件汇报，都能完美适配。

---

## 🛠 技术栈 (Tech Stack)

* **语言/运行时**: C# 12 / .NET 8.0
* **桌面框架**: WPF (Windows Presentation Foundation)
* **核心依赖库**: 
  * [Nodify](https://github.com/miroiu/nodify) - 强大的 WPF 节点编辑器控件，用于渲染执行计划物理树和数据流管道。
  * LINQ to XML (`XDocument`) - 高性能处理海量 `.sqlplan` 和 `.xdl` XML 文件流。
* **架构设计**: 视图模型 (MVVM 模式变体)，核心诊断逻辑全面静态化解耦。

---

## 📦 快速开始 (Getting Started)

### 编译环境要求
* Windows 10/11
* Visual Studio 2022 (v17.8+) 或最新版 Rider
* .NET 8.0 SDK

### 构建运行
1. 克隆代码仓库。
2. 使用 VS 打开 `SqlXmlAnalyzer.sln`，或在根目录执行命令：
   ```bash
   dotnet build SqlXmlAnalyzer.sln
   ```
3. F5 运行项目，即可看到主界面 `MainWindow.xaml`。
4. 拖入项目根目录附带的测试样本 `DEMO.sqlplan` 或 `LOCK.XDL`，体验分析效果。

---

## 📁 核心项目结构 (Project Structure)

```text
SqlXmlAnalyzer/
├── MainWindow.xaml / .cs           # 主窗体：负责文件加载、调度分析引擎和左侧属性板
├── PlanGraphControl.xaml / .cs     # 执行计划可视化核心控件：负责 Nodify 节点渲染与交互
├── PlanDiagnosticAnalyzer.cs       # 核心类：执行计划 17 项全量启发式巡检引擎
├── ExecutionPlanVisualizer.cs      # 执行计划 HTML/文本报告生成模块
├── DeadlockGraph.cs                # 核心类：死锁图解析引擎与 SargAnalyzer 锁源提取
├── HtmlReportGenerator.cs          # 核心类：前端 HTML 报告生成模版引擎
├── Logger.cs                       # 日志系统
└── ssms_icons/                     # 执行计划 SSMS 操作符经典图标库
```

---

## 🤝 贡献与反馈 (Contributing)

欢迎数据库爱好者、DBA 和 C# 开发者提交 Issue 和 PR！如果你发现某些特殊的、未被解析出来的死锁场景或奇怪的执行计划 XML 模式，请附上脱敏后的 `.xdl` 或 `.sqlplan` 提报 Issue，我们将不断丰富诊断经验库。

## 📄 开源协议 (License)

本项目采用 MIT License 开源，详见 [LICENSE](LICENSE) 文件。
