# IMP-30 发布与回退验证记录

本目录保留原实施 `imp30-rc1` 的历史证据。审查修复后的最新构建、候选与回退记录见 [IMP-30-hardening](../IMP-30-hardening/README.md)。源码和文档已更新，旧库存索引不能当作当前工作树的通过证明。

实施说明见 [IMP-30](../../IMP-30发布材料与回退验证.md)，随包材料见 [发布说明](../../release-materials/ReleaseNotes.md)、[操作单](../../release-materials/UpgradeRollback.md) 与 [问题状态](../../release-materials/IssueStatus.md)。本地候选不代表正式发布批准。

## 本轮结果

最终版本为 `2.0.0-imp30-20260910T110735Z-2b20e347`，详见 [summary.json](summary.json)。绑定 `.tmp.imp30-build-4`：814 项执行源码/材料，Debug、Release 各 **2821 项通过，0 失败，0 跳过**；强制重建和警告即失败均启用，构建零警告/错误。新增 30 项测试。

候选目录为 `E:/SqlXmlAnalyzer/publish/imp30-rc1`，本地 `publish/imp30-rc1.zip` 大小 167,938,774 字节，当时已在新目录解压并复验全部 38 个文件；此构建产物不随 GitHub 源码提交。ZIP SHA-256：`011926EB268D5B84FE1D01A640553752AF31DB4E5183D2400BA63E3B1CBA34E0`；内部清单 SHA-256：`63CD4E762C4C5181209F9CB1EAE9A2B3D169A8EFBD87510509807C3C4C652954`。

| 检查 | 结果与证据 |
| --- | --- |
| 完整构建/测试 | [构建证明](evidence/build-evidence.json)，原始日志与 TRX 位置/哈希保留在证明中 |
| 发布与 ZIP | [候选身份](evidence/candidate.json)、[38 项清单](evidence/candidate-manifest.json)、[解压验证](evidence/archive.json)；发布命令及退出码绑定同一构建 |
| 上一版本 | [源提交](evidence/baseline-source.json)、[旧包清单](evidence/previous-manifest.json)；GUI SHA-256 为 `B84F3F62DD53DD8ECDB60FC69388D4D93FF44B41167FF2F96967C6E91D78AB2D`，不同于候选 |
| 应用与数据回退 | [恢复记录](evidence/rollback.json)：两个 CLI 共 6 条实际命令通过；旧配置和 JSON 导出正常；SQL 恢复哈希与原件一致，当前 SQL 保留候选内容 |
| 随包旧会话读取 | [候选](evidence/candidate-session.json)、[恢复旧版](evidence/restored-session.json)：各自实际 EXE 中读取到 2 个快照及正确 A/B；仅启动钩子验收，不计可见窗口 |
| 异常与日志 | [Debug](evidence/diagnostics-Debug.json)、[Release](evidence/diagnostics-Release.json)：各生成并校验 1 个新原生 DUMP，Debug 四级/Release 两级日志门禁通过 |
| 可见桌面 | 阻塞，工具连续两次拒绝访问；没有候选窗口截图，不沿用 IMP-29 历史截图 |

自动化检查全部通过，`Imp30FullyAccepted=false`、`ApprovedForDistribution=false`。最终本地问题状态：D07 的文件回退和两个随包会话读取器已通过，可见应用回退验收仍待补齐；其他 R/D/UI 状态维持[候选问题表](../../release-materials/IssueStatus.md)的已验范围和限制。

本轮早期目录作为历史保留，最终证据只引用 `build-4`、`publish/imp30-rc1`、`.tmp.imp30-rollback-rc1`、`.tmp.imp30-session-rc1`、`.tmp.imp30-diagnostics-rc1` 和 `.tmp.imp30-archive-rc1`。早期 `Core/bin` 引导失败不计成功；修复后使用 GUI 目录的 Core 依赖，双配置独立入口测试及最终 DUMP 检查已重新通过。

## 可重复执行入口

在仓库根目录使用 PowerShell 7、.NET SDK 和 Windows x64。每次使用不存在的输出路径，所有示例中的哈希来自本次实际封存记录，不能复制历史值冒充新产物：

