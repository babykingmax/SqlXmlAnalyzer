# IMP-12 审查修复与加固

日期：2026-09-09。范围为本次审查确认的四项回归，以及直接相关的配置、身份合并和入口一致性保护。

## 修复结果

### 兼容已有结果分支

原适配器要求结果编号必须等于执行规则编号，错误地把四个已有分支转成 Failed，丢失提示并触发未知异常捕获。现在仅允许下列明确对应的内置实现返回分支编号：

| 执行规则 / 配置所属规则 | 保留的结果 RuleId |
| --- | --- |
| RULE_009_PARALLEL_SKEW | RULE_010_INEFFECTIVE_PARALLELISM |
| RULE_006_RESIDUAL_PREDICATE | RULE_007_NON_SARGABLE |
| RULE_013_ANTI_PATTERN | RULE_013_CASE_IN_PREDICATE |
| RULE_003_PARAM_SNIFFING | RULE_003_OPTIMIZE_FOR_UNKNOWN |

`PlanDiagnostic.RuleId`、语义编号及旧列表结果保留分支身份；`Origins`、`RuleRun.RuleId` 和配置快照保留实际执行规则。启用开关和严重度覆盖仍读取原规则配置，不增加配置项或改变阈值。格式器同时展示结果编号与执行来源。合并按执行来源排序选取主诊断，确保内容、结果编号和版本归属一致；复制旧结果编号，防止调用方修改原结果后污染已发布诊断。

未知、空白或冒用其他内置实现的结果编号仍为 Failed，并进入既有未知异常 DUMP 捕获。允许名单同时检查具体实现类型和执行编号，没有取消协议校验。此修复恢复旧分支行为，不代表已完成这些旧规则的领域推理校正。

### 分离状态与性能警告

`PlanGraphWarningResult`、节点构建结果及 ViewModel 新增 `DiagnosticStatusText`；生产节点提示在独立的“诊断运行状态”区域显示摘要及跳过/失败详情。NoHit、Disabled、不适用和缺证据记录不再填充性能警告文本，警告图标由真正的诊断/警告或 Failed 决定。缺证据状态仍可查看，完整调用记录仍保存在 `Diagnostics.Runs`。

### 正确区分串行与缺证据

ThreadSkewRule 先检查明确的 `Parallel=false`，返回 `Skipped / RULE_NOT_APPLICABLE`。只有 Thread 0 的串行实际计划不再被要求提供并行 worker 分布，也不再因此令 `HasMissingEvidence=true`。并行计划的计数缺失、重复 Thread、只有协调线程等情况继续标记缺少证据；合法全零 worker 计数可以得到 NoHit。

### 统一默认能力推导

诊断上下文已有 XML 来源，默认推导现在包含 `PreservedSource`，与输入识别契约一致。GUI、节点、默认 Detailed API 和显式传入能力的 Analysis/CLI 对同一输入给出相同结果。调用方明确传入的能力限制仍原样保留；源 XML 改变时沿用模型失效机制，重新计算运行计数能力和文档身份。

## 异常与日志

沿用原规则执行边界：已知输入/I/O 错误保留 Failed，真实取消传播；未知错误调用 `UnexpectedErrorReporter`，写真实 Windows minidump 和异常侧车文件。诊断组件或 DUMP 写入失败不会覆盖原规则错误。同一异常实例仍去重捕获。

Debug 记录 DEBUG / WARN / ERROR / CRITICAL；Release 只记录 ERROR / CRITICAL。合法分支和不适用状态不再制造未知错误 DUMP。新增测试使用记录型 reporter 验证捕获次数，完整测试另外验证真实 DUMP、捕获失败及两种配置的日志过滤。

## 验证

新增 47 项测试：33 项协议/边界测试、13 项入口/状态测试，以及 1 项生产 WPF 提示片段测试。初始 31 项核心用例在修复前有 18 项失败、13 项通过，修复后通过；最终新增 47 项均通过。

完整 Debug / Release 构建各 0 警告、0 错误；完整测试各 1481 项通过、0 失败、0 跳过。两种配置均通过真实 DUMP、捕获失败及日志过滤测试。

- [修复前回归结果](./implementation/IMP-12/hardening/hardening-red.trx)
- [新增用例验证结果](./implementation/IMP-12/hardening/hardening-green.trx)
- [当前完整构建、测试与源码哈希](./implementation/IMP-12/verification.json)
- [原实施验证快照](./implementation/IMP-12/hardening/before-verification.json)
- [生产提示片段渲染](./implementation/IMP-12/hardening/node-status.png)：NoHit / Skipped 不出现性能警告，Hit / Failed 保留警告。
- [Release CLI 实测](./implementation/IMP-12/hardening/smoke-verification.json)：串行样例 Hit 0 / NoHit 31 / Skipped 3 / Failed 0；低效并行样例 Hit 1 / NoHit 32 / Skipped 1 / Failed 0，保留 RULE_010 的 Info 结果。两者 Passed、退出 0、HasMissingEvidence=false；[JUnit](./implementation/IMP-12/hardening/parallel-branch.junit.xml)保留结果编号和来源。样例为人工构造，仅验证报告契约。

WPF 验证在 STA 中加载实际 XAML 的状态和警告区域、检查绑定及可见性并渲染，不等同于完整窗口交互验收。没有连接 SQL Server 执行查询，没有生成覆盖率报告。

复验：

```powershell
$env:SQLXML_IMP12_VISUAL_OUTPUT = Join-Path $PWD 'DOCS/implementation/IMP-12/hardening'
.\DOCS\implementation\IMP-12\Verify-IMP12.ps1
.\DOCS\implementation\IMP-12\hardening\Verify-HardeningSmoke.ps1
```

## 检索依据

遵循 Microsoft 官方资料优先。网页搜索接口连接失败后，直接通过 HTTPS 读取下列官方正文，2026-09-09 均返回 HTTP 200。本轮涉及的运行事实和平台约束已有官方依据，未用社区结论代替依据；GitHub 插件在当前会话提供技能说明，但没有可调用的连接器工具。

1. [显示实际执行计划](https://learn.microsoft.com/en-us/sql/relational-databases/performance/display-an-actual-execution-plan?view=sql-server-ver17)：实际计划包含运行信息，运行时警告是否存在另行判断，支持把运行状态与性能警告分开展示。
2. [查询处理体系结构指南](https://learn.microsoft.com/en-us/sql/relational-databases/query-processing-architecture-guide?view=sql-server-ver17)：说明串行和并行执行、worker 数量及低行数下的串行执行选择。本项目据此结合已有线程事实模型区分“不适用”与“缺证据”；四状态和原因码属于项目协议。
3. [.NET 库的兼容性变更](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/breaking-changes)：支持保留既有消费者契约。结果分支与执行来源的具体映射属于本项目设计，以上表格以已有内置规则实现为依据。
4. [MiniDumpWriteDump](https://learn.microsoft.com/en-us/windows/win32/api/minidumpapiset/nf-minidumpapiset-minidumpwritedump)：优先从外部进程捕获；进程内方案需要专用线程并同步 DbgHelp 调用。本次沿用现有捕获机制及失败反馈，不改变为外部进程。
