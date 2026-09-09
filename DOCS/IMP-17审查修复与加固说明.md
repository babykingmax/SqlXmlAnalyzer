# IMP-17 审查修复与加固

后续联合审查已完成[比较证据完整性加固](IMP-17比较证据完整性加固.md)：补齐 BuildResidual、Sort/TopSort 和对象身份缺失检查，模型为 1.2.0，新增 55 项回归，Debug/Release 各 2012 项通过。下文的 1.1.0、1957 项及 GUI/探针结果为上一轮历史记录。

日期：2026-09-09。修复本轮审查确认的三个问题，并补充异常输入、共享快照和会话来源恢复的回归。当前比较模型为 `plan-comparison/1.1.0`，会话为 `2.1`。初版 IMP-17 的 52 项回归和 1928 项全量结果见[原实施记录](IMP-17多语句与可比性检查的AB比较.md)；本轮新增 **29 项**，Debug/Release 全量各 **1957 项通过、0 失败、0 跳过**，完整构建均 **0 警告、0 错误**。

## 修复结果

| 审查问题 | 触发与错误结果 | 修复后的行为 |
| --- | --- | --- |
| P1：Seek 身份只保留表达式文本 | 同一 SQL 的 `K > 1` 与 `J > 1` 都可能仅取到 `(1)`，被判为 High 算子对应，并显示没有依据的耗时差值 | 纳入范围列、ScanType、Prefix/StartRange/EndRange/IsNotNull 角色及有序表达式；不同列/边界不再形成 High 算子对应，相关差值为 N/A |
| P2：共享快照覆盖双侧选择 | A/B 指向同一 PlanSnapshot，选择 A 的第一条、B 的第二条时，写同一 SelectedQueryPlan 导致两侧都比较第二条 | A/B 选择由独立 PlanComparisonSelection 持有；同一快照可比较不同语句，交换、全部语句、保存和载入均保留正确状态 |
| P2：会话恢复丢失比较来源指纹 | 载入仅恢复快照 OriginalSourceHash，而比较器只读取模型 Envelope.SourceHash，详情退回 XML 哈希 | 显式传递与当前 XML 关联的原始指纹，同时显示 XML 表示指纹；来源标注为会话记录，无法核验关联时只保留历史记录 |

## 谓词身份与缺证据处理

[PlanComparisonPredicateEvidence](../SqlXmlAnalyzer.Core/Comparison/PlanComparisonPredicateEvidence.cs) 只在当前算子内取证，跳过子 RelOp、其他 QueryPlan/Statements、运行记录和内部扩展。谓词签名保留元素及属性的完整名称、属性值、子元素顺序和非空文本；属性顺序与命名空间前缀不影响身份。多个局部谓词仍保留各自角色。

结构化 ScalarOperator 有类型化表达式时，不再依赖可选的 ScalarString 展示文本；没有子表达式时保留文本作为启发式证据。复合范围列和对应表达式的顺序不能交换，`>` 与 `>=`、不同列、不同常量也不能合并。范围列/表达式数量、ScanType、必要的列名和常量值、空 Seek 包装、未知标量类型及外部命名空间均有检查。

证据不完整时，算子和包含该算子的子树不参与 High 结构匹配；唯一的类型/对象对应最多为 Candidate。多个 QueryPlan 的结构匹配也不把不完整签名提升为 High。语句文本配对可以仍为 High，但不因此绕过算子级证据检查。Candidate 保留对应提示和本侧指标，不生成该算子的数值差值。

这是针对比较身份的结构检查，**不是完整 Showplan XSD 校验或 SQL 等价证明**。未来未知表达式、别名变化、重写、自连接和重复形状仍可能只能形成候选或未匹配。完整范围缺失时拒绝生成高置信度差值，是有意的保守行为。

## 双侧选择与兼容入口

[PlanComparisonController](../Core/Services/PlanComparisonController.cs) 新增可选 `PlanComparisonSelection` 参数。显式 `new(null, null)` 表示全部语句；省略参数时仍读取旧 `PlanSnapshot.SelectedQueryPlan`，以兼容既有直接调用。WPF 使用独立选择记录，不再在点击“比较所选”时修改共享快照。

[MainViewModel.Comparison](../Core/ViewModels/MainViewModel.Comparison.cs) 的 `SetComparisonPlans` 先设置两侧快照与选择，再通知比较更新，避免恢复/交换过程发布混合状态。替换单侧快照仅重置该侧选择。命令校验选择属于当前候选列表，控制器进一步校验完整 QueryPlan 身份。

