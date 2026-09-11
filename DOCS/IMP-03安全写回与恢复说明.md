# IMP-03：安全写回与恢复说明

日期：2026-09-08（Asia/Taipei）。基线 HEAD：`c5b1e0766cfcdf28f0d3a27d3238f5f0d6ae1a15`，实现位于未提交工作树。

**联合加固更新（2026-09-08）：** Debug/Release 全量构建均 0 警告/错误，各 905 项测试通过；验收工具 38/38、应用进程 10/10 通过。写回未知异常保留提交状态，DUMP 失败保留异常记录，日志级别按编译模式固定；适配器升级 v2.3。逐项符合性、证据和剩余边界见[联合加固核对](./IMP-02-04加固与实施符合性核对.md)。历史结果保留如下。

**初次交付状态：IMP-03 文件服务、编排及 CLI 实施完成。** 备份失败会立即终止；候选 SQL 先写入同目录临时文件，校验后才提交。最终完整构建 0 警告、0 错误，xUnit 852 通过、0 失败、0 未执行；验收工具回归 32 通过；真实 CLI 进程演练 3 通过。R03 最小条件为 `ProbeConditionMet`，其余 21 项仍为 `NotMet`。整个 R03 不自动关闭：GUI 审核应用闭环仍按 IMP-19/21 验收，本轮没有启动 WPF 窗口或执行 SQL Server。

## 1. 原问题与实施范围

原编排在备份异常后仅添加警告，仍继续覆盖 SQL，且可能输出成功。现在备份是写回的硬前置；禁止以直接覆盖、先删除原文件再移动或复制覆盖作为失败回退。改写引擎仍负责候选 SQL，本步骤只负责安全存储及状态传递，不证明候选 SQL 语义等价。

| 模块 | 实施结果 |
| --- | --- |
| [SqlFileSnapshot](../src/SqlXmlAnalyzer.Application/Models/SqlFileSnapshot.cs) | 保存不可变原字节快照、SHA-256、解码文本、编码和 BOM；拒绝有损解码 |
| [SqlWritebackService](../src/SqlXmlAnalyzer.Application/Services/SqlWritebackService.cs) | 编排源指纹校验、独立备份、临时文件、提交、取消及失败清理 |
| [ISqlWritebackService / 文件接口](../src/SqlXmlAnalyzer.Application/Services/ISqlWritebackService.cs) | 注入文件操作与提交故障；不依赖真实磁盘故障复现 |
| [PhysicalSqlWritebackFileSystem](../src/SqlXmlAnalyzer.Application/Services/PhysicalSqlWritebackFileSystem.cs) | 独占创建、磁盘刷新、同目录移动覆盖；Windows 创建时保留源 DACL 与所有者 |
| [SqlWritebackResult](../src/SqlXmlAnalyzer.Application/Models/SqlWritebackResult.cs)、[OrchestratorResult](../src/SqlXmlAnalyzer.Application/Models/OrchestratorResult.cs) | 携带阶段、是否写入、是否结果未知、取消、备份验证状态、恢复路径与指纹 |
| [ApplicationOrchestrator](../src/SqlXmlAnalyzer.Application/ApplicationOrchestrator.cs) | 非 dry-run 使用新服务；备份/写回失败不调用成功报告；有解析或规则失败时禁止写回；区分提交后的报告失败 |
| [SqlReportPathGuard](../src/SqlXmlAnalyzer.Application/Services/SqlReportPathGuard.cs)、[CLI](../SqlXmlAnalyzer.CLI/Program.cs) | 拒绝报告路径与 SQL 相同，包括路径别名、可解析符号链接及 Windows 文件身份相同的硬链接；失败 JSON 输出写回详情，Ctrl-C 传递取消 |
| [WPF 注册](../App.xaml.cs)、[PlanAnalysisService](../Core/Services/PlanAnalysisService.cs) | 注册共享服务并传递取消；现有 GUI 调用仍为 dry-run，失败原因通过现有错误文本返回 |
| [ConsoleResultReporter](../src/SqlXmlAnalyzer.Application/Services/ConsoleResultReporter.cs) | 普通模式标签改为 `Normal (Writeback enabled)`，不再仅凭普通模式就宣称已写入 |

