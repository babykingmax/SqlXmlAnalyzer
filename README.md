# SqlXmlAnalyzer 🚀

SqlXmlAnalyzer 是一款极其强大且现代化的 **SQL Server 性能诊断与执行计划分析工具**。
它的目标是提供媲美甚至超越商业软件（如 SQL Sentry Plan Explorer）的分析能力，不仅能够优雅地可视化庞大复杂的执行计划（`.sqlplan`）和死锁图（`.xdl`），更内置了一套专业的 **30 项规则诊断引擎 (Rule Engine)**，为您提供 DBA 级别的深度中文诊断报告！

## 🌟 核心特性 (Features)

*   **📊 完美的执行计划可视化 (基于 Nodify)**
    *   **全新视觉引擎**：采用工业级的 Nodify 节点图表库，支持千万级算子的超大画板无限缩放、平移，告别原生 TreeView 的局限。
    *   **SSMS 原生图标支持**：无缝还原 SSMS 的经典图标体系，零学习成本。
    *   **智能折叠 (Smart Collapse)**：自动识别并折叠成本极低（<5%）且无告警的次要执行分支，一眼聚焦核心性能瓶颈，极大降低视觉噪音！
    *   **智能连线与流向标记**：粗细动态变化的连线直观反映数据流大小（Data Flow Size），迅速定位瓶颈。

*   **🕵️‍♂️ 专家级深度诊断规则引擎 (Rule Engine)**
    *   内部挂载了基于开源 Performance Studio 标准构建的 **P0/P1/P2** 核心规则组。
    *   包括但不限于：隐式转换警告 (Implicit Conversion)、高昂回表查询 (Key/RID Lookup)、严重 TempDB 溢出 (Spill)、参数嗅探 (Parameter Sniffing) 等。
    *   **全中文 DBA 建议**：每一条规则命中后均提供详细的中文场景解释及调优处方。
    *   **现代索引调优沙盒**：支持三栏式交互列配置，支持拖拽和增删 Key/Include 字段，可自动生成 CREATE INDEX 脚本并内置 **回表临界值分析器 (Tipping Point Analyzer)**。结合读写比进行多彩圆环智能打分，精准避开“写惩罚”与“伪缺失索引”！
    *   **参数嗅探对比面板**：以并排卡片形式对比编译期参数（Compiled Parameter）和运行期参数（Runtime Parameter），配备卡片偏离度警报，更支持粘贴 `DBCC SHOW_STATISTICS` 真实直方图并自动生成概率分布曲线。

*   **🚨 智能色彩高亮预警**
    *   **成本红色预警**：开销占比极高的算子节点，其边框自动加粗并飙红。
    *   **全分区扫描报警**：当检测到跨越了全部表分区的范围扫描时，自动使用亮红色 (`#FF0000`) 醒目标注！

*   **💀 深度死锁分析 (Deadlock Analyzer)**
    - **✨ 新特性：水平步进时间轴死锁回放（Timeline Playback）**：支持加载 `.xdl` 文件后，通过横向步进器气泡指示器（以不同颜色标记已执行/当前帧/未执行/受害者 💀 阶段）逐帧回放死锁过程，清晰展示进程锁的请求、持有顺序。
    - **聚焦关键路径**：在回放中可勾选一键过滤无关进程，仅保留导致死锁的核心环（Cycle）。

*   **🖥️ 精致现代无边框 UI (Modern UI & Custom TitleBar)**
    - 支持双色主题切换（浅色/深色），并配有淡雅过渡动画。
    - 自定义无边框窗口布局，在最大化时内置 **Win32 消息钩子 (WM_GETMINMAXINFO)**，自动适应各显示器的桌面工作区，最大化绝不遮挡系统任务栏。

## 🏗️ 快速开始 (Getting Started)

### 系统要求
*   操作系统: Windows 10/11
*   运行时环境: **.NET 8.0 Desktop Runtime**

### 编译与运行
1.  克隆本仓库到本地。
2.  进入根目录，打开命令行：
    ```bash
    dotnet build SqlXmlAnalyzer.sln
    dotnet run --project SqlXmlAnalyzer
    ```
3.  点击界面上的 **"打开 SQL 执行计划或死锁文件..."** 按钮，或者直接将 `.sqlplan` / `.xdl` 文件拖入窗口。

## 📁 核心项目结构 (Project Structure)

*   `SqlXmlAnalyzer` (WPF UI 主工程)：包含所有 XAML 视图层、图形画布 (`PlanGraphControl` 基于 Nodify)。
*   `SqlXmlAnalyzer.Core` (核心逻辑库)：完全解耦的分析引擎。包含 `RuleEngine` 架构和底层 XML 解析器。
*   `SqlXmlAnalyzer.Tests` (单元测试工程)：提供 100% 覆盖率的规则引擎测试网，保障诊断规则升级的健壮性。

## 🛠️ 参与贡献 (Contributing)

如果您发现有未被识别的特殊 SQL Anti-Pattern，欢迎在 `SqlXmlAnalyzer.Core/Rules` 目录下继承 `IPlanAnalyzerRule` 接口增加属于您的自定义规则！

## 🧑‍💻 作者 (Author)

* **姓名**: 胡冰
* **Email**: [BABYKINGMAX@HOTMAIL.COM](mailto:BABYKINGMAX@HOTMAIL.COM)

## 📜 许可证 (License)

本项目采用 MIT License 开源，您可以自由地使用、修改及分发。
