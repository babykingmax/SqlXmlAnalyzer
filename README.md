# SqlXmlAnalyzer 🚀

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows%20WPF-lightgrey)
![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)

**SqlXmlAnalyzer** 是一款面向 SQL Server 数据库管理员 (DBA) 与性能优化专家的**开源图形化诊断神器**。它拥有媲美原生 SQL Sentry Plan Explorer 的极简操作节点风格，并内置了基于规则引擎 (Rule Engine) 的智能诊断机制。不仅能让你瞬间看懂天书般的 `.sqlplan` 执行计划，还能自动提取 `.xdl` 找准死锁牺牲者。

---

## 🌟 为什么选择 SqlXmlAnalyzer？

传统的 SSMS 执行计划阅读体验枯燥、信息层级混乱。SqlXmlAnalyzer 提供了：
- 🎨 **专家级图形化渲染**: 采用 `Nodify` 节点拓扑图，平移缩放丝滑，直观展示算子成本占比。
- 🚨 **智能红绿灯告警系统**: 自动标红 `Critical` (如：由于隐式类型转换导致的宽表全表扫描)，标橙 `Warning` (如：参数嗅探、键查找异常)。
- 💀 **死锁回滚成本透视**: 不仅仅画出死锁依赖图 (Mermaid.js)，更独创性地抓取底层 `logused` 数据，精准还原 SQL Server 选择死锁牺牲者 (Victim) 的底层逻辑。
- 🤖 **批量无头自动扫描 (CLI Mode)**: 支持一次性吞吐上千个计划文件，在控制台秒级输出安全健康报告，完美集成至 CI/CD。

---

## 📸 工具截图展示

*(请在本地 `assets` 文件夹中放入对应的截图)*

### 1. 执行计划可视化与告警红框
> 直观看到 隐式类型转换 与 高成本算子 的智能报警。
![执行计划图形化界面](assets/plan_graph_demo.png)

### 2. 详细算子分析面板
> 鼠标悬停展示分区数据、内存请求、线程倾斜、残差谓词等隐藏杀手。
![执行计划Hover详情](assets/hover_tooltip_demo.png)

### 3. 死锁有向图与回滚成本 (LogUsed)
> 清晰地看出进程间的资源抢占与各自的日志回滚代价。
![死锁依赖分析图](assets/deadlock_graph_demo.png)

---

## 📖 使用指南

### 桌面图形端 (GUI)
1. 下载并运行 `SqlXmlAnalyzer.exe`。
2. 依次点击菜单栏：`文件` -> `打开`，或者**直接将文件拖拽进窗口**。
3. 支持解析的文件类型：
   - `.sqlplan` (SQL Server 执行计划 XML)
   - `.xdl` (SQL Server 死锁跟踪图 XML)
4. **快捷交互**：
   - 按住鼠标**左键拖动**画布，**滚轮**缩放。
   - **悬停**节点查看详细警告与参数。
   - **双击**节点可以查看原始 XML 切片代码。
5. 底部面板会直接汇总整个查询的**缺失索引建议 (Missing Indexes)** 以及**基数估计误差 (Cardinality Error)**。

### 高级批处理自动化 (CLI)
如果您是高级架构师，可以在终端中以无头模式运行分析：
```powershell
# 分析单个计划文件并在控制台输出报告
.\SqlXmlAnalyzer.exe --analyze "C:\DB_Dumps\slow_query.sqlplan"

# 【强烈推荐】批量扫描目录下的所有 .sqlplan 和 .xdl 文件！
.\SqlXmlAnalyzer.exe --batch "D:\SQL_Performance_Dumps"
```

---

## ❓ 常见问题解答 (FAQ)

### **Q1: 为什么有的节点外框是红色的，有的是橙色的？**
**A1:** 系统内置的**分析规则引擎 (RuleEngine)** 会对所有算子进行评估。
- 🔴 **红色 (Critical)**: 代表极为严重的阻碍性能反模式（如发现 `CONVERT_IMPLICIT` 隐式转换导致索引扫描、内存溢出落盘 `Memory Spill`）。
- 🟠 **橙色 (Warning)**: 潜在的问题（如 `Key Lookup` 回表、参数嗅探 `Parameter Sniffing`，或者存在残差谓词）。
- ⚪ **透明**: 正常健康的算子。

### **Q2: 导入死锁 (`.xdl`) 文件后，图表里的 `LogUsed` 是什么意思？**
**A2:** 当两个事务发生死锁时，SQL Server 必须杀掉其中一个 (Victim)。数据库引擎判断“该杀谁”的核心依据就是回滚成本，而这个成本就记录在隐藏的 `logused` 字段中。本工具会自动提取该字段并标注在死锁进程旁，让你能够一眼看穿 SQL Server 的“杀人逻辑”。

### **Q3: 遇到解析失败或者闪退怎么办？**
**A3:** SqlXmlAnalyzer 拥有强大的兜底日志系统。请在 `SqlXmlAnalyzer.exe` 同级目录下寻找 `log\` 文件夹，里面会包含诸如 `SqlXmlAnalyzer_20260612_xxxxx.log` 的日志文件。查看该日志的 `[Critical]` 或 `[Error]` 级别条目通常能知道是哪个 XML 节点格式不兼容导致的。

### **Q4: 它和 SSMS 原生执行计划有什么区别？**
**A4:** SSMS 是原生的基础查看器，而本工具对标的是 **SQL Sentry Plan Explorer** 等商业化软件。我们在 UI 层面去掉了冗余信息，突出了实际/预估成本；在逻辑层面，把经验老道的 DBA 脑海里的排错经验写成了代码，实现了“自动化诊断”。

---

## 🛠️ 构建与开发

本工具基于 .NET 8 WPF 编写。

1. 克隆代码库并用 Visual Studio 2022 打开 `SqlXmlAnalyzer.sln`。
2. 架构分为：
   - `SqlXmlAnalyzer` (WPF UI, Nodify, CefSharp)
   - `SqlXmlAnalyzer.Core` (规则引擎, 解析器, 纯逻辑层)
3. 扩展诊断规则：你只需在 `SqlXmlAnalyzer.Core/Rules/` 中新建类实现 `IPlanAnalyzerRule` 接口，并注册到 `RuleEngine.cs` 即可。详见 `Architecture.md`。

## 📝 许可 (License)
本项目采用 [MIT License](LICENSE) 许可协议。
