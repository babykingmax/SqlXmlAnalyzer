# IMP-06 审查修复与加固

> 后续实施：IMP-09 已完成共享能力、读取预算、取消结果、Partial 和 XEL 契约。本文保留 IMP-06 当时的边界；当前行为以 [IMP-09 说明](IMP-09统一文档读取与能力契约.md) 为准。

日期：2026-09-08。针对[审查记录](review-evidence/IMP-06/20260908T120357345Z/Review.md)中的 R1～R4 修复，并补充相邻边界的回归。此前工作区中的 IMP-03～06 改动继续保留。

## 修复内容

| 审查项 | 修复后的行为 | 主要实现 |
| --- | --- | --- |
| R1：错误命名空间的算子导致 Passed | 检查整个 ShowPlan 结构的元素命名空间，错误 QueryPlan、RelOp、算子与字段节点返回 INPUT_PLAN_STRUCTURE；合法无 RelOp 计划仍可识别 | InputRecognitionService.HasInvalidPlanContents |
| R2：编码重试替换坏字节 | 只有实际 BOM 能确定编码时才允许重试，UTF-8/16/32 都使用严格解码；坏字节返回 MalformedXml 或 ReadError，不生成替换后的文档 | InputRecognitionService.LoadXml / GetStrictBomEncoding |
| R3：事件标题覆盖文件路径 | XML/XEL 的选择回调分别传递源路径和显示名称；CurrentDeadlockFilePath 始终取源路径，刷新重新读取原文件 | XelDeadlockUiActionService、DocumentAnalysisUiActionService、MainWindow.Documents |
| R4：refactor 误解码辅助计划 | IFileHandler.OpenRead 返回原始字节流，共享服务负责识别与解码；编排器在生成 SQL 候选前拒绝失败输入，成功后通过 AnalyzeDocument 传递 XDocument | PhysicalFileHandler、ApplicationOrchestrator、IAnalysisEngine、SqlXmlAnalysisEngine |

命名空间校验保留微软 XSD 中 InternalInfoType 的任意内容扩展点：标准命名空间 InternalInfo 元素的内部内容不受该校验约束。根下多余元素和嵌套 Statements 中的未知语句种类也会被拒绝。校验采用迭代遍历，未增加递归遍历深度。

BOM 重试复用同一个已打开的流，避免重开路径时读取到被替换的文件。先识别四字节 UTF-32 BOM，再识别 UTF-16，处理短读取；手动跳过 BOM 并关闭 StreamReader 的自动 BOM 检测，防止运行时将严格解码器替换成宽松默认值。无 BOM 的输入直接遵循 XML 读取器的编码识别结果。重试始终使用 SafeXmlHelper，DTD 禁止和 XmlResolver=null 继续生效；不依据本地化异常文字决定编码。

IFileHandler 新增 OpenRead，调用者负责释放流，生产实现与测试替身已适配。IAnalysisEngine 保留 Analyze(string)，并提供 AnalyzeDocument 的默认适配，已有分析引擎实现可继续工作；生产引擎直接处理 XDocument，避免再次序列化和解析。SQL 源文件读取、备份及安全写回契约未改变。

未知异常继续由 ExceptionPolicy / UnexpectedErrorReporter 生成真实 Windows minidump 和 exception.json，同一异常去重。已知编码/读取错误不生成 DUMP。Debug 的 DEBUG/WARN/ERROR/CRITICAL 和 Release 的 ERROR/CRITICAL 策略不变；新增诊断不包含 XML/SQL 正文。

## 检索依据

按用户指定的来源优先级，先核对微软文档与官方 schema，再用 .NET 官方 GitHub 实现核实解码细节；这些一手资料已能支持本次修复。本次没有把社区推测作为实现依据。检索工具连接失败后直接读取官方网站；GitHub 插件在本会话提供工作流技能，公开源码通过 GitHub HTTPS 读取，没有发布或修改远端内容。

