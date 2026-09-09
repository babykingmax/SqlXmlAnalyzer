# IMP-19：SQL 语义验证与可靠应用

**联合审查更新（2026-09-09）：** 后续五项问题已修复，包括 UTF-16 保真、sql_variant 明确拒绝、有界读取前置、建库确认失败后的清理和真实应用状态。新增 47 项回归，Debug/Release 全量各 2229 项通过。当前支持范围和证据以 [联合审查修复说明](IMP-18-19联合审查修复与加固说明.md)为准，下文保留各阶段历史记录。

**审查修复更新（2026-09-09）：** 三项审查问题已修复：观察器只读隔离、辅助查询对会话状态的污染及 -1 受影响行数累计。新增 41 项回归，Debug/Release 全量各 2182 通过、构建各 0 警告/错误；两版本数据库每配置新增 26 项检查通过。详见 [修复、迁移及最新证据](IMP-19审查修复与加固说明.md)。本文原 58/2141 和初版运行附件保留为历史记录。

日期：2026-09-09。状态：本步实现、单元测试、真实 SQL Server 场景比较、CLI 写回及 WPF 窗口验证完成。新增 58 项回归；Debug/Release 全量各 2141 项通过，完整构建各 0 警告、0 错误。有限场景证据不构成任意输入的等价证明，不据此关闭全部 R02/R19/R20 或 IMP-21 工作台需求。

## 交付行为

IMP-18 继续生成默认未选择的提案，`refactor` 继续只输出审核预览。IMP-19 新增独立的“验证 → 审核场景 → 备份并应用”流程；只有所选组合通过本次隔离数据库验证，且源文件、提案、规则版本、选择和预览均未变化时，才允许写入。保存的是 `Review.PreviewSql`，不是旧兼容字段中的全部候选。

| 结果 | 含义及应用状态 |
| --- | --- |
| `PassedForScenarios` | 所列场景的观测匹配且没有执行错误、基础设施错误或清理失败；可进入审核应用准备 |
| `Different` | 至少一项结果、错误、事务或对象观测不同；禁止应用 |
| `Inconclusive` | 例如双方都产生相同 SQL 错误；相同失败不能作为可应用证据 |
| `Failed` / `Canceled` | 输入、连接、预算、执行基础设施失败或取消；禁止应用 |

`RewriteReview.CanApply=false` 仍表示静态提案本身不能授权写入。`SqlSemanticReport.CanPrepareApply` 仅描述本次场景证据；`semantic-compare` 即使得到该值也不生成应用凭据。应用服务在进程内签发不可从 JSON 恢复、只能使用一次的 `PreparedSqlRewrite`，桌面还要求验证完成后显式勾选场景审核确认。

## 隔离与支持范围

只连接命名为 `SqlXmlAnalyzer_*` 的专用 Windows LocalDB，不接受任意服务器或生产数据库连接串。需要安装 SQL Server 2019 或 2025 LocalDB；本次实测环境如下，版本信息及清理结果见 [server-cleanup.json](verification/IMP-19/server-cleanup.json)。

| 实例 | ProductVersion | 验证兼容级别 |
| --- | --- | --- |
| `SqlXmlAnalyzer_IMP19_2019` | `15.0.4382.1` | 150 |
| `SqlXmlAnalyzer_IMP19_2025` | `17.0.4075.5` | 170 |

在已安装对应 LocalDB 版本的机器上准备专用实例：

```powershell
SqlLocalDB versions
SqlLocalDB create SqlXmlAnalyzer_IMP19_2019 15.0 -s
SqlLocalDB create SqlXmlAnalyzer_IMP19_2025 17.0 -s
```

已有同名专用实例时使用 `SqlLocalDB start <实例名>`。本次保留上述两个实例供重跑，未修改既有 `MSSQLLocalDB`、`ProjectModels` 或默认 SQL Server 实例。比较器接受主版本 15/17，兼容级别 150/170 且不能高于服务器版本；排序规则仅接受 `Latin1_General_100_CI_AS_SC` 和 `Latin1_General_100_CS_AS_SC`。语法入口仍使用仓库的 ScriptDom TSql160 支持范围，不因此声称支持全部 SQL Server 2025 新语法。

