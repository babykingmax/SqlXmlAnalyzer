# IMP-30 审查修复后验证

修复说明见 [5 项缺陷与加固](../../IMP-30审查修复与加固说明.md)。本轮沿用上一源版本重建包作为回退基线，重新执行构建、测试、打包、回退和随包会话读取。原 [IMP-30 / rc1](../IMP-30/README.md) 仅为历史。

## 最终结果

- Debug/Release **各 2838 项通过，零失败、零跳过**；完整强制重建均零警告/错误。新增 17 项回归，IMP-30 合计 47 项。
- 候选：`2.0.0-imp30-20260910T113215Z-0f3c050f`，目录 `publish/imp30-rc2`，38 个文件。GUI/CLI 业务代码未变，候选以 ID、构建证据和完整包清单区分。
- 本地候选 ZIP `publish/imp30-rc2.zip`：167,939,348 字节；当时已在新目录解压并验证候选与构建关联及全部文件。此构建产物不随 GitHub 源码提交。
- 实际回退 CLI 命令 6 条通过；实际候选/旧版单文件 GUI 会话读取器各通过一次，恢复 2 个快照及正确 A/B。两个脚本均使用相对输入路径执行。
- 原 SQL 恢复字节/哈希正确，当前 SQL 保留候选内容；没有执行数据库回退。
- Debug/Release 各新生成并验证 1 个原生 DUMP；四级/两级日志策略通过，未知字符串脚本异常仍生成 DUMP。
- 两个真实脚本入口均拒绝 `rc1` 借用当前构建证明，返回 1、记录 Error、没有 DUMP 或通过记录，并在执行候选前拒绝。

构建证明 SHA-256：`0087776A2A8C68102BEB2F40DA33973678904B957D9F4F657E627FEA01469F8A`。
候选清单 SHA-256：`A100086C166EEF569BD5F38E30525E54B0836599B74F1B4B81739DCA613D164E`。
ZIP SHA-256：`B60F731C54F0BBC97A453889E894BBB2F8A9443DF45223184BF3089EE58D9676`。

完整机器摘要见 [summary.json](summary.json)。`Imp30FullyAccepted=false`、`ApprovedForDistribution=false`。前轮可见窗口操作受 `0x80070005` 阻断，本轮未重新执行可见 WPF 验收；启动钩子在 App.Main 前退出，不能代替窗口操作。UI-10/13/14/15、受限 SQL 改写、签名与正式分发等原有保留项继续存在。

## 证据

| 检查 | 记录 |
| --- | --- |
| 完整构建/测试 | [构建证明](evidence/build-evidence.json)，包含源文件、双配置 DLL 及原始构建日志/TRX 哈希 |
| 发布 | [候选](evidence/candidate.json)、[完整清单](evidence/candidate-manifest.json)、[发布阶段](evidence/publish-stage.json) |
| 回退 | [受测输入](evidence/rollback-inputs.json)、[结果](evidence/rollback.json)、[阶段](evidence/rollback-stage.json)；阶段包含全部 18 个命令文件 |
| 随包会话 | [受测输入](evidence/session-inputs.json)、[候选结果](evidence/candidate-session.json)、[旧版结果](evidence/restored-session.json)、[阶段](evidence/session-stage.json) |
| 未知异常与日志 | [Debug](evidence/diagnostics-Debug.json)、[Release](evidence/diagnostics-Release.json)；各阶段绑定原生 DUMP、侧车和日志 |
| 旧候选拒绝 | [两个真实入口](evidence/negative.json)、[阶段](evidence/negative-stage.json) |
| ZIP | [解压复验](evidence/archive.json)、[阶段](evidence/archive-stage.json) |

大型二进制、ZIP、TRX、原始日志和 DUMP 留在本地目录。各阶段 JSON 中的相对路径以 `summary.json` 的 `StageRoots` 为根；`inventory.json` 使用仓库相对路径汇总全部绑定记录。源码、二进制或原始输出变动后，不得重新散列旧结果声称当前版本通过。

完整复核：

```powershell
. DOCS/verification/IMP-29/AcceptanceEvidence.ps1
$inventory = Get-Content DOCS/verification/IMP-30-hardening/inventory.json -Raw | ConvertFrom-Json
Assert-EvidenceFiles (Get-Location).Path $inventory.Records
```

若清理本地大产物，上述复核应报告缺失文件。索引不能代替原始证据；没有生成覆盖率报告。

## 重跑

在 Windows x64、PowerShell 7.4+ 和 .NET SDK 环境中使用新的输出目录：先运行 `DOCS/verification/IMP-29/Run-BuildTests.ps1`，将其 `build-evidence.json` 传给 `publish.ps1`，再将本次候选路径、发布输出的清单哈希和同一构建证明传给 `Run-RollbackDrill.ps1` / `Run-SessionHook.ps1`。不要复制本页旧哈希充当新产物身份。

17 项新增回归与原有测试一起由 `dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj` 执行；其中 `Test-Release.ps1` 支持独立运行相应 `-Case`，输出目录必须不存在。旧基线为提交 `2f50216d58d57f2546c8316dac508fdbcce2637b` 的重建包，并非历史部署的签名安装包。
