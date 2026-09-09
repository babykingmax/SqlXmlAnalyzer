# IMP-15：索引目标身份与 DDL 标识符

2026-09-09 审查加固已完成：修复参数/内部列进入 DDL、Sort 取证失效，以及真实 ColumnReference 原始名称的误解码。本轮新增 46 项，Debug/Release 全量各 1766 项通过。当前列归属、名称及 Sort 契约以 [审查修复与加固说明](./IMP-15审查修复与加固说明.md) 为准；下文初版测试与实测计数保留为历史记录。

实施日期：2026-09-09。当前步骤完成；对应 R13、R14、D02、UI-09，依赖 IMP-10/12。修复 DDL 的名称拆分、数据库丢失、创建/回滚命名分离，以及 SQL 候选、评分、沙盒的表名首项关联。

## 标识符、目标与编译结果

`IndexDdlCompiler.QuoteIdentifier` 只引用一个原始标识符，`QualifyName` 接收独立名称段并拒绝中间缺段。兼容 `EscapeName` 接收单个原始或已分隔名称，不再按点号拆分。例如：

| 输入含义 | 输出 |
| --- | --- |
| 单个表名 Order.Detail | `[Order.Detail]` |
| 单个列名 R]ange | `[R]]ange]` |
| 数据库 db.one、schema sales、表 Order.Detail | `[db.one].[sales].[Order.Detail]` |
| 中文或带空格的名称 | 保持原字符后逐段引用 |

名称按完整分隔符规则解码，拒绝空名称、未闭合/未转义分隔符、NUL、超过 128 个 UTF-16 代码单元的名称。名称大小写保留；缺少数据库排序规则证据时不做大小写折叠。模型的 ObjectIdentity 是独立原始名称段，兼容字段与该身份冲突时报 `INDEX_TARGET_CONFLICT`，不默默覆盖目标。

已知数据库写入三段目标；只有 schema/表名时仍可生成两段目标，但附 `INDEX_DATABASE_UNSPECIFIED` 注释，要求在核验后的目标数据库执行。缺少 schema/表名则报 `INDEX_TARGET_UNRESOLVED`，不推断 dbo。SQL 候选可在所属语句中用唯一的捕获对象补齐缺失名称；不能唯一绑定时只输出未解析诊断。

CREATE INDEX 不支持四段远程目标。已提供 Server 时，创建与回滚脚本都先用 SERVERPROPERTY 校验当前服务器名，按二进制比较不一致则 THROW；DDL 仍使用合法的数据库/schema/表目标。没有 Server 的捕获对象只能在其所属文档与 QueryPlan 中关联，不据此判断跨服务器对象相同。

`Compile` 一次返回不可变 `CompiledIndexDdl(IndexName, Target, Create, Rollback)`；创建和回滚使用同一快照。确定性名称为 `IX_<最多24字短表名>_<64位SHA256十六进制摘要>`，总长度不超过 128；摘要包含版本、所有目标名称段、全部键/INCLUDE 名称、角色与顺序。第四个键、包含列或名称标点变化都会改变摘要，避免旧版仅截前三个键或删标点造成的可复现冲突。摘要不代替部署库的同名检查。

没有键列时创建、回滚都为空。重复列及键/INCLUDE 交叉重复、错误角色、非法选项被拒绝。DATA_COMPRESSION 限定为空或 NONE/ROW/PAGE，MAXDOP 为 0–64；选项不直接拼接任意文本。保持原默认 ONLINE=ON、PAGE、SORT_IN_TEMPDB=ON，适用版本、版本授权、列类型与运行条件仍须在部署前核验。

生成的创建和回滚均通过 ScriptDom TSql160Parser 语法检查后才返回。语法检查不等于实际对象/权限/数据类型/索引目录验证。旧版已部署索引应使用当时保存的回滚脚本；本版本的新名称用于新生成脚本。

## 提取、关联与证据

