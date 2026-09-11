# IMP-27 审查修复验证

范围见[修复与加固说明](../../IMP-27审查修复与加固说明.md)。此目录记录修复后的验证，原 `IMP-27` 验证目录保留实施时的数据。

## 构建和全量测试

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug -m:1 *> .tmp.imp27-hardening-build-debug.log
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build --logger "trx;LogFileName=full.trx" --results-directory .tmp.imp27-hardening-results/Debug
dotnet build SqlXmlAnalyzer.sln -c Release -m:1 *> .tmp.imp27-hardening-build-release.log
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release --no-build --logger "trx;LogFileName=full.trx" --results-directory .tmp.imp27-hardening-results/Release
./DOCS/verification/IMP-27-hardening/Write-Validation.ps1
./DOCS/verification/IMP-27-hardening/Write-Validation.ps1 -VerifySources
```

两个配置完整构建各 **0 警告、0 错误**；全量测试各 **2691 通过、0 失败、0 跳过**。相对实施基线 2684 项新增 **7 项**；诊断包相关测试从 39 项增至 46 项。计数和名称从完整 TRX 提取，见 [summary.json](summary.json)。原始构建日志及 TRX 位于本机 `.tmp.imp27-hardening-*`，摘要记录其 SHA-256 和测试目录中的程序集哈希。

新增回归：无报告时版本匹配可执行规则；报告快照版本与当前目录版本分离（启用/禁用）；并行计划保留 Incomplete/Ambiguous（附带/不附带报告）；规范状态名称及自由文本分类限制；非法 UTF-8 提示且不触发 DUMP。

失败阶段证据也写入摘要：`rule-versions-red.trx` 失败 1 项、`metric-gaps-red.trx` 失败 2 项、`utf8-red.trx` 失败 1 项。最终完整测试中同名场景全部成功。指标测试同时区分非法单线程 CPU 计数的 Invalid 与聚合结果的 Incomplete。

## 异常、日志与边界

全量测试包含 `DiagnosticPackageDiagnosticTests`：实际调用 `WindowsMiniDumpWriter` 并验证 minidump；验证 DUMP 失败、同异常去重、诊断组件异常以及 Debug/Release 日志门禁。生成的测试 DUMP 与侧车文件由测试清理，不进入交付物。

默认隐私、不可变审核字节、显式附件、已有文件/并发写入保护、取消、ZIP 校验、预算及临时数据清理继续由既有测试验证。

源码及相关文档记录在 [sources.json](sources.json)。哈希用于核对内容身份，不提供真实性签名。未运行覆盖率、SQL Server 在线验证或新的 WPF/实体交互验收；此前 WPF 截图与探针结果仍是实施时的历史证据。