原有 `IFileHandler` 继续用于只读/报告相关调用；写回集中在新文件服务。接口增加可选服务参数和可选 `CancellationToken`，仓库内调用点和测试同步迁移。

## 2. 写回步骤与失败合同

1. 读取源文件完整字节，严格识别 UTF-8（有/无 BOM）、带 BOM 的 UTF-16 LE/BE 或 UTF-32 LE/BE，记录 SHA-256。不能安全解码、源文件为文件级重解析点或 EFS 加密文件时拒绝写回。
2. 引擎生成候选。若有解析错误、规则失败或失败结果，则停止。写回服务使用源编码生成候选字节，再次核对当前源指纹；字节未变化时返回成功但 `SourceWritten=false`，不创建备份或临时文件。
3. 用独占 `CreateNew` 创建 `<源路径>.SqlXmlAnalyzer-<GUID>.bak`。写入原快照字节，刷新到磁盘，关闭并重新读取校验；此时才设置 `BackupVerified=true`。只对真实文件名冲突重试，最多 8 次；权限、磁盘等错误立即失败，不覆盖既有备份。
4. 用同样的独占创建方式在源目录创建 `.tmp`，分块写入完整候选、刷新、关闭、重新读取并校验候选 SHA-256。这里的校验是存储字节完整性校验，不是 SQL 语义验证。
5. 备份和临时文件准备完成后再次核对源 SHA-256、检查取消，再以同目录 `File.Move(..., overwrite: true)` 提交。提交成功后才设置 `SourceWritten=true`，此后收到取消也不能谎报未写入。
6. 失败时清理本次拥有的临时文件和未验证备份；保留验证成功的备份。清理失败时报告残留路径；`BackupVerified=false` 的残留不得用于恢复。
7. 提交调用抛错时重新读取源文件：等于候选则明确当前已包含候选；等于原快照则返回未覆盖；无法读取或出现第三种内容则设置 `CommitOutcomeUnknown=true`，保留恢复证据，不自动覆盖当前文件。
8. 写回后的报告异常单独返回失败，明确 SQL 已写回及备份位置；调用者不能仅凭进程非零退出码推断 SQL 未改动。

| 状态 | 调用者应如何解释 |
| --- | --- |
| `IsSuccess=false`，`SourceWritten=false`，`CommitOutcomeUnknown=false` | 本次没有确认写入；前置失败不由本服务覆盖原文件。外部程序可能已修改源文件，应保留其当前内容 |
| `SourceWritten=true` | 已完成写入或在提交异常后观察到候选字节；即使整体失败也不能按“未写入”重试 |
| `CommitOutcomeUnknown=true` | 提交结果无法确认；先保存当前文件并人工核对，不能自动回滚 |
| `BackupVerified=true` | 备份创建后已与原快照 SHA-256 一致；恢复时仍须重新核对指纹 |
| `IsCanceled=true` | 写回服务在相应阶段观察到取消。提交后的取消不能逆转已发生的提交 |

Windows 备份与临时文件在创建时应用源 DACL 和所有者，避免写入后再收紧权限留下暴露窗口。只读源的保护不会主动移除。若当前身份无法创建具有所需权限的备份，操作失败并保留源文件。

选择同目录移动提交的依据是避免把“API 抛错”误当作“原文件一定不变”：Microsoft 对 `ReplaceFileW` 的文档明确列出替换失败后原文件/替换文件可能处于不同名称状态。本实现仍对提交异常核对字节，不作无条件回滚。参考：[ReplaceFileW 错误语义](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew)、[MoveFileExW 行为与权限](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-movefileexw)、[.NET File.Replace](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace?view=net-8.0)。

## 3. 验证结果与复现

初次交付验收版本为 `20260908T090642185Z_c5b1e0766cfc_c45e1ec8`，适配器 `2.1`、证据格式 `schemaVersion=2`。执行环境为 Windows、PowerShell 7.6.5、.NET SDK 10.0.400，项目目标仍为 .NET 8。

