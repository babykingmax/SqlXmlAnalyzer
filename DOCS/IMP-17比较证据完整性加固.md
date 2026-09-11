# IMP-17 比较证据完整性加固

日期：2026-09-09。针对 IMP-14–IMP-17 联合审查确认的三项 IMP-17 缺陷，修复残差遗漏、排序身份不完整和未知对象被当作相同对象的问题。比较模型升级为 `plan-comparison/1.2.0`，会话格式仍为 `2.1`。

## 修复行为

| 问题 | 修复与边界 |
| --- | --- |
| `BuildResidual` 没有进入谓词签名 | 与 `ProbeResidual` 等谓词分别保留角色和有序表达式；不同列、常量或角色不能产生 High 算子差值。空谓词、缺少操作数/列/常量、未知比较操作及外部命名空间均不能作为完整证据。保留合法的一元 `IS NULL`、`IS NOT NULL` 和二元比较。 |
| Sort 只按类型、对象和子树匹配 | 新增 `PlanComparisonSortEvidence`，保留有序排序列、方向、`Distinct` 和可选分区列；TopSort 还保留 `Rows`、`WithTies`。缺少或非法必要字段时最多为 Candidate；不同排序语义不产生 High 算子差值。XML 布尔值 `1/true`、`0/false` 归一化，属性顺序不影响签名，排序列顺序仍影响签名。 |
| 双方对象身份都缺失却通过可比性检查 | 新增 `PlanComparisonObjectEvidence`，访问对象须有已知的数据库、schema、对象名；常见基表访问/写入算子缺少 Object 也视为证据缺失。自动配对降为候选；即使人工选定双方，也不能绕过对象完整性检查生成差值。 |

对象身份不完整时，本侧成本、运行指标继续显示，估算及运行差值为 N/A，原因包含“捕获对象身份不完整”。常量扫描及不引用持久对象的算子不被强制要求 Object。本地对象允许未记录 Server；捕获到的 Server 值仍参与精确比较，不把不同服务器或单侧缺少记录视为一致。未记录的服务器环境、硬件和工作负载仍不能由模型验证。

结构证据缺失沿子树向父算子传播，并参与多个 QueryPlan 的匹配门槛。健康的独立子算子仍可依据自己的完整证据匹配；存在未知对象的 QueryPlan 整体停止数值比较。结构签名计算在副本上归一化，不改动源 XML 或使源身份缓存失效。

`ComparisonQuery.IncompletePredicateOperators` 继续表达原有谓词证据状态；新增 `IncompleteStructureOperators` 包含谓词、排序、对象及子树证据状态。`HasCompleteObjectIdentity` 用于语句自动配对和采集条件评估。旧会话载入后使用当前模型重新比较，无需迁移会话文件。

本次仍是离线启发式匹配，不是完整 Showplan XSD 校验、SQL 等价证明或性能实验。其他算子载荷、计算列来源和别名重写等并未由本次修复获得完整语义验证；不能将 High 标签理解为 SQL 等价保证。未新增诊断规则 ID 或配置项。

## 异常与日志

继续通过 `PlanComparisonController.BuildComparison` 的 `ExceptionPolicy` 入口处理异常。未知错误由生产 `UnexpectedErrorReporter` 调用 `WindowsMiniDumpWriter` 生成 minidump 和 `exception.json`，DUMP 写入失败有明确失败记录；比较失败不会返回部分成功结果。取消、陈旧选择和预期输入错误沿用已知错误处理。证据缺失本身是可展示状态，不制造未知错误 DUMP。

新增结构不足情况会输出固定警告，不写入 SQL、参数或对象名。Debug 使用 DEBUG/WARN/ERROR/CRITICAL；Release 仅 ERROR/CRITICAL，CRITICAL 为致命级别。全量测试包含真实 minidump 校验、DUMP 写入失败、异常去重，以及日志文件和标准错误的两种构建模式断言。

## 验证结果

新增 [PlanComparisonEvidenceHardeningTests](../SqlXmlAnalyzer.Tests/PlanComparisonEvidenceHardeningTests.cs) **55 项**。先运行最初 44 项反例，修复前 **36 失败、8 通过**；随后补齐父子树传播、多 QueryPlan、合法一元比较、源 XML 不变性及未知异常边界等回归。

既有测试中的无对象 `SELECT 1` 夹具改用 Constant Scan，保留原断言；需要访问表的夹具仍显式提供完整 Object。新增反例另外覆盖缺少 Object 的 Table Scan，避免通过调整夹具掩盖缺陷。

| 配置 | 完整解决方案构建 | 全量单元测试 |
| --- | --- | --- |
| Debug | 0 警告、0 错误 | 2012 通过、0 失败、0 跳过 |
| Release | 0 警告、0 错误 | 2012 通过、0 失败、0 跳过 |

证据见[验证清单](verification/IMP-17-evidence-hardening/validation.json)、[Debug TRX](verification/IMP-17-evidence-hardening/full-debug.trx)、[Release TRX](verification/IMP-17-evidence-hardening/full-release.trx)及[修复前反例](verification/IMP-17-evidence-hardening/red-debug.trx)。本轮验证范围为源码、构建和单元测试；GUI 截图及独立 Probe.exe 故障进程证据保留在[上一轮记录](IMP-17审查修复与加固说明.md)，没有将历史进程验证计作本轮运行。

复现命令：

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug --disable-build-servers -m:1
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build
dotnet build SqlXmlAnalyzer.sln -c Release --disable-build-servers -m:1
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release --no-build
```

## 检索依据

按 Microsoft 官方资料优先核对。网页搜索服务连接失败，GitHub 插件无可调用连接器，改用只读 HTTPS 获取官方页面及 Microsoft 公开源码。三份来源成功响应及内容 SHA-256 记录在[research.json](verification/IMP-17-evidence-hardening/research.json)；之后一次 GitHub 重读发生 TLS EOF，不影响此前成功取得的记录。官方资料和仓库保存的官方 XSD 已能确定这三项字段契约，因此未声称穷尽 CSS 或社区资料。

- [Microsoft 算子参考](https://learn.microsoft.com/en-us/sql/relational-databases/showplan-logical-and-physical-operators-reference?view=sql-server-ver17)：Sort 根据 Order By 排序，Distinct Sort 同时去重，Top N Sort 还涉及保留行数。排序属性改变不能只凭相同算子名称认作相同操作。
- [Microsoft 执行计划比较说明](https://learn.microsoft.com/en-us/sql/relational-databases/performance/compare-execution-plans?view=sql-server-ver17)：SSMS 的数据库名忽略选项是显式比较策略；本工具继续保留捕获的数据库身份，不从两个缺失字段推导对象相同。
- [Microsoft Showplan XSD 的仓库副本](../SqlXmlAnalyzer.Tests/TestData/Schemas/showplanxml-sql2019.xsd)：HashType 定义 BuildResidual/ProbeResidual；SortType 要求 OrderBy/Distinct；OrderByColumn 要求 Ascending；TopSortType 要求 Rows；CompareType 允许一或两个 ScalarOperator。缓存文件哈希已写入研究记录。
- [Microsoft sqltoolsservice 的 SkeletonManager](https://github.com/microsoft/sqltoolsservice/blob/main/src/Microsoft.SqlTools.ServiceLayer/ExecutionPlan/ShowPlan/Comparison/SkeletonManager.cs)：以逻辑树结构组织计划比较的参考；本工具的完整性门槛和签名由本项目实现，不声称复现 SSMS 算法。