```powershell
pwsh -NoProfile -File DOCS/verification/IMP-29/Run-BuildTests.ps1 -OutputDirectory E:/SqlXmlAnalyzer/.tmp.imp30-build-new
pwsh -NoProfile -File publish.ps1 -BuildEvidence E:/SqlXmlAnalyzer/.tmp.imp30-build-new/build-evidence.json -OutputDirectory E:/SqlXmlAnalyzer/publish/imp30-new
```

准备上一源版本的干净隔离 checkout，分别执行 GUI/CLI 的 `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true '-t:Rebuild;Publish' -warnaserror`，保存退出码、完整命令和输出。记录真实 Git 提交、源树清洁状态，使用 `ReleaseBundle.Seal` 封存完整目录为 recovery。当前演练基线是 `2f50216d58d57f2546c8316dac508fdbcce2637b` 的重新构建，不冒充部署历史。

随后运行 `Run-RollbackDrill.ps1`，必填参数为 `Candidate`、`CandidateManifestSha256`、`PreviousBundle`、`PreviousManifestSha256`、`BuildEvidence` 和新 `OutputDirectory`。它创建本地测试 SQL、实际写回/备份，封存旧安装和用户数据样例，再恢复到新目录，验证两个真实 CLI 的帮助、读取、配置和 JSON 导出；不会执行数据库 SQL。

`Run-SessionHook.ps1` 用同一 `BuildEvidence`，接收 `Candidate`/`CandidateManifestSha256`、回退演练的 `Restored`/`RestoredManifestSha256` 和新 `OutputDirectory`。它编译仅用于测试的 .NET 启动钩子，分别在两个实际单文件 GUI EXE 进程中通过各自随包会话读取器读取恢复的旧会话。钩子验证后在 App.Main 之前退出，因此不能代替可见 WPF 窗口、键盘或截图验收；相关环境变量只作用于该探针进程及其子进程，最终恢复原值。

`Restore-Bundle.ps1` 是操作单的恢复入口，调用同一个 Core 校验器。`Test-Release.ps1` 由 6 项 xUnit 进程回归调用；boundary 场景还可单独运行保留 Debug/Release 的真实 DUMP 与日志证据。`ReleaseBundleTests` 另有 24 项测试。源码/材料 Git 可见性也在回归中。

## 证据规则

`build-evidence.json` 绑定执行时源码、两种配置的生产/测试 DLL 和原始构建日志/TRX 哈希。发布、回退及会话探针每步执行前后都重新校验该清单；阶段记录绑定同一构建证据哈希。包清单检查完整文件集合，并由交付记录单独保存清单 SHA-256。源代码、材料或产物发生变化后，不能重新散列旧结果来声称它验证了新版本。

大型 EXE、ZIP、TRX、构建日志和原生 DUMP 保留在本地生成目录，源码审查以脚本、测试、材料及本目录的 JSON 索引为主。恢复备份是本地敏感材料；此次仅使用合成 SQL/会话/配置样例。没有生成覆盖率报告。

[inventory.json](inventory.json) 汇总执行源码、最终文档及本地原始证据/候选文件的 SHA-256，包含大产物的位置，清单自身不递归纳入自身哈希。在保留这些本地路径的当前仓库中，可复核：

```powershell
. DOCS/verification/IMP-29/AcceptanceEvidence.ps1
$inventory = Get-Content DOCS/verification/IMP-30/inventory.json -Raw | ConvertFrom-Json
Assert-EvidenceFiles (Get-Location).Path $inventory.Records
```

清理本地大产物后，JSON 索引仍能随 Git 交付，但上述完整复核会报告缺失文件；不能将“索引存在”当作已保留原始证据。

## 桌面验收限制

本轮 Windows 交互工具在启动候选窗口时连续两次返回 `GetCursorPos failed: 拒绝访问。 (0x80070005)`。窗口枚举可用，但未获得可操作的候选窗口；未通过其他 UI 自动化绕过此错误。已请求用户确认桌面解锁及可交互状态。可见候选/恢复窗口的打开、导出与旧会话人工检查仍待补充，不能用 CLI、启动钩子或历史截图替代。

原 IMP-29 的 UI-10、UI-13、UI-14、UI-15 与 SQL 默认受限范围继续保留。完成本地自动化发布/回退检查不会自动关闭这些项目，也不会执行签名或对外分发。