| 来源 | 核对结果及用途 |
| --- | --- |
| [Microsoft Learn：XmlReader.Create](https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlreader.create?view=net-8.0) | Stream 入口可探测字节编码；TextReader 已提供 Unicode 字符，XML 声明不会重新解码这些字符。用于统一 refactor 的字节入口 |
| [Microsoft Learn：UTF8Encoding 构造函数](https://learn.microsoft.com/en-us/dotnet/api/system.text.utf8encoding.-ctor?view=net-8.0) | 启用非法字节检测；不使用默认替换回退掩盖损坏输入 |
| [微软 SQL Server 2019 ShowPlan XSD](https://schemas.microsoft.com/sqlserver/2004/07/showplan/sql2019/showplanxml.xsd) | 元素命名空间限定、语句块类型、可选 QueryPlan 及 InternalInfo 任意内容扩展点。使用既有固定 schema，无运行时下载依赖 |
| [.NET 官方 StreamReader 实现](https://github.com/dotnet/runtime/blob/5535e31a712343a63f5d7d796cd874e563e5ac14/src/libraries/System.Private.CoreLib/src/System/IO/StreamReader.cs) | 自动 BOM 检测会切换编码对象并重新创建解码器；因此严格重试自行识别 BOM，禁用自动切换。引用 v8.0.0 对应提交 |

上述页面与源码于 2026-09-08 读取。固定 XSD 的 SHA-256 为 `845B3FAA55748D442DFC71071315FF1EBB611E4E298E8D06A21EBF946B8C93A8`，与原有来源记录一致。

## 回归与验证记录

新增 33 项回归：错误深层命名空间、根多余元素、嵌套语句、InternalInfo 扩展、UTF-8/16/32 坏字节、截断编码、大小端与 BOM/无 BOM 编码、跨 GUI/scan/refactor 的真实文件一致性、失败时不生成候选/备份和不写回 SQL、选择第一/第二事件后的刷新、短读取/同句柄重试、取消释放流及未知错误真实 DUMP。

先在旧实现上运行首批 27 项测试，得到 19 失败、8 通过，随后完成修复。[失败记录](implementation/IMP-06/hardening-20260908/red/red.trx)保留。针对新代码、选择器和编排器的 46 项测试已通过，见[针对性回归](implementation/IMP-06/hardening-20260908/focused/hardened.trx)。

| 最终验证 | 结果 | 证据 |
| --- | --- | --- |
| Debug 全量非增量构建与测试 | 0 警告、0 错误；1044 / 1044 测试通过，0 跳过；42 / 42 验收工具回归通过 | [摘要](acceptance/IMP-02/runs/20260908T122224064Z_c5b1e0766cfc_b8ced8e6/summary.json)、[构建](acceptance/IMP-02/runs/20260908T122224064Z_c5b1e0766cfc_b8ced8e6/build.stdout.txt)、[TRX](acceptance/IMP-02/runs/20260908T122224064Z_c5b1e0766cfc_b8ced8e6/test-output/existing-suite.trx) |
| Release 全量非增量构建与测试 | 0 警告、0 错误；1044 / 1044 测试通过，0 跳过；42 / 42 验收工具回归通过 | [摘要](acceptance/IMP-02/runs/20260908T122332967Z_c5b1e0766cfc_04004332/summary.json)、[构建](acceptance/IMP-02/runs/20260908T122332967Z_c5b1e0766cfc_04004332/build.stdout.txt)、[TRX](acceptance/IMP-02/runs/20260908T122332967Z_c5b1e0766cfc_04004332/test-output/existing-suite.trx) |
| 原审查场景的真实 Release CLI 复验 | 9 / 9 符合预期；错命名空间及坏字节 exit 1，UTF-16/UTF-32 辅助计划 refactor exit 0 | [命令和退出码](implementation/IMP-06/hardening-20260908/repro/cli-probes.json)、[编码观察](implementation/IMP-06/hardening-20260908/repro/encoding-observed.json) |
| 真实主窗口分析、选择和刷新流程 | 第一条 p1 → 第二条 p3 → 刷新后 p1；三阶段源路径一致，事件集合仍为 2；进程正常 exit 0 | [流程结果](implementation/IMP-06/hardening-20260908/mainwindow-flow.json)、[进程结果](implementation/IMP-06/hardening-20260908/mainwindow-flow-process.json)、[探针源码](implementation/IMP-06/hardening-20260908/MainWindowFlowProbe.cs.txt) |

主窗口探针使用真实依赖注册、MainWindow、DocumentAnalysisUiActionService 和刷新入口，没有替换事件接收回调。独立宿主只覆写启动以避免额外创建窗口，并自行加载应用资源、关闭所拥有的 Dispatcher；最终验证不显示窗口，因此不是 DPI/主题视觉验收。首次宿主退出超时保留为历史过程记录，修正的是探针宿主，不是产品代码。

两配置的验收矩阵均为 7 项最低条件满足、15 项 NotMet、0 项探针执行失败；因此矩阵按约定退出 1。其余改善步骤尚未完成，原 22 项问题的自动关闭数仍为 0，不与本次四项局部审查修复混淆。受保护输入漂移为空，构建后源码再次核对无变化。原生 DUMP 格式、未知错误去重、写入失败处理和两配置日志策略的测试均包含在全量测试中。

统一结果见 [verification.json](implementation/IMP-06/hardening-20260908/verification.json)。复验脚本为 [Run-ReviewProbes.ps1](implementation/IMP-06/hardening-20260908/repro/Run-ReviewProbes.ps1)，默认使用本次独立产物目录；测试无需联网。

最终构建使用仓库忽略目录 `bin/imp06-hardening-20260908`，延续此前避免同步程序锁定默认 WPF 产物的布局。编译设置、默认部署路径与依赖版本未改动。

## 范围

这是 IMP-06 的审查修复，不是所有 ShowPlan 字段或版本的完整验证。完整能力契约、资源预算、Partial、事件会话保存/恢复等仍属于 IMP-09/20。此次刷新回归验证源文件路径恢复有效；刷新后的事件会按既有打开流程重新默认选择第一条。

所有输入均为合成测试数据，没有连接数据库或执行 SQL。原审查证据作为历史记录保留，不覆盖其错误输出。