| 验证 | 结果与证据 |
| --- | --- |
| 完整还原及非增量构建 | 成功，0 警告、0 错误；[构建日志](./acceptance/IMP-02/runs/20260908T090642185Z_c5b1e0766cfc_c45e1ec8/build.stdout.txt)、[命令记录](./acceptance/IMP-02/runs/20260908T090642185Z_c5b1e0766cfc_c45e1ec8/commands.json) |
| 全量 xUnit | 852 通过、0 失败、0 未执行；[测试日志](./acceptance/IMP-02/runs/20260908T090642185Z_c5b1e0766cfc_c45e1ec8/test.stdout.txt)、[TRX](./acceptance/IMP-02/runs/20260908T090642185Z_c5b1e0766cfc_c45e1ec8/test-output/existing-suite.trx) |
| 独立工具回归 | 32 通过、0 失败；[结果](./acceptance/IMP-02/runs/20260908T090642185Z_c5b1e0766cfc_c45e1ec8/infrastructure-tests.json) |
| R03 备份失败反例 | `IsSuccess=false`、`SourceWritten=false`、`BackupAttempted=true`、`CreateBackup` 阶段失败；[观察与断言](./acceptance/IMP-02/runs/20260908T090642185Z_c5b1e0766cfc_c45e1ec8/acceptance-results.json) |
| 22 项矩阵 | 1 `ProbeConditionMet`、21 `NotMet`、0 `ProbeExecutionFailed`、0 个问题自动关闭。运行器退出 1 是其余未满足条件的约定，不是构建或测试失败；[摘要](./acceptance/IMP-02/runs/20260908T090642185Z_c5b1e0766cfc_c45e1ec8/summary.json) |
| 运行期间输入漂移 | 空数组；[检查](./acceptance/IMP-02/runs/20260908T090642185Z_c5b1e0766cfc_c45e1ec8/protected-input-drift.json) |
| 真实 CLI 进程 | 正常写回 exit 0、源文件占用 exit 1、报告同源路径 exit 2，3/3 通过；[断言和指纹](./implementation/IMP-03/20260908T085018432Z_c5b1e0766cfc_65e9ddb4/cli-smoke-results.json) |

新增[写回服务测试](../SqlXmlAnalyzer.Tests/Application/SqlWritebackServiceTests.cs)，迁移[编排测试](../SqlXmlAnalyzer.Tests/Application/ApplicationOrchestratorTests.cs)并扩展[CLI 测试](../SqlXmlAnalyzer.Tests/Application/CliProgramTests.cs)。故障场景覆盖备份/临时文件创建、部分写入、刷新、读取和校验失败；权限拒绝、真实文件占用、只读、外部修改、取消、命名冲突、清理失败、编码/BOM、DACL/所有者保留、提交后抛错、结果未知及提交后的报告失败。相对历史 807 项增加 45 个执行用例；没有生成覆盖率报告，不据此声称覆盖率提升。

真实 CLI 输出样例：[成功 JSON](./implementation/IMP-03/20260908T085018432Z_c5b1e0766cfc_65e9ddb4/cli-smoke/success.stdout.txt)、[占用失败 JSON](./implementation/IMP-03/20260908T085018432Z_c5b1e0766cfc_65e9ddb4/cli-smoke/locked.stdout.txt)、[路径冲突错误](./implementation/IMP-03/20260908T085018432Z_c5b1e0766cfc_65e9ddb4/cli-smoke/same-report-path.stderr.txt)。输入是本步骤新建的合成 SQL，只处理本地文件。

重复完整验证：

```powershell
pwsh -NoProfile -File DOCS/acceptance/IMP-02/Run-AcceptanceMatrix.ps1 -RepositoryPath E:/SqlXmlAnalyzer
```

脚本每次另建版本目录，不覆盖旧证据。CLI 演练脚本见 [Run-CliSmoke.ps1](./implementation/IMP-03/20260908T085018432Z_c5b1e0766cfc_65e9ddb4/Run-CliSmoke.ps1)：将其复制到一个新的版本目录，再运行；脚本遇到已存在的 `cli-smoke` 目录会拒绝覆盖。演练前先完成正常构建。

