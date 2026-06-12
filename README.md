# SqlXmlAnalyzer 🚀

SqlXmlAnalyzer 是一款极其强大且现代化的 **SQL Server 性能诊断与执行计划分析工具**。
它的目标是提供媲美甚至超越商业软件（如 SQL Sentry Plan Explorer）的分析能力，不仅能够优雅地可视化庞大复杂的执行计划（`.sqlplan`）和死锁图（`.xdl`），更内置了一套专业的 **30 项规则诊断引擎 (Rule Engine)**，为您提供 DBA 级别的深度中文诊断报告！

## 🌟 核心特性 (Features)

*   **📊 完美的执行计划可视化**
    *   **SSMS 原生图标支持**：无缝还原 SSMS 的经典图标体系，零学习成本。
    *   **智能连线与流向标记**：粗细动态变化的连线直观反映数据流大小（Data Flow Size），迅速定位瓶颈。
    *   **自适应 UI 布局**：主窗口支持侧边栏和顶部区域的动态折叠，最大化画布区域，并支持无级缩放和平移。
    *   **一键转 Mermaid**：支持将庞大的执行计划树瞬间转化为 Mermaid 文本，方便在 Markdown 或浏览器中快速预览和分享。

*   **🕵️‍♂️ 专家级深度诊断规则引擎 (Rule Engine)**
    *   内部挂载了基于开源 Performance Studio 标准构建的 **P0/P1/P2** 核心规则组。
    *   包括但不限于：隐式转换警告 (Implicit Conversion)、高昂回表查询 (Key/RID Lookup)、严重 TempDB 溢出 (Spill)、参数嗅探 (Parameter Sniffing)、表变量/UDF 性能黑洞估算错误、嵌套循环暴增、非 SARGable 反模式（如前导通配符）、线程倾斜 (Parallel Skew) 以及串行计划原因剖析等。
    *   **全中文 DBA 建议**：摒弃生涩的机器翻译，每一条规则命中后均提供详细的中文场景解释及调优处方（如：强烈建议改为 #临时表，或添加覆盖索引）。

*   **🚨 智能色彩高亮预警**
    *   **成本红色预警**：开销占比极高的算子节点，其边框自动加粗并飙红。
    *   **全分区扫描报警**：当检测到查询访问了分区表，但未发生分区裁剪（即 `Partition Range` 跨越了全部分区）时，自动使用亮红色 (`#FF0000`) 醒目标注！

*   **💀 深度死锁分析 (Deadlock Analyzer)**
    *   一键解析 `.xdl` 死锁文件。
    *   将晦涩难懂的死锁 XML 转化为清晰的进程/资源冲突矩阵，直指被 `Kill` 的 Victim 进程及其持有的锁。

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

*   `SqlXmlAnalyzer` (WPF UI 主工程)：包含所有 XAML 视图层、图形画布 (`PlanGraphControl`)，以及动态数据绑定。
*   `SqlXmlAnalyzer.Core` (核心逻辑库)：完全解耦的分析引擎。包含 `RuleEngine` 架构和所有实现了 `IPlanAnalyzerRule` 的诊断规则，以及底层 XML Parser。
*   `SqlXmlAnalyzer.Tests` (单元测试工程)：提供 100% 覆盖率的规则引擎测试网，保障任何诊断规则升级都不会发生退化。

## 🛠️ 参与贡献 (Contributing)

如果您发现有未被识别的特殊 SQL Anti-Pattern，欢迎在 `SqlXmlAnalyzer.Core/Rules` 目录下继承 `IPlanAnalyzerRule` 接口增加属于您的自定义规则！

## 📜 许可证 (License)

本项目采用 MIT License 开源，您可以自由地使用、修改及分发。
