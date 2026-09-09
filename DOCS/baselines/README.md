# 实施基线采集与复用

本目录保存实施步骤的可重复采集工具及独立运行记录。当前 IMP-01 已完成；说明见 [IMP-01 基线冻结与模块边界](../IMP-01基线冻结与模块边界.md)。

## 当前基线

| 编号 | 环境 | 构建 / 测试 | 证据 |
| --- | --- | --- | --- |
| `20260908T074458598Z_c5b1e0766cfc_c3523269` | SDK 10.0.400 / Debug | 0 警告、0 错误；807 通过，0 失败 | [摘要](./IMP-01/20260908T074458598Z_c5b1e0766cfc_c3523269/summary.json)、[完整性验收](./IMP-01-20260908T074458598Z-验收记录.json) |

## 重复采集当前状态

从仓库根目录执行，需要 PowerShell 7+、Git 和能构建本项目的 .NET SDK：

```powershell
pwsh -NoProfile -File DOCS/baselines/Capture-ImplementationBaseline.ps1 -RepositoryPath E:/SqlXmlAnalyzer -Configuration Debug
```

每次使用 UTC 时间、HEAD 前 12 位和随机标识生成新目录；脚本不会覆盖已有目录。它记录当前工作树，而不是自动恢复之前的基线。默认执行 restore、全解决方案非增量 build 和全部测试项目，并保存实际解析的依赖列表；可能需要访问已配置的包源。它不提交代码、不切换分支、不连接 SQL Server、不启动 GUI、不发布软件。

脚本先用 Git 获取文件清单，按规则排除生成目录和历史运行目录；没有递归列举仓库根目录。源码扩展名定义为 `.cs`、`.xaml`、`.csproj`，配置和夹具另建清单，全部在范围输入另有指纹。

## 证据结构

- `summary.json`：运行版本、结果、计数、漂移和验证范围。
- `commands.json` 与 `*.stdout.txt` / `*.stderr.txt`：命令参数、时间、退出码和完整日志。用 `.txt` 保存日志，避免仓库的 `*.log` 忽略规则漏掉证据。
- `git-ls-files.txt`、`git-index.txt`、`git-status-before/after.txt`、`working-tree.patch`、`index.patch`：Git 跟踪、暂存与未暂存状态。
- `input-manifest.json`、`configuration-manifest.json`、`fixture-manifest.json`：输入 SHA-256、长度与来源；`source-inventory.txt` 单列编译源码范围。
- `snapshots/<原路径>.snapshot`：既有未跟踪文件、已变更跟踪文件、配置和夹具的原字节，不是待编译源码。
- `test-output/baseline.trx` 与 `test-nonpassed.json`：原始结果和非通过测试项。
- `input-drift.json`、`input-path-drift.json`：采集到验证结束之间的输入变化；基线自身新文件不算源输入漂移。
- `artifact-manifest.json`：证据文件自身的 hash 清单，不包含自己；这是完整性校验，不是数字签名。

若构建失败，脚本记录失败并跳过测试，避免错误地测试旧 DLL；若测试失败则保留 TRX 和非通过项。还原/构建/测试失败、缺少 TRX、输入漂移或 HEAD/index 变化都会返回非零退出码。辅助环境/依赖命令仍逐项保存退出码，使用者也要检查 `commands.json`，不能仅凭脚本退出码断言全部元数据齐备。

## 对照与恢复原则

1. 先校验 `artifact-manifest.json` 中记录的证据文件，再读取输入指纹和命令结果；原审查记录始终独立保留在 `DOCS/review-evidence/`。
2. 对后续版本按路径比较源码/配置 hash、按测试名称比较结果；SDK、依赖、Debug/Release 或夹具变化分别记录。
3. 精确恢复应在独立检出中定位 HEAD，先恢复 index patch，再恢复 working-tree patch，最后用快照恢复新增文件和已保存的原字节。按 `input-manifest.json` 验证存在性、长度与 hash，尤其注意 Git 换行转换。
4. 未修改的跟踪文件依赖 Git 中的对应 HEAD，本目录不是包含所有源码和 NuGet 包的离线备份。若不具备原 HEAD、SDK 或依赖，需要先补齐环境，不能宣称完全复现。
5. 基线文档和规划状态会在采集后更新；这些 DOCS 变更属于交付登记，不反写历史输入清单。新的运行一律创建新编号目录。