MissingIndex 仅从已识别 QueryPlan 下的 MissingIndexes/MissingIndexGroup 直接载荷提取，排除算子内误放、InternalInfo 和跨命名空间内容。每项保留完整对象与语句/QueryPlan 位置。键按 EQUALITY、INEQUALITY 分组，组内原顺序保留；INCLUDE 仍是单独角色且保持原次序。未知角色显式报错。

`IndexTargetResolver` 为 SQL 候选、评分和沙盒提供共同关联：

- 以捕获 QueryPlan 和 Server/Database/Schema/Object 名称段进行精确匹配，不再按 TableName 取首项。
- 只读取当前算子载荷，停止在子 RelOp、外部命名空间和 InternalInfo。沙盒可用列还核对列自身提供的对象信息。
- 同一对象在不同语句中互不借用候选、输出列、基数或行宽；沙盒重算复制 Database、Server、ObjectIdentity 和 Location，DDL 不再丢失数据库。
- 图上已有的精确身份关联继续保留；多个候选不能唯一关联时不随意选择第一项，全部候选仍在分析结果中。

SQL 推断保留数据库/schema/表的独立名称段。限定列按当前作用域解析；未限定列有多个或无法解析的来源时不归给第一个表。派生表、CTE 和被内层遮蔽的别名不会借用外层已知表身份。CTE 名称不会被当成可建索引的基表。列名使用严格引用，包含右方括号时也可生成合法 DDL。

本步没有读取生产数据库的完整索引目录，也没有把候选当成“已有索引”“重复索引”或“已被覆盖的索引”。沙盒的覆盖提示仅指已关联算子的候选列可能覆盖查询输出，继续标注为待验证假设；评分公式的校准与顺序不变性仍由 IMP-16 处理。

## 规则协议与兼容变化

RULE_020_MISSING_INDEX、RULE_035_SARGABLE_INDEX_RECOMMENDATION 的版本为 **2.0.0**，RuleId、严重度和配置项保持原值。

RULE020 的汇总标注各候选的完整目标与来源，明确 SQL Server Impact 和工具评分；计划汇总不再伪装成 NodeId=0。移除未核验的统一 INCLUDE 字节限制说明，提示核验列类型和版本条件。

RULE035 主诊断接口改为 **Statement 范围**，每个语句执行一次并保留全部候选。原生语义为 INDEX_SQL_CANDIDATE、INDEX_TARGET_UNRESOLVED、INDEX_NON_SARGABLE_EXPRESSION；证据包含目标、列角色和顺序，未解析目标不带可执行 DDL。相同 NodeId=0、不同语句中的相同对象都保持独立位置。规则的调用数/诊断数可能因此改变；节点入口只由该语句首个算子执行语句检查，其余为 RULE_OUTSIDE_SCOPE。旧 XML 单结果接口仍保留局部结果与汇总兼容入口。

规则执行的隔离 XML 副本在准备临时上下文后登记原文档 Envelope，使新结构化证据与原始文档拥有相同身份。源 XML 不被修改；其他协议状态、失败门禁和配置兼容性维持原约束。

部署包从一次编译结果生成创建和回滚部分。标题中的注释分隔符/换行被安全处理；回滚使用逐行注释，标识符中的 `*/` 不能提前结束注释并激活回滚。

## 异常、DUMP 与日志

编译器及 MissingIndex 提取入口通过既有 ExceptionPolicy 处理异常；规则仍有 RuleEngine 执行边界。非法目标/列/选项属于预期 InvalidDataException，保留明确错误；未知编译或语法验证异常记录原异常、生成 Windows minidump 与 exception.json 后重新抛出。规则失败表现为 Failed，不冒充 NoHit。相同异常实例只捕获一次，DUMP 生成失败时明确保留捕获失败说明和原异常。

| 构建 | 日志 |
| --- | --- |
| Debug | DEBUG、WARN、ERROR、CRITICAL（致命） |
| Release | ERROR、CRITICAL（致命） |

新增日志仅记录阶段、原因码、计数，不记录 SQL/DDL 或对象原值。日志写入文件和 stderr，CLI stdout 保持纯 JSON。预期输入错误与取消不请求 DUMP。独立故障测试在私有临时目录生成/验证 DUMP，结束后清理。