## 4. 恢复操作

1. 读取失败详情中的 `Stage`、`SourceWritten`、`CommitOutcomeUnknown`、`BackupVerified`、`BackupPath` 和 `SourceSha256`。不要因为出现 `.bak` 文件就认定它有效，也不要因为退出码非零就认定原文件未变。
2. 先将当前 SQL 另存到新的位置，保留所有外部修改。提交结果未知时同时保留临时文件、备份和日志，停止自动重试。
3. 仅对 `BackupVerified=true` 的备份重新计算 SHA-256，与 `SourceSha256` 核对。可用 `Get-FileHash -LiteralPath '<实际备份路径>' -Algorithm SHA256`。不一致或无法读取时停止恢复。
4. 将备份复制到新的恢复候选文件，核对 SQL 内容、编码/BOM及访问权限；比较当前 SQL、恢复候选和计划应用的候选，确定需要保留的版本。
5. 经操作者确认后，在占用解除且已保留当前文件的情况下替换工作文件；复查恢复后 SHA-256。程序不自动以旧备份覆盖外部新内容。
6. 验证业务文件可读后再决定清理临时文件和冗余备份。备份可能包含敏感 SQL，应按原文件的访问与保留要求管理；本步骤不自动删除已验证备份。

## 5. 证据归属与历史保留

原审查四附件、IMP-01 基线、IMP-02 v1/v2 历史运行原样保留。本轮只迁移当前 R03 适配器到新文件接口，使用独立、固定的合成候选改写，避免后续 IMP-04 禁用某条改写规则后导致备份故障未被触发。其余问题的业务预期不变。

源码与文档归属、最终输入复核、历史附件指纹及完整性结果见[交付校验](./implementation/IMP-03/20260908T085018432Z_c5b1e0766cfc_65e9ddb4/deliverable-validation.json)与[变更归属](./implementation/IMP-03/20260908T085018432Z_c5b1e0766cfc_65e9ddb4/change-ownership.json)。源码另存 `.snapshot`，防止证据被默认 C# 编译通配符纳入；[证据清单](./implementation/IMP-03/20260908T085018432Z_c5b1e0766cfc_65e9ddb4/artifact-manifest.json)包括 Git 默认忽略的开发日志和演练 `.bak`。

开发阶段 `writeback-tests-01` 至 `04` 保留了本轮遇到并修复的测试编译、DACL 创建/设置及等价权限断言问题；`05` 已通过。它们属于本轮开发中间结果，不能登记成历史基线失败。两次中间全量运行亦保留，最终应以本说明指向的版本为准。

## 6. 明确边界与后续工作

- 当前 GUI 的重构入口是 dry-run，通过既有文本通道返回失败原因；本轮没有新增 GUI 直接覆盖 SQL 的按钮，也未完成 WPF 实机验收。共享文件服务已就绪，审核应用与恢复交互由 IMP-19/21 继续实现。
- SHA-256 检查是乐观并发检测，不是文件系统比较交换或全程文件锁。最终校验与移动之间仍有窗口，不能宣称阻止了任何时序的并发修改。报告路径检查同样不是防恶意并发替换路径的沙箱。
- 实测针对 Windows 普通文件。文件级重解析点和 EFS 源文件被显式拒绝；本轮未做实际 EFS、网络共享、断电或其他操作系统集成验证。刷新和移动不构成跨任意文件系统的事务持久性承诺。
- 保护范围为原 SQL 主数据流字节、编码/BOM和本轮验证的 Windows DACL/所有者；不宣称完整保留全部 NTFS 备用数据流、时间戳等元数据。硬链接的其他名称可能仍指向旧文件，用户需对这些名称单独核对。
- `IMP-04` 仍需限制语义风险改写；`IMP-18/19` 处理提案与审核应用；`IMP-21` 完成对应 GUI 交互。本步骤的存储安全不等于改写语义已经安全，也不等于整个 R03/D07/UI-05 已关闭。