每个场景的原 SQL 与候选 SQL 各使用一个新建的 `SxaSemantic_<GUID>` 数据库。建库后显式关闭 `TRUSTWORTHY`、`DB_CHAINING`；受测 SQL 在本库无登录用户下 `EXECUTE AS USER ... WITH NO REVERT` 执行，连接池关闭。建库发出前即进入受保护生命周期；关闭受限连接后在 `finally` 通过独立连接检查并清理本轮精确库名，即使建库确认失败或取消也执行清理。清理有独立 30 秒期限，失败写入报告并阻止应用。隔离不依赖一个最终 ROLLBACK，因此不会把表变量、提交事务和 DDL 的副作用误当作已经撤销。

SQL、SetupSql 和 ObserveSql 都经过有界 ScriptDom 解析。受测 SQL 和 SetupSql 的白名单支持 SELECT、局部变量/表变量、CREATE/DROP TABLE、INSERT/UPDATE/DELETE、局部赋值、显式事务/保存点、IF/WHILE、TRY/CATCH、THROW/RAISERROR、PRINT。ObserveSql 另行限制为只读 SELECT，不能写入、SELECT INTO、嵌入 DML、变量赋值、控制事务或执行清理。所有入口拒绝动态执行、模块定义、USE、权限与实例设置、跨库/跨服务器名称、全局临时表、外部数据源/OPENROWSET/OPENQUERY 和序列取值等不支持形状。该限制是验证器边界，不能据此推断未支持 SQL 不合法。

固定会话设置完整写入报告：ANSI_NULLS、QUOTED_IDENTIFIER、ANSI_WARNINGS、ANSI_PADDING、CONCAT_NULL_YIELDS_NULL、ARITHABORT 为 ON；NUMERIC_ROUNDABORT、XACT_ABORT、NOCOUNT 为 OFF；语言 `us_english`、DATEFORMAT `ymd`、DATEFIRST 7、隔离级别 READ COMMITTED、LOCK_TIMEOUT 5000。结果不能推广到其他会话设置或工作负载。

## 场景与比较口径

场景 JSON 字段匹配不区分大小写，未知字段、空集合或重复场景名称均拒绝。`SetupSql` 在每侧建立相同夹具，受测 SQL 执行后立即运行只读 `ObserveSql`，再执行内部状态查询与对象快照。需要瞬时会话值时在观察器第一条查询读取；观察器自己的后续语句仍遵循 SQL Server 的状态变化。可参考 [完整示例场景](verification/IMP-19/example-suite.json)及[原 SQL](verification/IMP-19/example-original.sql)。

```json
{
  "InstanceName": "SqlXmlAnalyzer_IMP19_2025",
  "CompatibilityLevel": 170,
  "Collation": "Latin1_General_100_CI_AS_SC",
  "Scenarios": [
    {
      "Name": "bounded-int-null-threshold",
      "SetupSql": "CREATE TABLE dbo.Customers(Id int, Age int NULL CHECK(Age BETWEEN -100000 AND 100000)); INSERT dbo.Customers VALUES(1,-100000),(2,40),(3,41),(4,100000),(5,NULL);",
      "ObserveSql": "SELECT Id, Age FROM dbo.Customers;",
      "OrderedResults": false
    }
  ]
}
```

比较内容包括每个结果集的列名、类型、长度、精度/小数位、可空性，带类型的值、重复行数量、SQL 错误编号序列、受影响行数，以及只读观察器之后采集的 `@@TRANCOUNT`、`XACT_STATE()`、`@@OPTIONS`。观察器成功时不会提交、回滚或修改这些设置；观察器错误会阻止应用。RecordsAffected 只累计非负 DML 计数，-1 表示不适用、0 表示零行 DML，非法/溢出计数失败。默认按多重集合比较行；有顺序要求时设置 `OrderedResults=true`，并在 SQL 中提供明确排序。NULL 与空字符串分开；二进制、日期和高精度 `decimal(38,...)` 使用无区域性损失的表示，不通过字符串拼接或去重判断等价。

对象观测包含用户表的列元数据和数据，以及输入中出现的局部临时表的存在性、结果列模式和数据；附加状态由 `ObserveSql` 明确查询。它不穷尽所有索引、约束、默认表达式、对象生命周期或中间控制流的等价性，亦不比较耗时、锁竞争、执行计划及所有消息文本。观测不足时应扩充场景，不能把未观察到差异当作证明。

