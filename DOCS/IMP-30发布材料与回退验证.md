# IMP-30：发布材料与回退验证

**审查修复后的最新结果**见[加固说明](IMP-30审查修复与加固说明.md)及[修复后证据](verification/IMP-30-hardening/README.md)：新增 17 项回归，双配置全量各 2838 通过、构建零警告/错误。修复了候选与构建关联、进程管道超时、相对路径、原始输出证据和异常分类。下列 `imp30-rc1`、2821 项测试及时间戳为原实施历史，不作为修复后新候选的验收证明。

本步交付本地 Windows x64 候选包、发布说明、升级/回退操作单、问题状态与执行证据。正式分发没有执行，完整改善版本 M7 不作通过声明。候选准备依赖 IMP-29 的范围内验收；实际 DPI、多显示器、实体键盘/屏幕阅读器、外部阅读器与 100 ms P95 性能目标的未完成项继续保留。

最终自动化验收：Debug/Release 各 2821 项测试通过、零失败/跳过、构建零警告/错误；新增 30 项测试。候选 `2.0.0-imp30-20260910T110735Z-2b20e347` 的 38 个文件及 ZIP 解压复验通过；6 条实际 CLI 命令、2 个实际单文件 GUI 的会话读取器、原 SQL 恢复和双配置各 1 个新原生 DUMP 均通过。见[最终摘要](verification/IMP-30/summary.json)。Windows 交互工具两次返回 `GetCursorPos / 0x80070005`，可见候选/旧版窗口的打开与导出验收尚未完成；因此 IMP-30 不标为全面验收完成，候选不批准正式分发。

## 实现

`publish.ps1` 要求输入双配置完整构建/测试的 `BuildEvidence`。先核对源码、二进制与测试记录的 SHA-256，再按现有 `dotnet publish` 流程为 GUI 和 CLI 生成 Release / win-x64 / self-contained / single-file 输出。强制重建，警告视为错误，进程退出码和超时都阻断发布；保留每条实际命令及 stdout/stderr。`publish.bat` 委托同一脚本并传回退出码。

候选包含 GUI、CLI、各自默认配置、全部发布依赖、发布材料、构建证据、候选状态与逐文件清单；不再指示用户只复制 GUI EXE。输出必须为新目录，脚本不会覆盖现有安装。固定程序集版本仍为 2.0.0，候选身份使用唯一 ID、执行源码清单和实际文件哈希。`ApprovedForDistribution` 始终为 false，SHA-256 不冒充签名。

`SqlXmlAnalyzer.Core/Deployment/ReleaseBundle.cs` 提供封存、校验和恢复新目录；命名空间为 `SqlXmlAnalyzer.Core.Release`。校验器必须接收外部可信清单哈希，检查实际文件集合、长度、内容哈希、版本和路径。拒绝缺失、额外、同长度篡改文件、重复/越界路径、NTFS 重解析点、预算超限和已有目标。恢复先复制到同卷私有目录，逐文件落盘，复验源和恢复目录，再以不覆盖目录重命名提交；失败保留私有暂存副本。

当前清单上限为 10,000 个文件、合计 8 GiB，读取清单 JSON 上限为 4 MiB；目录遍历另有限额。它用于本地应用和用户文件备份，不作为任意大小的数据库备份恢复工具。

`ReleaseOperation` 统一发布/恢复脚本的异常边界，展开 PowerShell 和反射包装后交给既有 `ExceptionPolicy`。预期 I/O、校验失败返回 1，取消返回 130；未知错误使用 `UnexpectedErrorReporter` 和 `WindowsMiniDumpWriter` 生成 Windows minidump 与异常侧车。生成失败保留主错误并写明原因。Debug 记录 Debug/Warning/Error/Critical，Release 只记录 Error/Critical；构建器输出、命令回执和 CLI 业务 JSON 属于验收证据，不与应用日志混淆。

## 回退范围

上一应用基线为已有验证记录中的提交 `2f50216d58d57f2546c8316dac508fdbcce2637b`（IMP-20–22）。在独立 detached worktree 重建 GUI/CLI，源树保持干净，记录原提交和新构建的产物哈希。这是上一源版本的重建包，不能描述为用户已部署的签名安装包。

演练脚本先备份该旧应用、旧配置、2.0 会话，以及 `SqlWritebackService` 实际写回时产生并校验的原 SQL 备份，再封存与恢复新目录。恢复后的 GUI 必须属于上一版本，CLI 必须能实际运行、用恢复配置读取计划并导出 JSON。旧会话恢复验证 A/B 为 `SELECT 1` / `SELECT 2`；SQL 恢复副本逐字节与原件对比，包含 UTF-8 BOM 和换行。当前 SQL 保留候选内容，证明应用回退没有偷偷覆盖新工作。

**回退应用或 SQL 文件不会撤销数据库已执行语句。** 数据库状态、DDL 和外部副作用需由 DBA 使用针对性回退脚本或数据库备份另行恢复。本步不提供突然断电、损坏介质、恶意管理员并发替换文件路径的恢复保证，也不代替实际部署前的备份演练。

## 材料与验证入口

- [随包发布说明](release-materials/ReleaseNotes.md)：行为修正、规则/日志区别、34 个规则 ID 的兼容范围、默认受限改写、CLI/格式契约和运行要求。
- [升级/回退操作单](release-materials/UpgradeRollback.md)：备份映射、清单哈希、恢复新目录、旧配置/会话及原 SQL 的核对步骤。
- [候选问题状态](release-materials/IssueStatus.md)：各 R/D/UI 的已验范围与未完成门槛。
- [本轮执行证据](verification/IMP-30/README.md)：完整构建/测试、候选身份、命令回执、DUMP 与实际窗口操作。

新增 30 项测试覆盖恢复字节/BOM、目标保护、篡改/缺失/额外文件、非法清单及路径、重复条目、未来版本、取消、重解析点、日志策略、原生 DUMP、PowerShell 包装异常、子进程失败/超时/参数带空格与 Git 可见性。未生成覆盖率报告，不声明覆盖率提升。

发布相关源码和文档显式纳入构建前后源清单。原命名 `Release/` 被仓库的通用构建目录忽略规则排除，实施过程中已改为 `Deployment/` 和 `release-materials/`，并添加回归。首次构建虽然 Debug 测试通过，但其源码清单随后变化，被门禁拒绝；不作为本候选的有效构建记录。

实际打包另发现 SDK 在 `Rebuild;Publish` 后可能清理 Core 项目自身的输出。发布、恢复和会话探针现从已纳入构建证明的 `bin/<Configuration>/net8.0-windows/SqlXmlAnalyzer.Core.dll` 引导，避免依赖被清理的 `Core/bin`。新增独立进程测试仅提供 GUI 目录的 Core 依赖，验证恢复入口仍能保留并恢复原字节。最终完整构建在该修复及全部验收工具稳定后重新执行。

## 参考依据

2026-09-10 核对 Microsoft 官方文档：[单文件与自包含部署](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)、[Directory.Move 的目标已存在约束](https://learn.microsoft.com/en-us/dotnet/api/system.io.directory.move?view=net-8.0)、[FileStream.Flush(Boolean)](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=net-8.0)、[MiniDumpWriteDump](https://learn.microsoft.com/en-us/windows/win32/api/minidumpapiset/nf-minidumpapiset-minidumpwritedump)。本步没有更改 SQL Server 分析或改写算法。
