# IMP-01：基线冻结与模块边界

状态：**已完成**。实施日期：2026-09-08（Asia/Taipei）。本步骤只增加基线工具和证据文档，未修复 R01–R22，未修改应用源码、规则配置或现有测试。

## 1. 冻结结果与证据入口

本次基线编号：`20260908T074458598Z_c5b1e0766cfc_c3523269`。采集窗口为本地时间 **2026-09-08 15:44:58–15:45:24（UTC+08:00）**。机器可读摘要见 [summary.json](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/summary.json)。

这里的“冻结”指保存 HEAD、暂存区、工作树、未跟踪输入和字节指纹，形成后续对照基准；未执行 commit、tag、reset、stash 或文件回退，不锁定用户后续编辑。

| 项目 | 本次实际记录 |
| --- | --- |
| 仓库 / 分支 | `E:/SqlXmlAnalyzer` / `master` |
| HEAD | `c5b1e0766cfcdf28f0d3a27d3238f5f0d6ae1a15` |
| 跟踪文件 | 543 个在范围内的文件，包含文档、代码、配置、夹具和资源 |
| C# / XAML / 项目源码清单 | 415 个已跟踪文件，加已有未跟踪 `MainWindow.KeyboardShortcuts.cs`，共 416 个 |
| 指纹范围 | 555 个输入：543 个已跟踪、12 个未跟踪；后者包含 10 个既有 DOCS 文件、1 个快捷键源码及本次基线脚本 |
| 配置与样例 | 15 个配置/项目/工作流文件；6 个独立样例（5 个 sqlplan、1 个 xdl） |
| 构建 | Debug，显式禁用增量编译，退出码 0，0 警告、0 错误 |
| 测试 | 807 总数 / 807 执行 / 807 通过，0 失败，0 未执行 |
| 源输入完整性 | 构建测试前后 0 内容漂移、0 路径漂移；HEAD 与 Git index 均未变化 |

源码枚举从 `git ls-files` 开始，未递归列举根目录。采集脚本显式排除 `.git`、`bin`、`obj`、`.vs`、`publish`、`publish-*`、`backups`、`.tmp.*`，并将 `DOCS/baselines/IMP-01/` 历史输出从下一次输入采集中排除，避免把证据再次嵌套复制。原始 Git 清单仍单独保留；图标等已跟踪资源只记录文件信息和 hash。

| 交付物 | 用途 |
| --- | --- |
| [源码清单](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/source-inventory.txt)、[Git 清单](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/git-ls-files.txt) | 确定构建输入与仓库跟踪范围 |
| [全部输入指纹](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/input-manifest.json) | 文件路径、是否跟踪、是否存在、字节数及 SHA-256 |
| [采集前状态](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/git-status-before.txt)、[Git index](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/git-index.txt) | 保存基线时的修改集合及暂存区 blob 身份 |
| [工作树 patch](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/working-tree.patch)、[暂存区 patch](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/index.patch) | 分开保存未暂存/已暂存差异；本次暂存区 patch 为空 |
| [未跟踪输入清单](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/untracked-inputs.txt) | 避免只靠 HEAD 或 git diff 漏掉快捷键与评审文件 |
| [配置清单](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/configuration-manifest.json)、[样例清单](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/fixture-manifest.json) | 固定规则、项目、依赖和样例输入 |
| [命令记录](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/commands.json)、[环境](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/environment.json) | 精确参数、执行时间、退出码、stdout/stderr 文件和环境 |
| [证据文件校验清单](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/artifact-manifest.json) | 校验保存的日志、TRX、patch、输入快照；清单自身不自引用 |
| [复用说明](./baselines/README.md)、[采集脚本](./baselines/Capture-ImplementationBaseline.ps1) | 后续以新编号重复采集，保留本次证据 |

