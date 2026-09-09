# IMP-14：审查修复与加固

日期：2026-09-09。修复本轮审查确认的 P2：独立 `RelOp` 的原生诊断转成兼容结果后丢失 NodeId。原实现与规则阈值见 [IMP-14](IMP-14基数与残差谓词规则修复.md)。

## 问题、修复与边界

输入独立的 `<RelOp NodeId="42" …>`，无论它是 XDocument 的根节点，还是调用 Remove() 后的游离节点，都缺少完整 ShowPlan 的语句/算子身份。直接调用四条规则的 Analyze() 返回 NodeId=42；通过 RuleEngine.AnalyzeNode() 调用，原 ToLegacyResults() 只尝试完整位置和 LegacyResult，返回空编号。RULE_007 函数提示分支也受影响。

修复前新增的 10 个回归用例全部失败，修复后全部通过。处理集中在诊断协议和执行边界，所有原生规则都可复用：

- RuleAnalysisContext.SourceNodeId 在执行前保存局部编号快照；新规则无需访问可变 XML。原始 XML 后续变化不会改变已经发布的编号。
- PlanDiagnostic.NodeId 优先采用完整位置中的编号，其次是算子范围的源编号，最后保留旧适配器显式提供的兼容值。诊断合并保留该编号；缺失编号保持 null，兼容输出为空字符串，合法的 `"0"` 不被当作缺失。
- RuleRun.NodeId 为算子调用保存编号，覆盖 Hit、NoHit、Skipped、Failed，包括以异常表达的跳过。计划/语句调用不借用首个算子编号；诊断 Scope 和调用 Scope 分别处理。
- ToLegacyResults()、JSON 和文本报告均保留编号。没有完整位置时，文本标注“局部 NodeId”；Failed 的兼容结果也可定位到原节点。
- 保持完整 Location、DiagnosticId 和去重算法；NodeId 是局部编号，不能单独用于跨语句定位。不同语句中的 NodeId=0 仍是不同算子；不为片段伪造 Batch、Statement 或 Operator 身份。

协议 v1.0 的 PlanDiagnostic、RuleRun 新增可空 NodeId 字段；消费者须允许新增 JSON 字段，并继续使用 Location 作完整身份。RuleRun 的位置构造参数和解构签名保持原样。四条规则仍为 2.0.0，未调整规则阈值、严重度或配置项。

## 异常、DUMP 与日志

未知异常继续由统一执行边界记录 Failed，并通过既有诊断器生成、校验 Windows minidump 和 exception.json。同一异常实例只捕获一次；生成失败时保留原异常和捕获失败信息。预期 I/O 错误不请求 DUMP，请求的取消向上传播。新增断言确认四条实际内置规则的未知故障结果和兼容结果都保留原 NodeId，其他规则继续运行。

日志契约不变：Debug 为 DEBUG / WARN / ERROR / CRITICAL，Release 仅 ERROR / CRITICAL。局部 NodeId 加入诊断结果和报告，不加入规则运行日志。既有测试同时核验日志文件、stderr、stdout 和 SQL/对象名不泄漏的约束；测试生成的 DUMP 留在私有临时目录并在结束后清理。

## 验证记录

新增 DetachedNodeIdentityTests 共 **26 项**：四条规则和 RULE_007 分支、独立/游离节点、计划/节点入口、两组语义合并及反向注册顺序、JSON/文本/兼容输出、null/空/零编号、快照与重新分析、Scope 覆盖、跳过及失败结果。增强原测试对真实 DUMP 失败路径和多语句重复编号的断言。

| 验证 | Debug | Release |
| --- | --- | --- |
| 完整解决方案构建 | 0 警告、0 错误 | 0 警告、0 错误 |
| 全量 xUnit | 1684 通过、0 失败、0 跳过 | 1684 通过、0 失败、0 跳过 |
| IMP-14 含本轮加固 | 105/105 | 105/105 |
| 四条内置规则未知故障、真实 DUMP 校验 | 4/4 | 4/4 |
| CLI 多语句夹具 | 4 条相关诊断、8 次调用保留 NodeId=0，2 个独立语句 | 同 Debug |

CLI 的退出码均为预期的 1（夹具命中 Critical），HasFailures=false。全量测试已包含分模式日志与 DUMP 写入失败测试。构建、测试摘要和源码哈希见 [验证清单](verification/IMP-14-hardening/validation.json)；[Debug 构建](verification/IMP-14-hardening/build-debug.log)、[Debug 测试](verification/IMP-14-hardening/test-debug.log)、[Release 构建](verification/IMP-14-hardening/build-release.log)、[Release 测试](verification/IMP-14-hardening/test-release.log)、[CLI 摘录](verification/IMP-14-hardening/cli-release-excerpt.json)。原 IMP-14 验证目录保留初版的 1658 项历史记录。

复现命令：

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build
dotnet build SqlXmlAnalyzer.sln -c Release
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release --no-build
dotnet run --project SqlXmlAnalyzer.CLI -c Release --no-build -- scan --path SqlXmlAnalyzer.Tests/TestData/imp14_cardinality_residual.sqlplan --format json
```

## 检索依据

按指定优先级先核对 Microsoft 官方资料，再核对公开源码；以下正文/源码于 2026-09-09 通过只读 HTTPS 获取。

1. [Microsoft Learn：XObject.Document](https://learn.microsoft.com/en-us/dotnet/api/system.xml.linq.xobject.document?view=net-8.0) 给出可空 XDocument 属性；[.NET 8 官方实现](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Private.Xml.Linq/src/System/Xml/Linq/XObject.cs) 沿父链寻找文档，顶层不是 XDocument 时返回 null。这支持“节点编号不能依赖所属文档存在”的修复依据。
2. [Microsoft Learn：sys.dm_exec_query_profiles](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-views/sys-dm-exec-query-profiles-transact-sql?view=sql-server-ver17) 将 node_id 定义为查询树中的算子节点，并区分同节点的多个执行线程。局部编号与完整计划身份应分别处理；这是本项目据此作出的设计选择，不是 Microsoft 规定的诊断协议。
3. GitHub 的 [html-query-plan/node.ts](https://github.com/JustinPealing/html-query-plan/blob/master/src/node.ts) 先选择 StatementId 对应语句，再在其 RelOp 中匹配 NodeId；[qp.xslt](https://github.com/JustinPealing/html-query-plan/blob/master/src/qp.xslt) 分别保留 statement-id 和 node-id。作为公开计划可视化实现的交叉参照，本次未复制其代码或增加依赖。

网络检索工具连接失败，当前 GitHub 插件没有可调用连接器，因此通过 GitHub 公共 API 定位文件并读取 raw 源码。CSS 定向检索未获得可核验的同类案例，未将无关结果作为依据；没有用社区意见替代可获得的官方资料。本次未连接 SQL Server，也未执行完整 WPF/SSMS 人工验收；这些仍按后续验收步骤推进。
