# IMP-17：WPF 验证探针文件占用异常修复

日期：2026-09-09。

后续生产代码及探针回归更新见 [IMP-17 审查修复与加固](IMP-17审查修复与加固说明.md)。本篇保留文件占用故障修复的历史范围和验证记录；本轮新证据不会覆盖本篇原有清单。

## 原始事件与修复范围

用户报告的 `Probe.exe` / `Program.cs:69` 异常确实来自本次验证工具：旧探针用 `File.Create` 写入固定的 `comparison-light.png`，文件占用导致 `IOException` 逃出 `Main`，Windows 记录了 APPCRASH。这条堆栈指向临时验证程序，不是 `SqlXmlAnalyzer.exe` 的入口；仅凭记录中的 `KERNELBASE.dll` 不能认定 Windows 模块有问题。

上一轮已改为每次独立的截图目录，并完成后续正常验证；当时没有补上探针顶层异常边界，因此仅避开了同名输出冲突，不能称为完整的异常修复。本次补齐该边界。

本次修改验证探针、复现脚本和文档；IMP-17 生产比较逻辑及其 52 项新增 xUnit 用例保持原实现。旧临时可执行文件和 Windows 历史事件不会因源码修复自动改变。以后应通过当前 `verify-wpf.ps1` 重新构建探针。

## 当前行为

- 每次运行使用唯一的临时项目、中间构建目录及截图输出目录；截图和 JSON 结果使用 `FileMode.CreateNew`，不覆盖已有证据。
- `Main` 捕获验证、写入及关闭阶段的异常。预期 I/O、权限、输入错误记录 ERROR，返回退出码 **2**，不把文件锁当作未知故障生成 DUMP。
- 未知异常记录 CRITICAL，使用生产 `UnexpectedErrorReporter` 和 `WindowsMiniDumpWriter` 生成并验证 Windows minidump，同时保存托管异常 `exception.json`；返回退出码 **3**。DUMP 保存失败时保留明确错误，仍受控返回 3。
- 探针日志、DUMP 位于独立的临时诊断目录，避免依赖截图目录可写。日志文件不可写时使用共享 Logger 的 stderr 回退；不因此把已经成功的 WPF 验证判为崩溃。
- 正常完成返回 **0** 并打印 PASS；失败不打印 PASS，最终进程退出码是运行状态依据。结果写入前失败不会新建成功 JSON；已有结果文件保持原样。
- 沿用编译配置日志策略：Debug 记录 DEBUG/WARN/ERROR/CRITICAL，Release 只记录 ERROR/CRITICAL。重定向输出采用 UTF-8，以保留中文错误和 DUMP 路径。

## 可复现的进程回归

```powershell
.\DOCS\verification\IMP-17\verify-wpf.ps1 -Configuration Debug -VerifyFailureHandling
.\DOCS\verification\IMP-17\verify-wpf.ps1 -Configuration Release -VerifyFailureHandling
```

每个配置使用独立子进程验证下列 8 种情况。这些是进程/文件系统集成回归，单独统计，不计入 xUnit 数量。

| 情况 | 断言 |
| --- | --- |
| 正常运行 | 完成两种主题资源切换、截图、三语句比较及人工选择/恢复，绑定错误为 0，退出 0 |
| 截图被独占锁定 | 父进程持有真实 `FileShare.None` 文件句柄，子进程退出 2，原文件不变，无 PASS、无未知 DUMP |
| 截图已存在但未锁定 | 拒绝覆盖，退出 2，原文件不变 |
| 结果 JSON 被独占锁定 | 退出 2，原结果不变，不错误宣称成功 |
| 注入未知异常 | 退出 3；生成恰好一个原生 DUMP，校验 MDMP 文件头及异常侧录，并保存哈希 |
| DUMP 目录不可创建 | 退出 3，报告 DUMP 生成失败，无二次未处理异常 |
| 日志被独占锁定 | stderr 报告日志失败，WPF 验证仍完成，退出 0，原日志不变 |
| 参数缺失 | 作为输入错误退出 2，无未处理异常 |

每例均检查实际退出码、stderr、是否误打印 PASS，并检查 Release 没有输出 DEBUG/WARN/INFO。原始系统报告是旧探针失败记录，不予删除；回归不以清除事件日志作为修复证据。

本次 Debug/Release 各 **8/8 进程回归通过**；完整解决方案构建各 **0 警告、0 错误**，全量 xUnit 各 **1928 通过、0 失败、0 跳过**。[Debug 故障回归](verification/IMP-17/wpf-Debug-a7f4bae2a30240189db86fae326e9092/failure-handling.json)、[Release 故障回归](verification/IMP-17/wpf-Release-205b79d51c764485b08cab3cbb388aa1/failure-handling.json)保留受控退出和原生 DUMP 证据。DUMP 原件保留在报告列出的临时诊断目录，未复制到源码目录。

只读查询 Windows Application 日志的 1000/1001/1026 事件：**2026-09-09 14:34:00 至 14:38:23（UTC+08:00）未观察到新增 `Probe.exe` 崩溃事件**。这是本次复测窗口的结果，不代表删除了原始历史记录。见 [事件查询记录](verification/IMP-17-probe-fix/windows-events.json)。

验证结果及源码哈希见 [本次验证清单](verification/IMP-17-probe-fix/validation.json)。本次记录补充 [原 IMP-17 清单](verification/IMP-17/validation.json)，不替换历史验证时点的哈希或结果。

源码：[WpfProbe.cs.txt](verification/IMP-17/WpfProbe.cs.txt)、[构建与运行脚本](verification/IMP-17/verify-wpf.ps1)、[故障回归脚本](verification/IMP-17/Test-WpfProbeFailureHandling.ps1)。