`snapshots/` 保存未跟踪文件、已改跟踪文件、配置及独立夹具的原始字节；其路径为 `snapshots/<原相对路径>.snapshot`。增加后缀是为了避免 WPF 根项目将归档 C# / XAML 再次当作编译输入。其他未改跟踪文件由 HEAD 与指纹定位，不复制整个仓库。

## 2. SDK、配置、样例与分析器版本

### 2.1 环境与版本

| 项目 | 观测结果与依据 |
| --- | --- |
| 操作系统 / 进程 | `Microsoft Windows 10.0.26200` / X64；不据此推断系统市场名称 |
| PowerShell | 7.6.5；复用脚本要求 PowerShell 7 或以上 |
| 选用 SDK | 10.0.400；完整信息见 [dotnet --info](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/sdk-info.stdout.txt) |
| SDK / Runtime 清单 | [已安装 SDK](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/sdk-list.stdout.txt)、[已安装 Runtime](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/runtime-list.stdout.txt) |
| 目标框架 | WPF 与测试为 net8.0-windows；Core、Analysis、Refactoring、Application、CLI 为 net8.0 |
| 产品声明版本 | `Directory.Build.props` 中 Version=2.0.0；[MSBuild 求值记录](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/evaluated-properties.stdout.txt)确认 Version 和目标框架 |
| UI 版本来源 | `SqlXmlAnalyzer.Core/ProductInfo.cs` 从入口程序集 InformationalVersion 读取并去掉 `+` 元数据，失败再回退到 AssemblyVersion；本次未运行 GUI 验证显示值 |
| 分析/改写身份 | 以 HEAD、未提交输入指纹及实际依赖版本共同定位，不能只用“2.0.0”判断两次分析器是否完全相同 |
| SDK 固定情况 | 仓库根没有 global.json；本次没有新增 SDK 锁定或修改 CI |

本次只进行项目属性求值时，AssemblyVersion 和 InformationalVersion 返回空字符串；这不代表生成程序集没有版本，因此不把空值作为最终程序集版本。分析器的可追溯依据以源码/配置指纹和构建记录为准。

CI 的 [.github/workflows/ci.yml](../.github/workflows/ci.yml) 使用 SDK `8.0.x` 和 Release，本次使用本机 SDK 10.0.400 与 Debug。因此本记录是当前工作树的本地基线，不是 CI 运行结果；也未执行 CI 的在线依赖漏洞检查或产物洁净检查。后续环境迁移需另建相同配置的对照运行，不能把 SDK / 配置差异直接归因于源码回归。

### 2.2 规则与依赖

`RuleEngine.RegisterDefaultRules` 当前有 34 次默认规则注册；GUI 和 CLI 的新改写 DI 各有 9 个 `ISqlRefactorRule` 注册。计数来自注册源代码检查，不等于 34 项规则全部命中，也不是覆盖率。规则版本通过以下文件 hash 固定：

| 定位项 | SHA-256 |
| --- | --- |
| `SqlXmlAnalyzer.Core/Rules/RuleEngine.cs` | `9D32A436695254CB0FDCDD025F6526DD50A590AD92598520C6CCED3B31133DFD` |
| `SqlXmlAnalyzer.Core/PlanDiagnosticAnalyzer.cs` | `3F2B8F15795EC3E2D2B80544880227598DD778A389F365B2C01FF6517F0FAE51` |
| `src/SqlXmlAnalyzer.Refactoring/SqlRefactoringEngine.cs` | `2167AC1C8980DBCB2ADD71DC2D51C4161802F5B162BC80C7473E179965861974` |
| `RuleConfiguration.json` | `13F16FDA311BED91456807A09EE8ED13E612613BA9A6F644086F0030CCB98D4B` |

依赖由 `Directory.Packages.props` 集中声明；本次同时保存了 [实际解析的直接与传递依赖](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/resolved-packages.stdout.txt)。关键实际版本：ScriptDom 180.18.1、XELite 2024.2.5.1、Nodify 6.0.0、QuestPDF 2026.6.0、DocX 5.0.0。新旧改写引擎的源代码均使用 TSql160Parser / Sql160ScriptGenerator；包版本与选用的语法级别分别记录。

