# SqlXmlAnalyzer 代码审查复评与修复计划

日期：2026-06-24  
依据：`CODE_REVIEW_AND_FUNCTIONAL_ANALYSIS_2026-06-24.md`、当前工作区代码、292 个现有测试

## 1. 复评结论

原报告的总体判断成立：项目功能完整、测试基础较好，但诊断可信度、安全边界和 UI 可维护性需要优先治理。建议不要一次性重构全部架构，而是按“诊断正确性 → 安全与资源 → 异步稳定性 → 架构收敛”拆成独立 PR。

需要修正的原报告结论：

- PDF/Word 截图 PNG 在正常导出路径中会删除，但删除不在 `finally`，导出异常时仍可能残留。因此问题保留，严重级别从 P1 降为 P2。
- 死锁 XML 在进入解析器前通常已完成安全 XML 解析；主要风险不是 XML 语法错误，而是关键节点缺失或内部异常时返回空/部分结果。建议按 P2 correctness 问题处理。
- “乱码”需要拆开判断：部分是终端编码显示问题，部分源码和资源字符串确实已是乱码。不能做全仓机械转码，必须按文件逐个修复并做 UI 验证。
- NuGet 过期/漏洞在线检查因当前环境无法连接 NuGet 服务未完成；静态检索未发现 Azure Identity、MSAL、ASN.1 的 C# 使用，应作为待验证清理项。

## 2. 修复优先级

| 批次 | 目标 | 主要问题 | 预计工作量 |
|---|---|---|---|
| PR-1 | 提升诊断可信度 | 参数嗅探误报、健康统计信息 Warning、文档规则重复执行 | 1～2 天 |
| PR-2 | 修复 HTML 输出安全 | HTML/脚本注入、报告 API 接受任意 HTML | 1～2 天 |
| PR-3 | 配置与解析失败显式化 | 配置路径不确定、静默回退、死锁部分成功 | 2 天 |
| PR-4 | 资源与异步稳定性 | 临时文件、取消、旧任务覆盖新结果 | 2～3 天 |
| PR-5 | 规则元数据与作用域 | RuleId 分类硬编码、NodeId 约定 | 3～5 天 |
| PR-6 | 架构和工程治理 | MainWindow 拆分、双重构引擎、依赖和文档 | 分阶段 1～3 周 |

## 3. PR-1：诊断可信度修复

### 3.1 修改范围

- `SqlXmlAnalyzer.Core/Rules/QueryRewriteRule.cs`
- `SqlXmlAnalyzer.Core/Rules/StatsUsageRule.cs`
- `SqlXmlAnalyzer.Core/Rules/ParameterSniffingRule.cs`
- `SqlXmlAnalyzer.Tests/Rules/` 下新增对应测试

### 3.2 实施方案

1. 删除 `QueryRewriteRule` 中“`OptimizerStatsUsage` 存在即视为参数嗅探”的逻辑。
2. 提取公共检测结果，例如 `ParameterSensitivityEvidence`：
   - 编译参数值与运行参数值是否不同；
   - 最大估算/实际行数偏差；
   - 是否存在过期或高修改量统计信息；
   - 是否为实际执行计划。
3. 只有存在参数值差异时才给出参数敏感建议；没有运行时值时不推断参数嗅探。
4. `StatsUsageRule` 只对 `IsStale`、`IsLowSampling` 或修改量超过阈值的统计信息生成 Warning；全部健康时返回 null，或由独立 Info 面板展示。
5. `ParameterSniffingRule` 只在计划级执行一次。短期可严格限定 Node 0；若计划没有 Node 0，则由分析入口传入一个计划上下文节点，不再同时允许 Node 0/1。
6. 不直接建议“使用局部变量”。输出改为候选措施，并注明验证方式：Query Store 对比、`OPTION(RECOMPILE)` A/B 测试、SQL Server 2022 PSP 检查。

### 3.3 测试与验收

- 有 `OptimizerStatsUsage`、无参数差异：不得出现参数嗅探建议。
- 健康统计信息：不得产生 Warning。
- 参数差异 + 100 倍行数偏差：产生 Critical。
- 同一计划同时有 Node 0/1：规则结果只有一条。
- 原有规则测试和完整 292+ 测试全部通过。

## 4. PR-2：HTML 报告安全改造

### 4.1 修改范围

- `SqlXmlAnalyzer.Core/HtmlReportGenerator.cs`
- `MainWindow.xaml.cs` 报告组装部分
- 新增 `SqlXmlAnalyzer.Tests/HtmlReportGeneratorTests.cs`