预算：场景文件最多 1 MiB、1–32 个场景；每份 SQL 最多 256 Ki UTF-16 字符、16 批次。SQL 文件先以 1 MiB + 4 字节为上限读取（兼容 UTF-32/BOM），严格解码并检查字符上限后才执行 AST 和规则；验证前后复查及写回校验同样有界。单次执行最多 16 个结果集、每集 128 列，用户表最多 64 张；整个验证共享 10,000 行和 4 MiB 内容预算；每个文本/二进制单元最多 65,536 字符/字节。超限失败而不截断后判为匹配。连接超时 10 秒、命令超时 15 秒、整轮执行有 3 分钟取消期限，之后仍须进行有独立期限的清理；关闭窗口或 CLI 取消会传播到验证。

字符串结果包含未配对 UTF-16 代理码元时，使用 `System.String.UTF16LE` 标签和原始小端码元的 Base64，避免不同值被 JSON 合并成替代字符；普通 Unicode 仍保持可读表示。结果列为 `sql_variant` 时因无法完整比较内部类型属性而返回 `UnsupportedSemanticType`，包括空/全 NULL 结果、观察器和对象快照，均不能取得应用凭据。

## CLI 操作

先在仓库根目录构建 Release，并将示例复制到待审核文件；以下 `dotnet run --no-build` 使用当前已构建程序集。

```powershell
dotnet build SqlXmlAnalyzer.sln -c Release
Copy-Item DOCS/verification/IMP-19/example-original.sql query.sql
Copy-Item DOCS/verification/IMP-19/example-suite.json suite.json
dotnet run --no-build -c Release --project SqlXmlAnalyzer.CLI -- refactor query.sql --format json --show-sql --output proposals.json
```

阅读 `proposals.json` 中 `Review.Proposals` 的规则、前提、diff 和 ID，将要审核的 ID 替换到下列命令。`--select` 可重复，前序依赖规则与 IMP-18 一致。验证命令不写 SQL 文件：

```powershell
dotnet run --no-build -c Release --project SqlXmlAnalyzer.CLI -- rewrite-validate query.sql --select <提案ID> --scenarios suite.json --show-sql --output validated.json
```

审核 `PreviewSql`、每项场景观测及限制；要求 `Validation.Status=PassedForScenarios`，再从本报告取得 `SourceHash`、`PreviewHash`、`Validation.SuiteHash`，显式发起应用：

```powershell
dotnet run --no-build -c Release --project SqlXmlAnalyzer.CLI -- rewrite-apply query.sql --select <同一提案ID> --scenarios suite.json --review-source-hash <SourceHash> --review-preview-hash <PreviewHash> --review-suite-hash <Validation.SuiteHash> --acknowledge-scenarios --output applied.json
```

三个 hash 绑定审核过的 SQL 文本、选定预览和结构化场景配置。文件原始字节 hash 由应用服务另外校验，不能拿 `Get-FileHash` 的字节摘要替代文本 hash。`rewrite-apply` 每次都重新生成提案并执行真实数据库验证，不接受旧 JSON 报告作为凭据。提案生成只使用新命令自身的规则上下文；需要执行计划才能生成的提案不能凭旧 ID 强行应用。

只比较任意两份 SQL、调查反例时使用：

```powershell
dotnet run --no-build -c Release --project SqlXmlAnalyzer.CLI -- semantic-compare original.sql --candidate candidate.sql --scenarios suite.json --show-sql --output comparison.json
```

该命令始终 `SourceWritten=false`。新命令退出码为成功 0、验证/应用失败 1、参数错误 2、取消 130；预期得到 `Different` 的反例退出 1 是预期行为。报告不能与任何输入文件同路径或别名；验证前后均重查。完整成功输出见 [validated.json](verification/IMP-19/apply-release/validated.json) 和 [applied.json](verification/IMP-19/apply-release/applied.json)。

## 桌面审核与可靠写回

打开“SQL 改写提案审核”窗口，选择候选并检查左右 SQL。点击“验证所选 SQL”，依次选择与窗口原 SQL **完整文本一致**的源文件和场景 JSON；仅有计划中 SQL 时，需要先提供该文本对应的文件。查看下方验证报告，验证通过后勾选“已审核所列场景；测试通过不代表任意输入等价”，再点击“备份并应用”。选择变化会废弃凭据并清除确认；提前勾选、验证期间改变选择、源文件过期或取消都不能启用应用。