`RuleConfiguration.json` 当前显式配置 6 个规则：RULE_004_ESTIMATE_MISMATCH、RULE_003_PARAM_SNIFFING、RULE_017_LARGE_MEMORY_GRANT、RULE_015_LOCAL_VARIABLES、RULE_016_ZERO_ROW_ACTUALS、RULE_035_SARGABLE_INDEX_RECOMMENDATION。它们均启用，其中 RULE_015 的 SeverityOverride 为 Warning，其余为 null。未列出的默认规则不能据此理解为被禁用。

配置路径存在入口差别：`RuleConfigurationPathResolver.Resolve(null)` 使用 AppContext.BaseDirectory；显式路径使用当前工作目录解析；CLI refactor DI 传入相对 `RuleConfiguration.json`。基线工作目录为仓库根，文件快照、执行目录和命令参数均保留；不将根配置存在等同于任意启动方式都读取同一个文件。

### 2.3 样例范围

| 独立夹具 | 冻结用途 |
| --- | --- |
| `SqlXmlAnalyzer.Tests/Resources/plan_critical_mismatch.sqlplan` | 基数差异规则输入 |
| `SqlXmlAnalyzer.Tests/Resources/plan_large_memory_grant.sqlplan` | 内存授予规则输入 |
| `SqlXmlAnalyzer.Tests/TestData/deadlock_bookmark_lookup.xdl` | 死锁进程/资源输入 |
| `SqlXmlAnalyzer.Tests/TestData/plan_clean.sqlplan` | 基础计划输入 |
| `SqlXmlAnalyzer.Tests/TestData/plan_implicit_conversion.sqlplan` | 隐式转换输入 |
| `SqlXmlAnalyzer.Tests/TestData/plan_missing_index.sqlplan` | 缺失索引输入 |

这 6 个文件是本次 Git 范围内的独立夹具，不代表测试仅有 6 个场景：测试源码还有内联 XML / SQL，已通过源码指纹纳入基线。没有在范围内发现独立 XEL 夹具，不代表解析器没有 XEL 能力。现有审查探针及结果维持在原 `DOCS/review-evidence/`，未重新执行，也未更改其 20 组历史结果。补充反例与正确性断言属于 IMP-02。

## 3. 当前模块责任表与实际调用链

以下为本次源代码核对的现状，不把目标架构描述成已经落地。职责角色仅用于后续分工，未指定个人。