### 4.2 实施方案

1. 不再让 `GenerateReport` 接收任意 `additionalAnalysis` HTML 字符串。
2. 新增结构化模型：

`HtmlReportSection(Title, IReadOnlyList<HtmlReportItem>)`，`HtmlReportItem(Heading, Description, Cause, Recommendation, Severity)`。

3. 所有文本统一经过 `WebUtility.HtmlEncode`；换行处理顺序固定为“先编码，再替换为 `<br/>`”。
4. 文件路径、标题、严重级别、SQL、对象名均编码。
5. HTML 标签只由生成器模板输出，不允许业务层拼接 `<div>`、`<strong>`。
6. 为报告增加 CSP。若继续使用 Mermaid CDN，至少限制 `script-src` 到固定 CDN；更稳妥方案是导出预渲染 SVG。

### 4.3 测试与验收

输入以下内容时，生成文件不得包含可执行标签：

- `<script>alert(1)</script>`
- `<img src=x onerror=alert(1)>`
- 含 `&`、`<`、`>`、引号的文件名和表名

测试应断言输出包含编码后的 `&lt;script&gt;`，且不存在原始事件属性。

## 5. PR-3：配置和死锁解析契约

### 5.1 配置加载

修改：

- `RuleConfigurationLoader.cs`
- `RuleEngine.cs`
- `SqlXmlAnalysisEngine.cs`
- `App.xaml.cs`
- CLI 配置参数处理

方案：

1. 增加 `RuleConfigurationPathResolver`。
2. WPF 默认使用 `Path.Combine(AppContext.BaseDirectory, "RuleConfiguration.json")`。
3. CLI 显式配置路径先执行 `Path.GetFullPath`；未指定时同样使用应用目录。
4. `Load` 返回 `RuleConfigurationLoadResult`，包含 `Configuration`、`ResolvedPath`、`Warnings`，不再静默吞掉问题。
5. 校验空 RuleId、重复 RuleId、非法 SeverityOverride。
6. 明确策略：默认配置缺失可继续使用内置默认值，但 UI/CLI 必须显示一次告警；显式指定的配置缺失则返回失败。

验收：改变当前工作目录后启动 WPF/CLI，加载的配置路径保持一致。

### 5.2 死锁解析

修改：

- `DeadlockXmlParser.cs`
- `DeadlockTimelineParser.cs`
- 上层死锁分析调用

方案：

1. 引入 `DeadlockParseResult<T>`，包含 `Value`、`Errors`、`Warnings`、`IsSuccess`。
2. 缺少 `process-list`、`resource-list` 或没有有效 process 时返回失败。
3. 缺少可选字段时保留默认值并记录 Warning。
4. 内部异常转为失败结果，不返回半成品图。
5. 上层仅在 `IsSuccess` 时构图；否则显示错误摘要。

兼容策略：先保留旧 tuple API，并标记 `[Obsolete]`，内部调用新 API；调用点迁移完成后删除。

## 6. PR-4：临时文件与异步分析

### 6.1 临时文件

1. XEL 选择不再写临时 XML：增加 `AnalyzeDeadlockXmlAsync(string xml, string displayName)`，直接调用 `SafeXmlHelper.ParseSafe`。
2. PDF/Word 截图删除移动到 `finally`。
3. Mermaid 临时 HTML 使用 GUID 文件名，记录到 `TemporaryFileManager`。
4. `TemporaryFileManager` 只管理前缀为 `SqlXmlAnalyzer_` 的文件；应用退出时删除本会话文件，启动时清理超过 24 小时的历史文件。
5. 临时文件创建权限失败时降级为不自动打开浏览器，并给出保存路径选择。

### 6.2 异步竞争

修改：`MainWindow.xaml.cs`，并新增 `AnalysisSessionCoordinator`。

1. 将 `AnalyzeFile` 改为返回 `Task` 的 `AnalyzeFileAsync`。
2. 每次新分析取消上一个 `CancellationTokenSource`。
3. 为每次请求生成递增 request ID；UI 写入前确认它仍是当前请求。
4. `Task.Run` 内的解析、规则执行和构图定期检查 token。
5. 事件处理器保留 `async void`，但只负责 await `AnalyzeFileAsync` 和统一错误提示。

验收：连续打开“大文件 A → 小文件 B”，最终 UI 必须显示 B，A 的完成结果不得覆盖 B。

## 7. PR-5：规则元数据与作用域

