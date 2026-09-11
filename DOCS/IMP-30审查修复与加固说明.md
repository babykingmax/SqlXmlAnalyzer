# IMP-30 审查修复与加固

本轮修复发布/回退审查发现的 5 项缺陷，保留原候选与历史记录，重新构建新的本地候选。正式分发和可见 WPF 窗口验收不因自动化测试通过而获得批准。

## 修复内容

1. **候选与构建关联**：回退及会话入口统一调用 `Assert-ReleaseCandidate`。验证候选完整清单后，要求 `Kind=candidate`、候选 ID、状态中的构建哈希与内嵌 `build-evidence.json` 的实际 SHA-256 均匹配本次接受的构建。执行前后复验；`inputs.json` 绑定候选/恢复包身份和实际 GUI/CLI 二进制哈希。旧候选不能借用新构建证明。
2. **完整进程超时**：主进程退出和 stdout/stderr 捕获共用一个取消期限，使用 `Task.WhenAll` 与 `WaitAsync`。输出直接流式写入新文件，避免在内存累计全部日志。超时后有界终止仍存活的进程树、关闭管道并保留已捕获字节；回执记录超时和两路捕获是否完成，主进程退出 0 不能掩盖捕获失败。父进程已退出时 `Kill(tree)` 无法再发现脱离的子进程；此时关闭本端管道并使验收失败，不声称所有脱离进程已终止。
3. **路径解析**：候选、恢复目录、构建证明及输出目录都在调用者当前 PowerShell 工作目录下解析为绝对路径，再传递给子进程。测试覆盖带空格路径及不同子进程工作目录，避免会话路径改变和 EXE 身份误判。
4. **原始命令证据**：六条 CLI 命令及会话探针的命令回执、stdout、stderr 全部纳入阶段哈希清单；缺失或修改原始输出会阻断复核。证据文件以 CreateNew 写入，不覆盖旧命令结果。
5. **异常分类**：共享证据守卫显式抛出 `InvalidDataException`，文件读取保留 I/O 异常类型，JSON 格式错误转换为预期校验错误。继续通过 `ReleaseOperation` 展开 PowerShell 包装；没有把所有 `RuntimeException` 一律视为预期错误。未知字符串脚本错误仍必须生成真实 DUMP。

额外加固：从已经恢复并校验的旧应用副本读取配置，避免复验后再读取未锁定的原包；探针结果要求严格布尔成功值；发布相关脚本要求 PowerShell 7.4 或更新版本以承载 .NET 8 Core。未更改 SQL Server 规则、模型、配置版本、报告格式或 CLI 参数。

## 回归验证

新增 17 项进程回归，连同原 IMP-30 的 30 项共 47 项。覆盖继承管道超时及部分输出保留、相对路径、禁止证据覆盖、候选匹配与 6 类身份错误、3 类预期失败无 DUMP、未知脚本错误真实 DUMP、3 类原始输出缺失/篡改。全部采用现有 xUnit 入口，在 Debug 与 Release 完整测试中执行。

完整构建、测试与新候选结果以 [IMP-30 修复后摘要](verification/IMP-30-hardening/summary.json) 和 [执行证据](verification/IMP-30-hardening/README.md) 为准。Debug/Release 全量各 2838 项通过，构建零警告/错误。旧 `imp30-rc1` 记录仅作为历史，不能重新散列为本次修复的验收结果。未生成覆盖率报告。

## 资料核查

按 Microsoft 官方资料优先核对，2026-09-10 访问成功：

- [Process.WaitForExitAsync](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.waitforexitasync?view=net-8.0)：进程等待及取消语义。
- [Task.WaitAsync](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.task.waitasync?view=net-8.0)：异步任务的超时/取消期限。
- [PowerShell about_Throw](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_throw?view=powershell-7.4)：字符串抛出及 RuntimeException 包装。
- [PowerShell 与 .NET 版本关系](https://learn.microsoft.com/en-us/powershell/scripting/whats-new/differences-from-windows-powershell?view=powershell-7.4)：PowerShell 7.4 基于 .NET 8。
- [dotnet/runtime v8.0.0 Process 源码](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.cs)：核对 WaitForExitAsync 与输出 EOF 等待实现。

本轮没有 SQL Server 引擎行为变更，因此未将 CSS 或社区案例用作这些 .NET/PowerShell 修复的依据。GitHub 连接器不可调用时，使用该官方仓库的公开源码核对。