| 边界 / 建议职责 | 入口与主要调用链 | 当前负责内容 / 输出 | 后续修改时需要一起核对 |
| --- | --- | --- | --- |
| GUI 装配：WPF 开发 | [App.OnStartup / ConfigureServices](../App.xaml.cs:21) → MainWindow.Services / ShellWiring | 服务注册、主窗口、控件事件绑定 | CLI 有另一套改写 DI；注册变更需核对两处 |
| GUI 文件入口：WPF + Core | [MainWindow.Documents](../MainWindow.Documents.cs:13) → FileOpenUiActionService → [DocumentAnalysisUiActionService.AnalyzeFileAsync](../Services/DocumentAnalysisUiActionService.cs:78) → DocumentOpenService.OpenAsync | 对话框/拖放、分类、会话与异步分派 | 打开、拖放、内存死锁和 XEL 是不同路径；IMP-06/09 |
| 输入解析：Core 开发 | [DocumentOpenService](../SqlXmlAnalyzer.Core/Services/DocumentOpenService.cs) 与 SafeXmlHelper；XEL 走 XelReader | XML 安全解析、文档种类、文件读取结果 | 保留 DTD/外部实体限制；分类与深层解析契约尚有差异 |
| GUI 计划分析：WPF 服务 + Core | PlanDocumentController.AnalyzeAsync → [PlanAnalysisService.Analyze](../Core/Services/PlanAnalysisService.cs:35) → PlanDiagnosticAnalyzer / ExecutionPlanVisualizer | 当前首条 SQL、文本诊断、Mermaid、缺失索引、候选 SQL | 通过 Application 再分析可重复读取；不能假定只存在一次规则执行 |
| 计划规则：Core 开发 | [PlanDiagnosticAnalyzer.AnalyzePlan](../SqlXmlAnalyzer.Core/PlanDiagnosticAnalyzer.cs:19) → RuleEngine.RegisterDefaultRules / AnalyzePlan；PlanGraphNodeBuilderService 也注册规则引擎 | 节点/文档规则及 AnalysisResult | 指标、配置、严重度和 NodeId；IMP-10–14 |
| 通用分析适配：Analysis 开发 | [SqlXmlAnalysisEngine.Analyze](../src/SqlXmlAnalyzer.Analysis/SqlXmlAnalysisEngine.cs:22) → SafeXmlHelper → PlanDiagnosticAnalyzer → SqlPlanAnalysisIssue / AnalysisReport | 将 Core 结果转为抽象分析问题 | 不是 GUI 全部计划分析或 CLI scan 的唯一入口；当前结构化位置会被简化 |
| CLI scan：CLI + Core | [Program.Main](../SqlXmlAnalyzer.CLI/Program.cs:39) → CollectPlanFiles → [ScanPlanFile](../SqlXmlAnalyzer.CLI/Program.cs:292) → SafeXmlHelper / PlanDiagnosticAnalyzer | 批量扫描、成本/扫描阈值、console / JSON / JUnit、退出码 | 此路径直接调用 Core，不经 ApplicationOrchestrator；IMP-06/09/28 |
| CLI refactor：CLI + Application | [HandleRefactorCommand](../SqlXmlAnalyzer.CLI/Program.cs:550) → 独立 DI → ApplicationOrchestrator.Execute | 参数、dry-run、计划辅助、结果与报告选择 | 该路径可以写回 SQL，与 GUI 预览的 dry-run 不同 |
| 新 SQL 改写：Refactoring 开发 + DBA | [SqlRefactoringEngine.Run](../src/SqlXmlAnalyzer.Refactoring/SqlRefactoringEngine.cs) → ScriptDom → 9 个注入规则 → 输出再解析 | 多轮 AST 改写、Context、输出 SQL / 错误 | 语法可解析并不证明语义等价；IMP-04/18/19 |
| 旧 SQL 改写 / 局部快修：Core + Refactoring | SargableIndexRecommendationRule → Core.Refactoring.SqlRefactorEngine；SqlQuickFixService → ScalarSubqueryToJoinRule.TryRewriteSelectedSubquery | SARGable 建议与选定子查询替换 | 两条路径并存，不能只修改新引擎后假定所有建议均受控 |
| 原 SQL 写回：Application 开发 | [ApplicationOrchestrator.Execute](../src/SqlXmlAnalyzer.Application/ApplicationOrchestrator.cs:36) → IFileHandler / PhysicalFileHandler | 读取源文件、可选计划、调用改写、备份、覆盖、reporter | 当前备份失败仍可能写回（R03）；GUI PlanAnalysisService 用临时 SQL + isDryRun=true |
| 死锁分析：Core 开发 | [DeadlockDocumentController](../Core/Services/DeadlockDocumentController.cs:22) → DeadlockAnalysisService → DeadlockGraphBuilder 与 DeadlockTimelineParser | 图、诊断、推演模型 | 图/推演仍有两套分析路径；IMP-13 |
| XEL：Core + WPF | [XelDeadlockUiActionService.AnalyzeXelFileAsync](../Services/XelDeadlockUiActionService.cs:35) → XelReader.ReadDeadlocksAsync → 事件选择 → 内存 XML 分析 | 读取事件列表、取消、选择后的死锁分析 | 事件时间、选择身份和 XML 输入路径；IMP-09/23/26 |
| 脱敏：WPF 服务 + Core | [MainWindow.ReportEvents](../MainWindow.ReportEvents.cs:7) → PlanObfuscationExportUiActionService.Export → PlanObfuscatorService.ObfuscatePlan → 保存 XML | 在文档副本上替换部分字段并输出 sqlplan | 此独立导出功能不等于其他报告自动脱敏；R01 / IMP-05/22 |
| GUI HTML 报告：报告 + WPF | ReportExportUiActionService → [AnalysisReportController](../Core/Services/AnalysisReportController.cs:38) → HtmlReportExportService / HtmlReportWriter → HtmlReportGenerator.SaveReport | 构建计划/死锁 HTML 内容、选择保存和浏览器打开 | 保留 HTML 编码/CSP；事实、范围与脱敏需统一 |
| GUI PDF / Word：报告 + WPF | ReportExportUiActionService → AnalysisReportController → PortableReportExportService → [PdfWordReportService.Export](../Core/Services/PdfWordReportService.cs:29) → ReportExportService | 控件截图、临时图片、QuestPDF / DocX 输出 | 仍使用便携报告内容模型，不是统一 DiagnosticReport；IMP-22 |
| CLI 报告：Application + CLI | ApplicationOrchestrator → ConsoleResultReporter / JsonResultReporter；scan 由 Program 自行生成结果 | 控制台和文件报告，ShowSql 等选项 | 与 GUI 报告和脱敏路径分离，修改时需覆盖每个出口 |
| 自动化验证：测试 + DBA | [SqlXmlAnalyzer.Tests.csproj](../SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj) 引用 6 个产品项目，包含 WPF 测试依赖 | xUnit / FluentAssertions 单元与服务测试 | SQL Server 语义、实际窗口及性能仍需专门证据 |

