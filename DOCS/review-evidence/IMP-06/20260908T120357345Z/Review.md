# IMP-06 代码审查记录

日期：2026-09-08。结论：发现 **4 项待修复问题（1 项 P1，3 项 P2）**，当前实现仍有错误输入被判定为 Passed、跨入口识别不一致以及多事件刷新回归。

后续状态：上述四项问题已于同日修复，并通过新增回归及真实 CLI/主窗口复验，见[审查修复与加固说明](../../../IMP-06审查修复与加固说明.md)。本文件下方保留审查当时的观察和原始证据。

本次只进行审查和复现，未修改产品代码、正式测试或原有交付文档。工作区同时含有未提交的 IMP-03～05 改动，审查范围按 IMP-06 实施说明及其调用链划分，不将整个工作区差异归入 IMP-06。

## R1 · P1：检查止于语句层，错误命名空间的算子被当成不存在

位置：[InputRecognitionService.cs](../../../../SqlXmlAnalyzer.Core/Services/InputRecognitionService.cs)，第 146～157 行，尤其第 151～152 行。

前置条件：ShowPlanXML、BatchSequence、Batch、Statements、StmtSimple 使用标准命名空间，但已出现的 RelOp 使用 `xmlns="urn:wrong"`。识别只检查 Statements 的直接子元素，随后直接返回 Success。规则和 CLI 扫描仍按标准命名空间查找 RelOp，因而遗漏该算子。

实际子进程结果：带成本 12 的 Table Scan，在 `--block-scans --max-cost 1` 下，正确命名空间的对照输入返回 Failed / exit 1；只改 RelOp 的命名空间后返回 Passed / exit 0，MaxSubtreeCost=0、ContainsScans=false、Issues=[]。这是错误结构被解释为“没有算子”，会使自动化门禁错误放行。

建议：在保留合法无 RelOp 计划的前提下，检查已出现的已知 ShowPlan 结构节点的命名空间和位置，拒绝这一类结构损坏。无需为此实现所有字段的完整 XSD 验证。增加“合法无算子”与“存在错误命名空间算子”的对照回归。

证据：[错误输入](wrong-relop-namespace.sqlplan)、[错误放行报告](wrong-relop-namespace.report.json)、[对照报告](correct-relop-namespace.report.json)。

## R2 · P2：编码重试会替换坏字节，损坏 XML 被判成功

位置：[InputRecognitionService.cs](../../../../SqlXmlAnalyzer.Core/Services/InputRecognitionService.cs)，第 116～124 行，尤其第 123 行。

UTF-8 声明的文件在 StatementText 内含非法字节 0xFF 时，首次 SafeXmlHelper.LoadSafe 正确抛出编码错误。当前 catch 随后使用默认替换回退的 StreamReader 重读，坏字节变成 U+FFFD，第二次 XML 解析成功。

实际结果：共享识别及 GUI 文件打开都返回 Success，重新构造的 XML 确实包含 U+FFFD；Release CLI 返回 Passed / exit 0。此路径既吞掉读取错误，也悄悄改变了被分析的 SQL 文本。

建议：只对明确的 BOM/声明兼容场景重试，并使用严格解码；坏字节、截断编码或非法代理项应返回稳定的非成功状态。保留已有 BOM 与声明不匹配的兼容测试，同时补充非法 UTF-8 和损坏 UTF-16 的反例。

证据：[坏字节输入](invalid-utf8-byte.sqlplan)、[严格读取与 GUI 观察值](encoding-observed.json)、[CLI 报告](invalid-utf8-byte.report.json)。严格读取原始异常为 `Invalid character in the given encoding`。

## R3 · P2：事件显示标题覆盖源文件路径，刷新多事件 XML 失败

位置：[XelDeadlockUiActionService.cs](../../../../Services/XelDeadlockUiActionService.cs)，第 90～91 行；下游 [DocumentAnalysisUiActionService.cs](../../../../Services/DocumentAnalysisUiActionService.cs) 第 193 行。

选择 XML 中任一事件时，回调第二个参数为“原路径 · 死锁事件 N · 时间”。下游把该字符串写入 CurrentDeadlockFilePath。RefreshDeadlockGraph 再把它作为文件路径传给 DocumentOpenService，File.Exists 返回 false，导致 INPUT_READ_ERROR。默认选择第一条时就会覆盖路径；切换事件不是必要条件。

