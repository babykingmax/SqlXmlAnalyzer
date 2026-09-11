# IMP-28 审查修复验证

范围见[修复与加固说明](../../IMP-28审查修复与加固说明.md)。本目录记录修复后的结果，原 [IMP-28 验证](../IMP-28/README.md)保留实施时记录。

## 构建和测试

Debug/Release 完整构建各 **0 警告、0 错误**；全量各 **2754 通过、0 失败、0 跳过**。相对修复前 2739 项新增 15 项迁移回归；IMP-28 兼容性相关测试合计 63 项。计数、测试名称、TRX、程序集哈希见 [summary.json](summary.json)。

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug --no-restore *> .tmp.imp28-hardening-build-debug.log
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build --no-restore --logger 'trx;LogFileName=full.trx' --results-directory .tmp.imp28-hardening-results/Debug
dotnet build SqlXmlAnalyzer.sln -c Release --no-restore *> .tmp.imp28-hardening-build-release.log
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release --no-build --no-restore --logger 'trx;LogFileName=full.trx' --results-directory .tmp.imp28-hardening-results/Release
./DOCS/verification/IMP-28-hardening/Write-Validation.ps1
./DOCS/verification/IMP-28-hardening/Write-Validation.ps1 -VerifySources
```

首次检出需先恢复依赖。失败阶段的 `indentation-red.trx` 为 1 失败，`escaping-red.trx` 为 4 失败；它们分别复现原缩进问题和只关闭缩进仍存在的字符转义问题。`hardening-green.trx` 为 15 通过，最终全量包含全部同名回归。

原始 TRX/构建日志位于本机 `.tmp.imp28-hardening-*`，摘要保留 SHA-256。其他检出可以运行完整构建和测试；原始失败记录不存在时，不能用新结果补造历史失败记录。

## 保护措施与验证范围

新测试通过 `Parse`、`WithCurrentSchema`、`RuleConfigurationStore` 公共接口验证大配置复制、重新载入、字节边界、幂等、未知字段保留、覆盖拒绝和取消。原始配置和活动配置在失败后保持不变。没有改变配置预算、规则运行结果、CLI 参数或会话格式。

完整测试继续包含 `CompatibilityDiagnosticTests` 和 `RuleConfigurationDiagnosticTests`，实际验证 Windows minidump、捕获失败、诊断组件异常及 Debug/Release 日志过滤；测试 DUMP 由测试清理。本次没有新增桌面操作、数据库语义、独立 CLI 进程或发布包回退运行，原 IMP-28 的 CLI 与转换样例保持历史身份。未生成覆盖率报告。

[sources.json](sources.json)记录全部 C# / WPF 构建输入、新测试与相关文档哈希；修复前的 13 个冻结样例按原 manifest 逐项核对未改变。哈希用于内容身份核对，不提供真实性签名。
