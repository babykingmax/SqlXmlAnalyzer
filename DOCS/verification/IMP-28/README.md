# IMP-28 兼容性验证

本目录为初版实施记录。配置迁移膨胀问题的修复结果见[IMP-28 审查修复验证](../IMP-28-hardening/README.md)：新增 15 项回归，Debug/Release 全量各 2754 通过。此处原始 TRX、程序集与源码哈希继续标识修复前版本；不要用当前产物重写本目录的历史结果。

交付范围、版本策略、D01–D09 消费者和不兼容变更见[实施说明及兼容矩阵](../../IMP-28模型配置报告与CLI兼容性.md)。[冻结样例来源](fixtures.md)区分历史配置、重建 2.0 会话和升级前程序实际输出，不将合成数据当作生产验收。

## 初版结果

| 构建 | 完整构建 | 全量 xUnit | IMP-28 新增测试 | 实际进程和文件转换 |
| --- | --- | --- | --- | --- |
| Debug | 0 警告、0 错误 | 2739 通过，0 失败，0 跳过 | 48 通过 | 22 检查通过 |
| Release | 0 警告、0 错误 | 2739 通过，0 失败，0 跳过 | 48 通过 | 22 检查通过 |

相对 IMP-27 加固基线 2691 新增 48 项：配置 9、会话 19、模型/报告 5、CLI 12、诊断 3（以 [summary.json](summary.json) 中 TRX 实际方法名及计数为准）。完整测试也保留 IMP-24 配置未知字段和备份、IMP-17 身份与来源 hash、IMP-22 报告、IMP-27 数据包回归。

失败阶段 `.tmp.imp28-results/Debug/config-red.trx`、`session-red.trx`、`session-shape-red.trx` 分别记录 5、10、2 个失败，结果和 SHA-256 保存于摘要。首轮全量回归另发现通用 `schemaVersion` 与旧扩展冲突，以及额外的会话文件扩展名限制；最终改用专用 `ConfigurationSchemaVersion` 并恢复任意扩展名，既有回归全部通过。

## 实际输出及转换

[Debug 索引](latest-Debug.json)和 [Release 索引](latest-Release.json)包含每次独立运行目录、七组真实 CLI 命令、退出码、输出文件、22 项检查及各产物哈希；每次脚本新建目录，不覆盖以往运行。

每次目录保留 `scan.stdout.json`、`read.stdout.json`、`refactor.stdout.json`、阈值/未知参数/未知配置/已有报告目标的错误输出，以及 `legacy.json → converted.json`、`legacy.pesession → converted.pesession`。原件不变用 SHA-256 验证；并保留拒绝未知会话版本和重复目标后的证据。会话运行日志展示 Debug 下的调试/警告/错误及 Release 下的错误；Critical 与真实 DUMP 在诊断测试中验证。

2026-09-11 提交整理时按用户要求排除原始日志，取消了合成 `session.log` 和 CLI `*.stderr.log` 的 Git 例外。这些日志不随源码提交；历史 JSON 中的日志路径和哈希仅作为当时运行的标识，检出后不能据此读取完整原始日志。结构化输出、转换样例及验证脚本继续保留；可重新运行探针获得新日志，但不得冒充历史记录。

## 可复现命令

在仓库根目录执行；两种探针使用独立 PowerShell 进程，避免同名程序集已载入后混用构建配置。

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug --no-restore *> .tmp.imp28-build-debug.log
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build --no-restore --logger 'trx;LogFileName=full.trx' --results-directory .tmp.imp28-results/Debug
dotnet build SqlXmlAnalyzer.sln -c Release --no-restore *> .tmp.imp28-build-release.log
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release --no-build --no-restore --logger 'trx;LogFileName=full.trx' --results-directory .tmp.imp28-results/Release
pwsh -NoProfile -File ./DOCS/verification/IMP-28/Run-CompatibilityProbe.ps1 -Configuration Debug
pwsh -NoProfile -File ./DOCS/verification/IMP-28/Run-CompatibilityProbe.ps1 -Configuration Release
./DOCS/verification/IMP-28/Write-Validation.ps1
./DOCS/verification/IMP-28/Write-Validation.ps1 -VerifySources
```

首次构建需先 `dotnet restore SqlXmlAnalyzer.sln`。`Write-Validation.ps1` 提取最终 TRX，检查全量成功、新增测试、探针输出 hash 及测试/CLI/WPF 三处对应程序集一致性；冻结失败阶段 TRX 仅在本机存在，其他检出可独立运行构建、测试及探针，不能凭新运行补造旧失败记录。

## 失败恢复、DUMP 和日志

`SessionCompatibilityTests` 注入部分 I/O、取消、篡改临时文件、并发创建目标，验证旧文件/并发文件保留且未发布部分新会话；未知管理扩展、错误版本、重复标识、悬空引用和含歧义文本的容器拒绝读取。界面 ViewModel 加载失败保留原历史和选择。

`CompatibilityDiagnosticTests` 实际调用 `WindowsMiniDumpWriter` 并使用 `MinidumpValidator` 检查 Windows minidump，另验证创建失败的侧车记录、同异常去重和主错误保留；临时 DUMP 由测试清理。日志测试强制请求 verbose，Debug 仍只按既有门禁记录 Debug/Warning/Error/Critical，Release 仅 Error/Critical，日志不含样例 SQL 文本。

未运行覆盖率、新的 SQL Server 语义场景、WPF 真实窗口/实体交互或发布包回退验收。此前这些验证继续作为对应版本的历史证据，IMP-29/30 仍待实施。[sources.json](sources.json) 和 [fixture-hashes.json](fixture-hashes.json) 用于内容核对，不提供真实性签名；原始 TRX / 构建日志保留于本机 `.tmp.imp28-*`。