会话 2.1 根层 `ComparisonSelection/A`、`ComparisonSelection/B` 保存各侧 Batch/Statement/QueryPlan 序号；空根选择表示全部语句。旧会话没有根选择时，继续读取快照内的旧选择。载入后用当前文档身份重建键，拒绝重复/未知比较侧、非法序号及不存在的选择。保存前验证陈旧选择，验证失败不会覆盖原会话文件。此项不是对所有会话 I/O 的事务写回承诺。

## 指纹的含义与恢复规则

[PlanSnapshot](../Core/ViewModels/PlanSnapshot.cs) 将原始字节指纹与对应的 XML 修订关联；[TuningSessionService](../Core/Services/TuningSessionService.cs) 在保存/载入中保留这层关系。[ComparisonCapture](../SqlXmlAnalyzer.Core/Comparison/PlanComparisonEvidence.cs) 明确区分：

| 记录 | 来源和使用方式 |
| --- | --- |
| 原始文件字节 | 本次统一读取入口得到的 SHA-256 |
| 原始文件字节（会话记录，未重新读取原文件验证） | 会话内记录的原始 SHA-256，保存内容指纹与当前 XML 表示可关联；没有重新读取原文件 |
| XML 表示 | 缺少可关联的原始指纹时，使用当前 XML 根元素序列化内容的 SHA-256 |
| 历史原始文件 SHA-256（与当前 XML 的关联未核验） | 保留的来源元数据，不能作为当前 XML 的已确认来源 |

`XmlContentHashFormat="xml-representation/1"` 定义为根元素 `ToString(SaveOptions.DisableFormatting)` 的 UTF-8 SHA-256；包含根内保留的空白/注释，不包含文档声明和根外空白/注释。保存会话同样禁用自动缩进，避免写入时改变所记录的 PlanDoc 表示。全量回归发现旧 XDocument 级哈希包含根外尾部换行，而会话只保存根；本轮统一了实际存储范围，相关旧回归已通过。

新格式载入发现内容指纹不同，返回 `SESSION_CONTENT_HASH_MISMATCH`；未知指纹格式返回 `SESSION_HASH_FORMAT_UNSUPPORTED`；非法原始 SHA-256 返回 `SESSION_SOURCE_HASH_INVALID`。XML 后续变更会使原始指纹关联失效，再保存仍可以保留该指纹为历史记录，不提升为当前来源。

旧版没有格式标记，曾在保存后自动增加缩进。载入会尝试当前表示和移除空白文本后的旧表示。都不匹配时仍允许打开旧会话，原始指纹明确为未核验历史记录；Debug 日志记警告。缺少 XML 指纹的旧记录也不提升原始指纹。新格式不会使用这条宽容路径。

这些哈希帮助记录来源和发现意外内容变化，**不是会话认证、数字签名或原文件字节复核**。同时修改会话内容及元数据不在其防护范围内；哈希不用于绕过版本、SET、参数、DOP 和指标口径的可比性检查。

## 异常、DUMP 与日志

继续使用生产 `ExceptionPolicy`、异常报告器和日志配置。预期输入、会话校验、I/O、取消错误在入口报告，未知异常生成 Windows minidump 和 `exception.json`；DUMP 写入失败有失败记录，不把诊断失败当作正常比较结果。日志沿用 Debug 的 DEBUG/WARN/ERROR/CRITICAL 和 Release 的 ERROR/CRITICAL；CRITICAL 为致命级别。新增旧会话警告不包含 SQL、参数、对象名或完整文件路径。

此前用户报告的截图占用 `Probe.exe` APPCRASH 由[独立探针修复](IMP-17验证探针异常处理修复.md)处理。本轮继续使用只新建输出、顶层捕获、独立诊断目录和明确退出码，并将共享快照选择及指纹恢复加入真实 WPF 进程断言。文件占用属于已知 I/O 错误，不因占用而生成未知异常 DUMP。

## 回归与复跑

| 新增测试类 | 项数 | 主要覆盖 |
| --- | --- | --- |
| [PlanComparisonPredicateIdentityTests](../SqlXmlAnalyzer.Tests/PlanComparisonPredicateIdentityTests.cs) | 17 | 不同列/边界、复合键顺序、无 ScalarString 的类型化表达式、缺失/未知/外部命名空间证据 |
| [PlanComparisonSelectionIsolationTests](../SqlXmlAnalyzer.Tests/PlanComparisonSelectionIsolationTests.cs) | 6 | 共享快照 A1/B2、会话恢复、交换/重置、替换单侧、陈旧保存和重复侧输入 |
| [PlanComparisonSourceHashTests](../SqlXmlAnalyzer.Tests/PlanComparisonSourceHashTests.cs) | 6 | 原始指纹恢复、内容/原始哈希损坏、XML 修改、旧缩进格式、无关联记录 |

