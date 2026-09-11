# IMP-04：高风险改写限制与诊断说明

日期：2026-09-08（Asia/Taipei）。HEAD：`c5b1e0766cfcdf28f0d3a27d3238f5f0d6ae1a15`，实现位于未提交工作树；前置步骤为 [IMP-03 安全写回](./IMP-03安全写回与恢复说明.md)。

**复审修复更新（2026-09-08）：** 已修复“打开日志目录”仍指向程序目录的 P2 回归，并加固路径/打开失败、未知异常 DUMP 与诊断失败提示。两配置完整构建均 0 警告/错误，各 965 测试通过。见[复审修复与加固](./IMP-03-05复审问题修复与加固.md)；下文原批次证据保留。

**联合加固更新（2026-09-08）：** Debug/Release 全量构建均 0 警告/错误，各 905 项测试通过；验收工具 38/38、应用进程 10/10 通过。写回未知异常保留提交状态，DUMP 失败保留异常记录，日志级别按编译模式固定；适配器升级 v2.3。逐项符合性、证据和剩余边界见[联合加固核对](./IMP-02-04加固与实施符合性核对.md)。历史结果保留如下。

**初次交付状态：IMP-04 默认风险限制、异常诊断、日志和自动化验证已完成。** Debug、Release 完整构建均为 0 警告、0 错误；两个配置各有 886 项测试通过、0 失败、0 未执行。独立验收工具 32 项通过，进程演练 8/8 通过，并分别生成了真实 Windows DUMP。R02/R19/R20 的默认保护条件通过，连同 R03 共 4 项最小条件通过；其余 18 项 `NotMet`。这是风险缓解交付，没有自动关闭整个评审问题。

## 1. 实施结果

| 原行为 | 当前行为 | 实现 |
| --- | --- | --- |
| 无列数据约束时移除 LTRIM/TRIM，仅提示“假设无前导空格” | 保留应用改写规则中的 LTRIM/RTRIM/TRIM，记录跳过原因和未证明的前提 | [TrimRefactorRule](../src/SqlXmlAnalyzer.Refactoring/Rules/TrimRefactorRule.cs) |
| 表变量直接转换为临时表，可能改变回滚、作用域或同名对象行为 | 保留表变量，不自动创建/删除临时对象，说明事务与作用域验证要求 | [TableVariableRefactorRule](../src/SqlXmlAnalyzer.Refactoring/Rules/TableVariableRefactorRule.cs) |
| 规则抛异常后继续下一条规则，仍可能返回成功/部分改写结果 | 立即终止本次改写，清除已记录的候选变更，返回原 SQL 和失败；未知异常生成 DUMP | [SqlRefactoringEngine](../src/SqlXmlAnalyzer.Refactoring/SqlRefactoringEngine.cs) |
| 没有改写仍重新格式化 SQL，可能触发写回 | 无实际变更时返回原文本，沿用 IMP-03 字节比较，不产生多余备份和写回 | 引擎、[ApplicationOrchestrator](../src/SqlXmlAnalyzer.Application/ApplicationOrchestrator.cs) |
| GUI 只返回 OutputSql，丢失上下文警告；异常文本混入 SQL 注释 | 风险/失败说明进入现有警告区，SQL 文本保持独立；复制 SQL 不附带错误注释 | [PlanAnalysisService](../Core/Services/PlanAnalysisService.cs)、[RefactorPresentation](../src/SqlXmlAnalyzer.Application/Services/RefactorPresentation.cs) |
| 报告写入异常被吞掉，可能仍报告成功 | 报告器向编排传播异常；提交后的报告失败仍明确源文件已写入 | [JSON 报告](../src/SqlXmlAnalyzer.Application/Services/JsonResultReporter.cs)、[控制台报告](../src/SqlXmlAnalyzer.Application/Services/ConsoleResultReporter.cs) |
| GUI、CLI、分层引擎日志级别和输出位置不一致 | 共用文件/stderr 输出及编译模式过滤；CLI UTF-8 JSON 和日志可正确重定向 | [Logger](../SqlXmlAnalyzer.Core/Logger.cs)、[ApplicationLoggerProvider](../src/SqlXmlAnalyzer.Application/Services/ApplicationLoggerProvider.cs)、[CLI](../SqlXmlAnalyzer.CLI/Program.cs) |