特别注意：根目录 `Core/Services/` 和 `Core/ViewModels/` 编译进 WPF 主项目；`SqlXmlAnalyzer.Core/` 才是独立 Core 项目。相似目录名与命名空间不是实际程序集边界。项目引用和目标框架以已归档的 `.csproj` 为准。

## 4. 实施前差异及归属记录

归属采用“本轮开始前已存在 / 先前文档工作 / 本轮 IMP-01 新增”区分。未提交文件没有足够信息证明个人作者，本记录不作归责；构建实际包含以下源码变化，而不是只测试裸 HEAD。

| 路径 | 进入 IMP-01 时状态 | 实际差异 | 归属与处理 |
| --- | --- | --- | --- |
| `AGENTS.md` | 已跟踪、未暂存 | 文件末尾增加 4 行空白，含空格行 | 实施前已有；原字节保留 |
| `MainWindow.Services.cs` | 已跟踪、未暂存 | 初始化时新增 `WireKeyboardShortcuts()` | 实施前已有导航/快捷键工作；保留并纳入构建 |
| `Views/ShellNavigationRail.xaml` | 已跟踪、未暂存 | 中文 ToolTip、快捷键提示与 AutomationProperties.Name | 实施前已有导航可访问性工作；不重复实现 |
| `MainWindow.KeyboardShortcuts.cs` | 未跟踪 | 6 组快捷键与 RoutedCommand 绑定 | 实施前已有；保存完整 `.snapshot`，仅 git diff 不足以恢复 |
| `DOCS/README.md`、3 份评审文档、`检索来源与验证记录.md`、`软件改善实施规划.md` | 6 个未跟踪 Markdown 文件 | 先前评审与规划交付 | 本轮前已存在；采集原字节，随后仅更新索引与规划状态 |
| `DOCS/review-evidence/` 下 4 个文件 | 未跟踪 | 探针源文本、脚本、基线 JSON、探针结果 JSON | 历史附件；不更名、不覆盖、不重写 |
| `DOCS/baselines/Capture-ImplementationBaseline.ps1` | 采集时为未跟踪 | 本轮建立的可重复采集工具 | IMP-01 新增；计入 12 个未跟踪输入，不归入先前改动 |