应用前重新审核完整提案集合、版本、依赖、所选组合及原文件字节，然后复用 IMP-03 `SqlWritebackService`：校验原文件 → 生成并验证备份 → 写同目录临时文件并校验 → 提交前再次检查源文件 → 替换目标。保留原编码/BOM，只保存已选预览。备份失败、并发变化或前置校验失败均停止写入；一次凭据不能重试提交。

结果保留 `SourceWritten`、`CommitOutcomeUnknown`、阶段、原文/输出字节 hash、备份与临时路径。提交后丢失成功确认时，不伪报“未写入”；先核对实际文件与备份，按 [IMP-03 恢复说明](IMP-03安全写回与恢复说明.md)恢复。若应用完成后仅报告文件输出失败，CLI 在 stdout 补发 `ReportOutputFailed=true` 及实际提交结果，退出 1；不能因非零退出码盲目重新应用。成功写入或提交结果未知后，桌面禁用再次应用，需重新打开并审核当前文件。

已检查生产窗口的[未选择](verification/IMP-19/wpf-Release-4085ba8802044ac792382c94ceac67ee/review-unselected.png)、[已选择](verification/IMP-19/wpf-Release-4085ba8802044ac792382c94ceac67ee/review-selected.png)、[验证并确认](verification/IMP-19/wpf-Release-4085ba8802044ac792382c94ceac67ee/review-validated.png)、[写入后](verification/IMP-19/wpf-Release-4085ba8802044ac792382c94ceac67ee/review-applied.png)四张截图。探针显示实际窗口并驱动生产 ViewModel，执行真实 LocalDB 验证和文件应用，检查备份及零绑定错误；文件选择器的人工鼠标操作不在本次自动化验证范围内。

## 异常、DUMP 与日志

验证器、准备/应用服务、CLI 和桌面边界使用既有 `ExceptionPolicy`。取消、输入/文件错误和 SQL Server 可预期错误给出失败结果；未知异常生成原生 `.dmp` 和异常侧车记录，默认在 `%LOCALAPPDATA%\SqlXmlAnalyzer\dumps`。DUMP 捕获失败仍保留原异常与捕获失败状态，不覆盖实际文件提交结果；同一异常重复报告有去重保护。

日志按编译模式固定：Debug 为 DEBUG/WARN/ERROR/CRITICAL（调试、警告、错误、致命），Release 仅 ERROR/CRITICAL；运行参数不能在 Release 打开调试或警告日志。文件日志默认在 `%LOCALAPPDATA%\SqlXmlAnalyzer\log`，CLI 诊断走 stderr、JSON 走 stdout。常规新增日志记录阶段、计数和 SQL 错误编号，避免记录 SQL 和对象名。业务场景差异和风险提示属于结果内容，在 Release 仍然可见。

验证报告含原始值、对象模式或本地路径；`--show-sql` 仅控制 SQL 文本展示，不会将观测或 DUMP 脱敏。输出携带原始资料隐私提示，不应作为脱敏报告直接共享。真实 DUMP、捕获失败、损坏 reporter、已知异常不产生 DUMP 和分配置日志均有回归测试；实现期间还实际捕获了事务状态读取类型错误并修复，见 [故障记录](verification/IMP-19/initial-fault-dump.json)。

## 验证证据

| 验证 | 结果与附件 |
| --- | --- |
| 全量构建 | Debug/Release 各 0 警告、0 错误：[Debug](verification/IMP-19/build-debug.log)、[Release](verification/IMP-19/build-release.log) |
| 全量单元测试 | 各 2141 通过、0 失败、0 跳过，较 IMP-18 新增 58 项：[Debug](verification/IMP-19/test-debug.log)、[Release](verification/IMP-19/test-release.log) |
| SQL Server 语义回归 | 21 个场景 × 2 个版本，每配置 42/42，共 84 次真实比较：[Debug](verification/IMP-19/database-debug/summary.json)、[Release](verification/IMP-19/database-release/summary.json) |
| CLI 实际应用 | 两配置均验证原文件保留、只保存所选项、BOM/备份一致及报告失败后的真实提交状态：[Debug](verification/IMP-19/apply-debug/summary.json)、[Release](verification/IMP-19/apply-release/summary.json) |
| WPF 实际窗口 | 原文恢复、真实验证、确认门控、准确写入/备份、四张截图、零绑定错误：[运行日志](verification/IMP-19/wpf.log)、[应用结果](verification/IMP-19/wpf-Release-4085ba8802044ac792382c94ceac67ee/application.txt) |
| 数据库清理 | 最终两实例剩余验证数据库均为 0：[版本与清理](verification/IMP-19/server-cleanup.json) |