### 7.1 目标模型

新增：

`RuleMetadata(RuleId, Category, Scope, DefaultSeverity, Description)`

其中 `RuleScope` 为 `Plan`、`Statement`、`Operator`。

### 7.2 方案

1. `IPlanAnalyzerRule` 暴露 `Metadata`，逐步替代散落的 RuleId/Name/Description。
2. `RuleEngine`：
   - Plan 规则每个文档执行一次；
   - Statement 规则每个 `StmtSimple` 执行一次；
   - Operator 规则遍历 `RelOp`。
3. 删除通过 NodeId=0/1 判断文档规则的约定。
4. 报告分类直接读取 `Category`，删除 `MapRuleIdToCategory`。
5. 删除 `IsNodeLevelRule` 的数字区间推断。
6. RuleId 暂不改名，避免破坏现有配置；仅新增唯一性测试。下一主版本再统一重复数字编号。

验收：注册规则 ID、配置规则 ID、报告分类均有自动化一致性测试。

## 8. PR-6：架构与工程治理

### 8.1 MainWindow 拆分顺序

不要一次重写 MVVM。按可测试边界逐步抽取：

1. `TemporaryFileManager` 和 `BrowserLauncher`。
2. `HtmlReportExportService`、`PdfWordReportService`。
3. `PlanAnalysisService`、`DeadlockAnalysisService`。
4. `AnalysisSessionCoordinator`。
5. 最后将状态和命令迁移到 `MainViewModel`。

目标：第一阶段将 `MainWindow.xaml.cs` 降至 2,000 行以下，最终控制在 800～1,200 行。

### 8.2 合并重构引擎

1. 将 `src/SqlXmlAnalyzer.Refactoring/SqlRefactoringEngine` 作为唯一公开实现。
2. 统计旧 `SqlXmlAnalyzer.Core.Refactoring.SqlRefactorEngine` 的调用点。
3. 为旧 API 增加适配器和 `[Obsolete]`。
4. 将 `SargableIndexRecommendationRule` 改为依赖 `IRefactoringEngine`，避免内部直接 new 旧引擎。
5. 删除重复的 `ISqlRefactorRule`、`RefactorResult` 和桥接 Context 前，先完成所有调用迁移。

### 8.3 依赖、版本和仓库卫生

1. 静态检索未发现 Azure Identity、MSAL、ASN.1 使用；在独立分支逐项删除后执行 restore/build/test/publish，确认无运行时依赖再合并。
2. 增加 `Directory.Packages.props` 集中版本，但不要与功能修复放同一 PR。
3. 版本从程序集读取，替换 HTML 和 Logger 中硬编码的 `1.0.0`。
4. README 删除“100% 覆盖率”和硬编码规则数量，改为 CI 徽章或自动生成数据。
5. 增加 `.editorconfig`：UTF-8、4 空格、CRLF/LF 策略、移除尾随空格。
6. `.gitignore` 增加 `publish-wpf*/`、`*.bak`；历史发布产物通过单独清理提交移除。
7. 逐文件修复乱码；每次只处理一个功能域并截图验证，禁止全仓自动编码转换。

## 9. 推荐测试矩阵

每个 PR 至少执行：

`dotnet build SqlXmlAnalyzer.sln --no-restore`  
`dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj --no-restore --no-build`

另外增加：

- 规则正确性：正例、反例、边界阈值、重复节点。
- HTML 安全：危险标签和属性编码。
- 配置：默认路径、显式路径、缺失、损坏、重复 ID。
- 死锁：缺关键节点、未知锁类型、缺 victim、并行死锁。
- 异步：取消、乱序完成、重复打开。
- 重构：NULL、重复行、COUNT(*)、多子查询只修一个、语法回归。
- 发布冒烟：WPF 启动、打开 sqlplan/xdl/xel、导出 HTML/PDF、CLI JSON/JUnit。

## 10. 完成标准

第一里程碑可以定义为“可信、安全、稳定”，必须满足：

- 已知参数嗅探和统计信息误报有回归测试并修复。
- HTML 报告不接受未编码外部文本。
- 配置加载路径确定且失败可见。
- 死锁解析不会把半成品标记为成功。
- XEL 不落盘；其余临时文件在 finally 或会话结束时清理。
- 快速切换文件不会发生旧结果覆盖新结果。
- 完整构建 0 错误，全部测试通过。

架构重构不应阻塞第一里程碑。先保证诊断结论可信和输入输出安全，再逐步收敛模块边界。