先运行反例观察失败，再实现并扩大回归；`red-*.log` 和中间 `green-*.log` 保留阶段状态，不能当作最终结果。初次全量 Debug 的 1 项指纹失败也保留，修复后的正式结果在 `build-final-*.log`、`test-final-*.log`。最终 Debug/Release 各 1957 项通过；未生成覆盖率报告。

WPF 进程验证通过真实控件和事件连接检查三语句比较、第二条成本差值 997、两种主题资源、选择/重置、共享快照 A1/B2、会话恢复及原始指纹，绑定错误为 0。每配置分别运行正常、截图被独占、截图已存在、结果文件被独占、未知故障、DUMP 目录不可用、日志被独占、非法参数八种场景，**Debug 8/8、Release 8/8 通过**；检查退出码、日志过滤、已有文件不被修改及实际 minidump 文件头/sidecar。[Debug 进程结果](verification/IMP-17/wpf-Debug-2dbe084c44994adca2eef7946b26336c/failure-handling.json)、[Release 进程结果](verification/IMP-17/wpf-Release-bcd54b06fc754fbb91a7d604a8ae2646/failure-handling.json)、[浅色截图](verification/IMP-17/wpf-Release-bcd54b06fc754fbb91a7d604a8ae2646/success/comparison-light.png)和[主题资源切换截图](verification/IMP-17/wpf-Release-bcd54b06fc754fbb91a7d604a8ae2646/success/comparison-dark.png)保留在独立目录。完整文件与 DUMP 路径/哈希见[本轮验证清单](verification/IMP-17-review-fix/validation.json)。DUMP 保存在本机临时诊断目录，清单仅保留定位与哈希。

复跑完整构建和测试：

```powershell
$runId = [Guid]::NewGuid().ToString('N')
foreach ($configuration in @('Debug', 'Release')) {
    dotnet build SqlXmlAnalyzer.sln -c $configuration --disable-build-servers -m:1 "-p:IntermediateOutputPath=obj/imp17-review-$runId/$configuration/"
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c $configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
    & DOCS/verification/IMP-17/verify-wpf.ps1 -Configuration $configuration -VerifyFailureHandling
}
```

使用独立 obj 和输出目录避开同步程序占用，不终止同步程序或覆盖历史证据。本轮没有新增 CLI 比较命令、RuleConfiguration 配置或规则 ID，也没有创建新的 SQL Server 性能实验；原有原生捕获计划回归继续执行。WPF 本轮验证比较行为与绑定，不替代后续整体 UI/可访问性验收。

## 检索依据

优先读取 Microsoft 官方资料；网页搜索连接失败，GitHub 连接器没有可调用接口，改用只读 HTTPS 获取官方页面和 Microsoft GitHub 源码。直接 schema URL 本次发生 TLS 连接失败，因此核对仓库已保存的 SQL Server 2019 官方 Showplan XSD。没有声称本轮已遍历 CSS、社区全部来源。抓取状态、文件哈希和 GitHub blob 信息见[检索记录](verification/IMP-17-review-fix/research.json)。

- [Microsoft Learn：Compare execution plans](https://learn.microsoft.com/en-us/sql/relational-databases/performance/compare-execution-plans?view=sql-server-ver17)：支持多语句计划选择及相似部分比较，并公开数据库名比较选项。本工具继续保留数据库身份。
- [Microsoft Showplan XSD](https://schemas.microsoft.com/sqlserver/2004/07/showplan/sql2019/showplanxml.xsd) / [仓库保存副本](../SqlXmlAnalyzer.Tests/TestData/Schemas/showplanxml-sql2019.xsd)：ScanRangeType 定义 RangeColumns、RangeExpressions 和必需的 ScanType；ScalarString 是可选展示信息，不能替代 Seek 范围结构。
- [Microsoft sqltoolsservice：ObjectWrapperTypeConverter](https://github.com/microsoft/sqltoolsservice/blob/main/src/Microsoft.SqlTools.ServiceLayer/ExecutionPlan/ShowPlan/ObjectWrapperTypeConverter.cs)：ScanRangeType 展示组合列、比较符与表达式，与本次需要保留的身份信息一致。
- [Microsoft sqltoolsservice：SkeletonManager](https://github.com/microsoft/sqltoolsservice/blob/main/src/Microsoft.SqlTools.ServiceLayer/ExecutionPlan/ShowPlan/Comparison/SkeletonManager.cs)：作为逻辑结构比较的参考。当前唯一匹配、置信度和保守停止规则由本工具实现，未复制或声称复现 SSMS 的匹配算法。

相关用户和架构说明已同步更新。工作区原有改动与历史证据保留；本轮没有提交、推送或发布。