## 自动化与真实数据库验证

首批 8 个反例在修复前全部失败。新增 **36 项** xUnit：IndexDdlIdentityTests 20 项、IndexTargetIdentityTests 12 项、IndexDdlDiagnosticTests 4 项。包括 100 组确定性特殊标识符样本、DDL AST、命名差异/快照、分隔符与选项拒绝、部署注释、完整身份/多语句/作用域隔离、JSON、真实 DUMP、捕获失败和日志模式。调整原测试夹具补齐必要身份，保留原功能断言。

| 验证 | Debug | Release |
| --- | --- | --- |
| 完整解决方案构建 | 0 警告、0 错误 | 0 警告、0 错误 |
| 全量 xUnit | 1720 通过、0 失败、0 跳过 | 1720 通过、0 失败、0 跳过 |
| 本步新增 | 36/36 | 36/36 |
| CLI | 4 条已绑定语句候选、HasFailures=false | 同 Debug |

CLI 在合成夹具上均退出 0，仅表示默认严重度门禁未失败。夹具含 4 个语句，重复 StatementId 和 NodeId=0，覆盖 sales/audit 同名表、不同数据库、同表不同语句。没有把合成计划称为 SQL Server 实际生成的执行计划。

使用专用临时 **SQL Server LocalDB 17.0.4075.5** 实际验证 5 个目标：跨数据库、sales/audit 同名表、数据库及表名中的点号、右方括号、中文空格。连接在 master 发起 DDL，查询 sys.indexes/sys.index_columns/sys.columns 核对准确对象、等值键序号 1、不等值键序号 2 和 INCLUDE 标志；随后执行回滚并核验删除。每个目标还验证错误服务器名会在创建前被拒绝。实测选项为 ONLINE=OFF、ROW、SORT_IN_TEMPDB=OFF；不将此结果外推为默认在线选项适用于所有版本/授权。专用数据库与实例均已清理。

证据：[验证清单和源码哈希](verification/IMP-15/validation.json)、[LocalDB 实测](verification/IMP-15/localdb-results.json)、[可重复验证脚本](verification/IMP-15/verify-localdb.ps1)、[CLI 摘录](verification/IMP-15/cli-release-excerpt.json)、[Debug 构建](verification/IMP-15/build-debug.log)/[测试](verification/IMP-15/test-debug.log)、[Release 构建](verification/IMP-15/build-release.log)/[测试](verification/IMP-15/test-release.log)。

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build
dotnet build SqlXmlAnalyzer.sln -c Release
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release --no-build
dotnet run --project SqlXmlAnalyzer.CLI -c Release --no-build -- scan --path SqlXmlAnalyzer.Tests/TestData/imp15_index_targets.sqlplan --format json
# 在装有 SQLCMD 与 LocalDB 17.0 的 Windows 环境复现；脚本创建并清理专用实例。
./DOCS/verification/IMP-15/verify-localdb.ps1 -Configuration Release
```

本步未执行完整 WPF/SSMS 人工操作验收，保留后续综合验收安排。

## 官方依据

2026-09-09 核对 Microsoft Learn 正文。网络检索工具连接失败后，通过只读 HTTPS 取得页面：

- [QUOTENAME](https://learn.microsoft.com/en-us/sql/t-sql/functions/quotename-transact-sql?view=sql-server-ver17)：单个标识符的分隔/转义及 sysname 长度界限。本实现对无效输入返回明确错误，而非制造错误引用。
- [CREATE INDEX](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-index-transact-sql?view=sql-server-ver17)：目标的数据库/schema/对象名称段、索引选项和适用限制。服务器名不能直接成为 CREATE INDEX 的第四段。
- [sys.dm_db_missing_index_details](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-views/sys-dm-db-missing-index-details-transact-sql?view=sql-server-ver17)：等值键在不等值键之前，包含列单独进入 INCLUDE。组内排序保留捕获顺序；本工具未凭空推断选择性最优顺序。