保持规则 ID `REF_RULE_103_TRIM`、`REF_RULE_002_TABLE_VAR` 不变。保护位于规则的 `Apply` 内，因此显式选中规则或直接调用规则不会绕过前提检查；本步骤没有添加“不安全强制应用”开关，也没有改变 `RuleConfiguration.json`。当前没有可证明等价的数据契约，因此采取保守的跳过行为，完整候选提案/审核机制由 IMP-18/19 继续实现。

SQL 语义依据：LTRIM 会移除字符串开头的空格或指定字符，无法从右侧常量推断列中不存在这些字符；表变量与临时表的事务/回滚及作用域行为也不能直接等同。参考 Microsoft：[LTRIM](https://learn.microsoft.com/en-us/sql/t-sql/functions/ltrim-transact-sql?view=sql-server-ver17)、[table 数据类型及限制](https://learn.microsoft.com/en-us/sql/t-sql/data-types/table-transact-sql?view=sql-server-ver17)。本轮通过保持原写法控制风险，没有连接 SQL Server 证明候选 SQL 等价。

## 2. 结果与风险说明合同

[RefactorSafetySkip](../SqlXmlAnalyzer.Core/Models/RefactorSafetySkip.cs) 包含 `RuleId`、`ReasonCode`、`Reason`、`RequiredEvidence`。同一规则/原因在多轮执行中只记录一次，并同步形成可读警告。跳过不等于引擎失败；`RefactorChanges` 只记录实际生成的候选变更。

| Outcome | 含义 | SourceWritten |
| --- | --- | --- |
| `NoChanges` | 分析完成，没有产生改写；风险写法原样保留，原因见 SafetySkips/Warnings | false |
| `CandidateGenerated` | 产生候选，尚未写入源文件，例如 dry-run | false |
| `Applied` | 编排已确认写回成功 | true |
| `Failed` | 本次操作失败，候选不可按成功结果应用 | 还须查看写回详情，提交后的报告失败可能为 true |

JSON 报告新增 `SafetySkips`、`Outcome`、`SourceWritten` 和 `Diagnostic`，保留原字段。正常写回的状态只在 IMP-03 文件服务确认提交后设置；写回失败使用既有 `Writeback` 详情。控制台报告显示对应的中文状态。GUI 当前仍为 dry-run，显示“已生成候选，尚未应用”或“分析完成，未产生改写”，不宣称已经修改用户 SQL 文件。

Release 的级别过滤作用于运行日志；用于用户决策的跳过原因和验证前提仍保留在 JSON、文本报告和 GUI 警告区。

## 3. 异常处理与 DUMP

新增 [UnexpectedErrorReporter](../SqlXmlAnalyzer.Core/Diagnostics/UnexpectedErrorReporter.cs)、[WindowsMiniDumpWriter](../SqlXmlAnalyzer.Core/Diagnostics/WindowsMiniDumpWriter.cs)、[ExceptionPolicy](../SqlXmlAnalyzer.Core/Diagnostics/ExceptionPolicy.cs) 和 [GlobalExceptionHandlers](../SqlXmlAnalyzer.Core/Diagnostics/GlobalExceptionHandlers.cs)。

1. 输入语法错误、取消、文件 I/O、权限及编码错误作为可识别失败处理；记录相应日志并保留错误原因，不为这些正常失败路径生成进程 DUMP。
2. 未知异常在改写规则、引擎管线、编排、GUI 改写适配器及 CLI 边界记录致命日志，调用共享诊断服务生成 DUMP。同一异常实例穿过多层边界只捕获一次。
3. WPF 在启动入口注册 Dispatcher、AppDomain、未观察 Task 异常处理；CLI 注册 AppDomain/Task 处理并捕获主入口异常。已有捕获点调用 `Logger.LogException` 时也使用同一分类策略。此项不代表已经逐一改造仓库中所有历史 catch。
4. Windows 捕获使用 `MiniDumpWriteDump` 和独立后台线程，进程内 DbgHelp 调用串行化。先独占写入 `.dmp.partial`、刷新并关闭，再改名为 `.dmp`；校验 MDMP 签名、版本、流目录及流位置/长度边界后才报告 `DumpCreated=true`。
5. `exception.json` 保存异常类型/堆栈、操作位置、UTC 时间、进程 ID 和 RequestedDumpPath（计划路径），在捕获前先独占写入并刷新，补充被捕获的托管异常上下文。未知规则异常返回原 SQL；用户可以从失败报告获取 DUMP 路径。
6. 捕获或保存异常记录失败不会替换原始操作错误。记录 `Failure` 和明确失败说明；默认主目录失败时尝试临时目录。后台捕获超过 30 秒会报告失败，超时后禁止迟到发布完成 DUMP，后续请求不排队启动线程；部分文件可能保留至底层调用返回。

默认 DUMP 目录为 `%LOCALAPPDATA%\SqlXmlAnalyzer\dumps\<UTC_PID_GUID>\process.dmp`，备用目录为 `%TEMP%\SqlXmlAnalyzer\dumps`。每次创建独立事件目录，Windows 在创建时设置当前用户所有权和受保护 DACL，子文件继承该权限。[DiagnosticFileAccess](../SqlXmlAnalyzer.Core/Diagnostics/DiagnosticFileAccess.cs) 不扩大访问权限；`.gitignore` 排除 `.dmp` 与 `.dmp.partial`。

这是含线程信息的 Windows minidump，托管异常原始堆栈另存 JSON；它不是完整托管堆内存转储。Microsoft 建议条件允许时从独立进程捕获，进程内捕获应使用专用线程，并串行调用 DbgHelp；本实现采用后者。进程内存已严重损坏、进程被强杀、磁盘不可写或底层捕获挂起时无法承诺成功。未进行断电、内存耗尽或真实 DbgHelp 挂死验证；联合加固有可控阻塞注入验证超时与禁止迟到发布。参考：[MiniDumpWriteDump 使用约束](https://learn.microsoft.com/en-us/windows/win32/api/minidumpapiset/nf-minidumpapiset-minidumpwritedump)。

DUMP 可能含进程内敏感数据，仅在本地保存，不自动上传；没有自动删除已生成 DUMP 的保留策略。排查后由操作者按数据保留要求处理。

## 4. 日志行为

| 构建配置 | 输出的运行日志级别 | 验证 |
| --- | --- | --- |
| Debug | DEBUG、WARN、ERROR、CRITICAL | 单元测试和真实进程均验证四种标记进入文件与 stderr |
| Release | ERROR、CRITICAL；运行时请求 Debug/verbose 不能放宽此下限 | 单元测试和真实进程验证 DEBUG/WARN 标记缺失，ERROR/CRITICAL 保留 |

日志默认位于 `%LOCALAPPDATA%\SqlXmlAnalyzer\log\SqlXmlAnalyzer_<UTC_PID_GUID>.log`，每个进程使用独立文件名。原 Info/Verbose 调用归入 DEBUG 调试信息；分层 `ILogger` 通过 `ApplicationLoggerProvider` 使用同一策略。新增改写日志以规则 ID、原因代码、数量及阶段为主，不主动记录完整 SQL。

文件日志自动刷新；控制台日志统一写 stderr，stdout 用于结果报告。CLI 显式使用无 BOM 的 UTF-8 输出；最终进程演练按严格 UTF-8 解码，并断言“已跳过”“回滚”等中文内容。无法创建或写入日志文件时，向 stderr 报告日志故障，诊断输出失败不替换原始操作异常。

## 5. 初次交付验证与历史证据

环境：Windows，.NET SDK 10.0.400，PowerShell 7.6.5，应用目标仍为 .NET 8 / WPF。相对 IMP-03 的 852 项，本步骤增加 34 个执行用例，并修改五个接受旧危险默认行为的回归测试；没有增加 Skip 或生成覆盖率报告。

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| Debug 全量 | 构建 0 警告/错误；886 通过，0 失败/未执行 | [构建](./acceptance/IMP-02/runs/20260908T093220366Z_c5b1e0766cfc_0420c7c1/build.stdout.txt)、[TRX](./acceptance/IMP-02/runs/20260908T093220366Z_c5b1e0766cfc_0420c7c1/test-output/existing-suite.trx) |
| Release 全量 | 构建 0 警告/错误；886 通过，0 失败/未执行 | [构建](./implementation/IMP-04/20260908T091503194Z_c5b1e0766cfc_52a72a38/release-final-02/build.stdout.txt)、[TRX](./implementation/IMP-04/20260908T091503194Z_c5b1e0766cfc_52a72a38/release-final-02/full-release.trx)、[摘要](./implementation/IMP-04/20260908T091503194Z_c5b1e0766cfc_52a72a38/release-final-02/summary.json) |
| 验收工具 | 32 通过，0 失败 | [独立工具回归](./acceptance/IMP-02/runs/20260908T093220366Z_c5b1e0766cfc_0420c7c1/infrastructure-tests.json) |
| 产品最小反例 | R02/R03/R19/R20 为 ProbeConditionMet；18 项 NotMet；0 个 ProbeExecutionFailed | [逐项观察](./acceptance/IMP-02/runs/20260908T093220366Z_c5b1e0766cfc_0420c7c1/acceptance-results.json)、[摘要](./acceptance/IMP-02/runs/20260908T093220366Z_c5b1e0766cfc_0420c7c1/summary.json) |
| 两配置真实进程 | 各验证默认保留、候选未应用、确认写回、未知异常；总计 8/8 通过 | [进程摘要、DUMP 路径及源指纹](./implementation/IMP-04/20260908T091503194Z_c5b1e0766cfc_52a72a38/process-final-02/summary.json) |
| 实际 DUMP | 两份文件均为 MDMP，均含 14 个数据流；未知错误后原 SQL SHA-256 不变 | [Debug 诊断](./implementation/IMP-04/20260908T091503194Z_c5b1e0766cfc_52a72a38/process-final-02/debug/unknown-error/verification.json)、[Release 诊断](./implementation/IMP-04/20260908T091503194Z_c5b1e0766cfc_52a72a38/process-final-02/release/unknown-error/verification.json) |
| 版本与历史保护 | 校验输入、源码快照、历史附件、DUMP/日志指纹与差异归属 | [交付校验](./implementation/IMP-04/20260908T091503194Z_c5b1e0766cfc_52a72a38/deliverable-validation.json)、[变更归属](./implementation/IMP-04/20260908T091503194Z_c5b1e0766cfc_52a72a38/change-ownership.json)、[证据清单](./implementation/IMP-04/20260908T091503194Z_c5b1e0766cfc_52a72a38/artifact-manifest.json) |

测试源码：[风险规则与中断](../SqlXmlAnalyzer.Tests/Refactoring/UnsafeRewriteProtectionTests.cs)、[日志与 DUMP](../SqlXmlAnalyzer.Tests/Diagnostics/DiagnosticLoggingTests.cs)、[GUI 服务和异常传递](../SqlXmlAnalyzer.Tests/Application/RefactorSafetyIntegrationTests.cs)、[CLI](../SqlXmlAnalyzer.Tests/Application/CliProgramTests.cs)。覆盖前导空格/NULL、嵌套 TRIM、比较方向、事务回滚、同名临时表、重复执行、显式选择、直接规则调用、多轮去重、规则在 AST 修改后抛错、预期失败不生成 DUMP、DUMP 失败、日志失败以及候选/应用状态区分。

可读样例：[Release 中文风险报告](./implementation/IMP-04/20260908T091503194Z_c5b1e0766cfc_52a72a38/process-final-02/release/risk-preserved.stdout.txt)、[Debug 运行日志](./implementation/IMP-04/20260908T091503194Z_c5b1e0766cfc_52a72a38/process-final-02/debug/unknown-error/runtime.log)、[Release 运行日志](./implementation/IMP-04/20260908T091503194Z_c5b1e0766cfc_52a72a38/process-final-02/release/unknown-error/runtime.log)。运行器退出 1 表示仍有未满足的矩阵条件，不是本轮构建/单元测试失败。

重复验证使用 `Run-AcceptanceMatrix.ps1`；Release 与进程演练脚本位于本步骤版本目录。将脚本及 `DiagnosticProbe.cs.txt` 复制到新的版本目录后运行，已有输出目录会拒绝覆盖。探针工程在系统临时目录构建，源码在 DOCS 中保存为 `.cs.txt`，不会进入应用默认编译。

保留开发中间结果：首次构建出现一个已修正的 nullable 警告；首次定向测试有两项失败，分别为日志读取的共享模式与语法错误位置说明，第二次 147 项全通过。首轮独立进程的结构断言通过但遗漏中文乱码，已追加 UTF-8 修复及中文断言；`process-final`/`release-final` 和首次全量运行均保留，最终使用 `*-02` 与上表指向的 Debug 版本。它们均属于本轮开发过程，不登记为历史基线失败。

## 6. 后续边界

R02/R19/R20 记录为“默认风险已缓解”，完整等价性、提案审核与作用域模型仍由 IMP-18/19 处理。GUI 适配器的 SQL/警告传递有自动化测试，但未启动实际 WPF 窗口验证可见交互；GUI 审核应用由 IMP-21 继续验收。本轮未执行 SQL Server，也没有宣称其他改写规则或所有历史异常捕获点已完成全面整改。当前里程碑 M1 仍在实施中，下一步骤为 IMP-05。