本轮最终交付还新增本文、基线说明、版本化采集目录及验收记录，更新 `DOCS/README.md` 与规划中 IMP-01 的状态。它们发生在采集窗口之后，因此不会倒填到已冻结的输入清单；原文档快照仍可用于核对前后变化。

已改源码指纹：MainWindow.Services=`307EBEAD645D99AE459CE00242491D9E4892A6A37002B3D38D2DE14128550004`；ShellNavigationRail=`CACF36B9B54436C69A732D68A048ECCD685C11ABE279E14D47A66DE5A20C795A`；KeyboardShortcuts=`D3C6F1AB69F9A1138F9A45812DA594AB55F6333CC1D1209D1512680ABEBDA345`。完整值也在输入清单中，后续应按路径自动比较。

## 5. 完整构建、测试与已有失败登记

本次依次执行以下命令；测试结果目录使用上述独立基线编号，未沿用先前临时 TRX。

```powershell
dotnet restore SqlXmlAnalyzer.sln --nologo
dotnet build SqlXmlAnalyzer.sln --no-restore --no-incremental --configuration Debug --nologo
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj --no-build --no-restore --configuration Debug --nologo --logger 'trx;LogFileName=baseline.trx' --results-directory '<本次基线目录>/test-output'
```

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 依赖还原 | 退出码 0 | [restore stdout](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/restore.stdout.txt) |
| 全解决方案非增量构建 | 7 个项目，0 警告、0 错误，日志耗时 11.94 秒 | [build stdout](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/build.stdout.txt) |
| 全测试项目 | 807 通过、0 失败、0 跳过 | [test stdout](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/test.stdout.txt)、[原始 TRX](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/test-output/baseline.trx) |
| 非通过测试列表 | 空数组 | [test-nonpassed.json](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/test-nonpassed.json) |
| 构建测试期间输入漂移 | 内容与路径变化均为空 | [内容漂移](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/input-drift.json)、[路径漂移](./baselines/IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/input-path-drift.json) |

所有命令的 stderr 与退出码也已保存，空 stderr 是明确的空文件。没有已有构建失败或 xUnit 非通过项需要豁免；后续在相同输入、SDK、依赖与配置下出现的失败需要单独调查，不能笼统归入历史问题。

**领域缺陷台账仍保留 22 项：5 项 P1、17 项 P2。** 它们来源于[代码 Review 报告](./代码Review报告.md)，不因现有 807 项测试通过而关闭。此处登记的是“已知评审问题仍待处理”，没有把它们伪造成现有 xUnit 的失败用例。

本步骤未运行 SQL Server、未做实际 WPF 窗口/DPI/键盘操作、未做性能基准、未生成覆盖率报告、未发布软件；TRX 中的 WPF 相关服务测试不等于 GUI 人工验收。

## 6. 验收结论与后续使用

IMP-01 的三项交付已具备：可重复采集脚本及版本化基线、按实际调用链整理的模块责任表、区分历史工作与本轮新增的差异清单。构建与测试日志、TRX、输入/证据 hash 均保存在 DOCS；独立完整性复核见[本次验收记录](./baselines/IMP-01-20260908T074458598Z-验收记录.json)。

后续每次修复先引用本基线编号，新增运行使用新目录；比较源码与配置变化后，再按测试名称与业务预期核对结果。不能只比较测试总数，也不能只比较 HEAD。需要复现历史状态时，在独立检出目录依据 HEAD、两类 patch 与 `.snapshot` 恢复并检查指纹，不在用户当前工作树直接覆盖。

**IMP-02 及其他步骤仍为待开始。** 下一步是把评审反例转换成修复后的业务验收断言；本次没有提前新增或修改这些测试。