STA 探针调用真实选择器，选择第二条，再调用真实刷新请求构建器和文件打开服务：原文件存在、事件数为 2，但刷新路径含显示标题，最终 ReadError。探针边界：文件路径赋值通过源码核对，未启动完整主窗口分析流程。

建议：将真实源文件路径与事件显示名称分开传递、存储。新增“打开多事件文件→选事件→刷新”的调用链回归，断言刷新仍使用真实源路径；现有选择器测试只断言标题和选中事件 XML，未覆盖这一状态传播。

证据：[观察值](selector-refresh-observed.json)、[STA 探针源码](SelectorReviewProbe.cs.txt)、[构建日志](selector-probe-build.log)。

## R4 · P2：refactor 的辅助计划仍经过不同的文本解码入口

位置：[ApplicationOrchestrator.cs](../../../../src/SqlXmlAnalyzer.Application/ApplicationOrchestrator.cs)，第 72～73 行；PhysicalFileHandler.ReadAllText 使用 File.ReadAllText。

GUI 与 scan 使用共享服务从字节流读取 XML，能按 XML 编码处理文件。refactor --plan 仍先用 File.ReadAllText（默认 UTF-8/BOM 检测）读成字符串，再交给分析引擎；共享识别之前就可能已按错误编码解码。

实际结果：同一份无 BOM、声明 utf-32 的计划，严格 XML 读取成功，且经随仓库保存的微软 ShowPlan XSD 全量校验通过。GUI / scan 返回 Success，scan exit 0；refactor --dry-run --plan 返回 INPUT_MALFORMED_XML / exit 1。UTF-8 对照输入的两个 CLI 入口均 exit 0。无 BOM UTF-16 输入也复现同样分歧，但此项以通过严格读取和 XSD 校验的 UTF-32 输入作为主要证据。

建议：让辅助计划通过相同的字节流读取/识别路径，再把已解析文档或正确解码的内容交给分析引擎。不要改变 SQL 源文件本身的读取与安全写回约定。补充使用真实文件的 GUI、scan、refactor 三入口编码一致性测试。

证据：[UTF-32 输入](valid-utf32-no-bom.sqlplan)、[严格读取/XSD 校验](utf32-validation.json)、[scan 报告](valid-utf32-no-bom.report.json)、[refactor 报告](refactor-valid-utf32-no-bom.report.json)。

## 检查范围与验证

逐项检查了共享识别及结果类型、DocumentOpenService、分析引擎、refactor 编排、独立 CLI 和桌面 CLI、死锁图/时间线/分析服务、WPF 文件路由与事件选择、MainWindow 接线和退出码传播，以及 IMP-06 测试、夹具、文档和相关验收逻辑。异常与日志检查包含复用的 ExceptionPolicy、UnexpectedErrorReporter、WindowsMiniDumpWriter、Logger 及其调用点。

本次执行前，将 449 个受保护源码/构建输入与最终交付快照逐一做 SHA-256 比较，未发现差异。使用该快照对应的既有产物重新运行完整测试，无需重建产品：

| 本次验证 | 结果 |
| --- | --- |
| Debug 全量测试 | 1011 / 1011 通过，0 失败、0 跳过 |
| Release 全量测试 | 1011 / 1011 通过，0 失败、0 跳过 |
| Release CLI 实际子进程 | 9 组，包括正反对照、编码和 refactor |
| WPF STA 选择器刷新探针 | 成功复现错误路径；探针构建 0 警告、0 错误 |

全量测试包含未知异常生成真实 Windows minidump、DUMP 失败报告与去重、Debug/Release 日志级别策略等已有回归。本轮未发现这些已覆盖路径的新增缺陷；测试全绿不消除上面四项问题，因为它们没有对应的完整场景回归。

[Debug TRX](tests/review-debug.trx)、[Release TRX](tests/review-release.trx)、[CLI 命令及退出码](cli-probes.json)、[源码核对](source-verification.json)。

复现 CLI 与编码检查：

```powershell
pwsh -NoProfile -File .\DOCS\review-evidence\IMP-06\20260908T120357345Z\Run-ReviewProbes.ps1
```

探针生成的所有输入均为人工合成测试数据，没有连接数据库或执行 SQL。独立探针项目放在仓库忽略的 bin/imp06-review-probe-20260908T120357345Z 中，源码快照以 .cs.txt/.csproj.txt 保存，避免加入正式项目编译。