新增单测覆盖沙盒逃逸形状、批次/类型/NULL/重复行/顺序、流读取预算、源文件与选择过期、场景证据不完整或 hash 不匹配、提前确认、备份失败、提交并发变化、提交确认丢失、一次性凭据、CLI 参数与路径别名、未知错误 DUMP 和日志。

数据库场景覆盖 LTRIM 的 varchar/nvarchar/char/nchar、前导空格、NULL、尾部空格、TRIM、Unicode 前缀与大小写排序；表变量的提交、回滚、嵌套事务、保存点、TRY/CATCH、事务内声明、既有名称碰撞、多声明；另有有界算术、整数溢出、重复行、相同错误及 decimal(38)/二进制控制组。**反例的 `Passed=true` 表示实际发现了预期差异，不表示改写等价。** 提交/清理控制组匹配不能启用表变量转换；TRIM 和表变量规则仍维持 IMP-04/18 的限制。

在已有专用实例及完成对应配置构建后重跑：

```powershell
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release --no-build
./DOCS/verification/IMP-19/Run-Regression.ps1 -Configuration Debug
./DOCS/verification/IMP-19/Run-Regression.ps1 -Configuration Release
./DOCS/verification/IMP-19/Run-ApplyCheck.ps1 -Configuration Debug
./DOCS/verification/IMP-19/Run-ApplyCheck.ps1 -Configuration Release
./DOCS/verification/IMP-19/verify-wpf.ps1
```

脚本使用合成数据，写入本步骤的验证输出目录；WPF 探针为每次运行创建独立输出目录。没有生成覆盖率报告，不声称覆盖率提高。完整 SQL Server 版本矩阵、生产工作负载与性能收益不在本次验证结论内。

## 检索依据

按 Microsoft 官方资料优先核对，再以实际数据库结果验证适用范围；本步不需要以社区推测替代官方语义。MicrosoftDocs GitHub 源文件用于读取与核对官方文档。

- [Microsoft LTRIM](https://learn.microsoft.com/en-us/sql/t-sql/functions/ltrim-transact-sql?view=sql-server-ver17)及[官方源文档](https://github.com/MicrosoftDocs/sql-docs/blob/live/docs/t-sql/functions/ltrim-transact-sql.md)：函数处理前导字符，NULL 行为必须保留；据此保留前导空格反例，不默认删除函数。
- [Microsoft table 类型](https://learn.microsoft.com/en-us/sql/t-sql/data-types/table-transact-sql?view=sql-server-ver17)：表变量与普通事务回滚的行为不同；据此分别执行提交、完整回滚与局部回滚场景。
- [Microsoft SAVE TRANSACTION](https://learn.microsoft.com/en-us/sql/t-sql/language-elements/save-transaction-transact-sql?view=sql-server-ver17)：保存点和嵌套事务须分别观察，不能仅用单个最终回滚验证替换。
- [Microsoft EXECUTE AS](https://learn.microsoft.com/en-us/sql/t-sql/statements/execute-as-transact-sql?view=sql-server-ver17)：`WITH NO REVERT` 约束模拟上下文的恢复；结合无登录本库用户、禁用跨库信任和不复用连接执行场景。
- [Microsoft XACT_STATE 官方源文档](https://github.com/MicrosoftDocs/sql-docs/blob/live/docs/t-sql/functions/xact-state-transact-sql.md)：返回 smallint，与事务嵌套计数表达不同事实；实际读取显式转 int，报告同时保留两者。
- [Microsoft SqlScriptDOM AST](https://github.com/microsoft/SqlScriptDOM/blob/b583737682dbb1682b97d0ac5a5261dc94279b8d/SqlScriptDom/Parser/TSql/Ast.xml)：核对外部数据访问与语句节点；解析成功只是白名单检查的输入。连接驱动使用 `Microsoft.Data.SqlClient 6.1.6`，版本核对自[官方 NuGet 发布索引](https://api.nuget.org/v3-flatcontainer/microsoft.data.sqlclient/index.json)。
