# IMP-19 代码审查

**修复更新（2026-09-09）：** 下述三项已由 [审查修复与加固](IMP-19审查修复与加固说明.md)处理，新增 41 项回归；Debug/Release 全量各 2182 通过、构建各 0 警告/错误。原始反例、错误输出及行号保留为修复前证据，当前实现与重跑结果见修复说明。

日期：2026-09-09。范围：SQL 语义验证、场景策略、比较与值读取、应用凭据、CLI/WPF 接入，以及既有写回和异常边界。未修改产品实现。本轮确认 3 项问题：2 项 P1、1 项 P2；其中 2 项通过实际 `rewrite-validate` 得到不应允许的 `CanApply=true`。

## P1：观测脚本可在对象快照前擦除真实差异

位置：`src/SqlXmlAnalyzer.Application/Services/LocalDbSqlSemanticRunner.cs:120–121`；场景策略在 `SqlSemanticSandboxPolicy.cs:43–44` 对 SetupSql 与 ObserveSql 使用同一允许写入的白名单。

`ObserveSql` 可以执行 DELETE、UPDATE、DROP 或事务操作，且对象快照在它执行后才采集。因此观测脚本可以把两侧不同的受测 SQL 结果都清空，然后比较器将空表判断为相同。

实际 CLI 反例使用 `real` 列的三行值 `16777215`、`16777216`、`16777218`。原 SQL 为 `UPDATE dbo.T SET Mark = CASE WHEN Value + 1 > 16777216 THEN 1 ELSE 0 END;`，现有规则产生 `Value > 16777215` 的预览。浮点边界导致 Mark 值不同，两侧均更新 3 行。ObserveSql 为只读 SELECT 时正确返回 `Different / CanApply=false`；改成 `DELETE FROM dbo.T;` 后，两侧对象差异被抹掉，返回 `PassedForScenarios / CanApply=true`。

应禁止有修改副作用的观测脚本，或设计能保留受测 SQL 结束时原始对象状态且不受观察器修改影响的观测流程；清理操作必须与取证分开。新增真实数据库回归，保证 DELETE/UPDATE/DDL/事务型观察器无法擦除差异后获得应用资格。

证据：[原文](verification/IMP-19-review/real-observer-source.sql)、[提案](verification/IMP-19-review/real-observer-proposals.json)、[可写观察器配置](verification/IMP-19-review/real-observer-mutating-suite.json)、[误通过结果](verification/IMP-19-review/real-observer-mutating-validated.json)、[只读对照结果](verification/IMP-19-review/real-observer-readonly-validated.json)。

## P1：内部状态查询改写了观察器要读取的会话状态

位置：`src/SqlXmlAnalyzer.Application/Services/LocalDbSqlSemanticRunner.cs:115–120`。

受测 SQL 结束后，验证器先执行 `SELECT @@TRANCOUNT, XACT_STATE(), @@OPTIONS`，再执行 ObserveSql。这条内部 SELECT 将 `@@ROWCOUNT` 改为 1，并影响 `@@ERROR` 等会话状态。于是即使场景显式要求 `SELECT @@ROWCOUNT`，读取的仍是验证器自身的执行结果。

实际 `rewrite-validate` 反例同样使用上述三行 real 值，原 SQL 依次按 `Value + 1 > 16777216` 和 `Value + 1 <= 16777216` 执行两条 `SET Value=Value` 的 UPDATE；提案移项为 `Value > 16777215` 和 `Value <= 16777215`。直接执行两侧的总更新行数均为 3，最后一条语句的 `@@ROWCOUNT` 分别为 **2 与 1**。验证器却把双方 ObserveSql 结果都记录为 1，并返回 `PassedForScenarios / CanApply=true`。

应在任何辅助 SQL 改写状态前捕获需比较的会话状态，并让观察器读取受测 SQL 的真实状态；如果暂不支持某类依赖会话瞬时值的 ObserveSql，须拒绝或标记无法验证，不能发布匹配结论。新增真实数据库测试，不能仅构造 `SqlSemanticObservation` 验证比较函数。

证据：[原文](verification/IMP-19-review/real-rowcount-source.sql)、[场景](verification/IMP-19-review/real-rowcount-suite.json)、[CLI 误通过结果](verification/IMP-19-review/real-rowcount-validated.json)、[无辅助查询的直接执行对照](verification/IMP-19-review/real-rowcount-direct.json)。Microsoft 说明无 FROM 的 SELECT 会将 `@@ROWCOUNT` 设为 1，见 [官方 @@ROWCOUNT 文档](https://learn.microsoft.com/en-us/sql/t-sql/functions/rowcount-transact-sql?view=sql-server-ver17)。

## P2：把 RecordsAffected 的 -1 哨兵值当作更新行数累加

位置：`src/SqlXmlAnalyzer.Application/Services/LocalDbSqlSemanticRunner.cs:191`。

`SqlDataReader.RecordsAffected=-1` 表示 SELECT 等没有适用的 DML 行计数，不是负一行更新。当前代码对每批次直接相加，使只读批次抵消真实更新行数，也可能把等价只读脚本判为不同。

已复现两侧都有两个批次、相同 SELECT 结果及相同最终表数据的脚本：原文批次更新行数为 `1 + 2`，候选为 `4 + SELECT(-1)`。真实更新总数为 **3 与 4**，验证器却均记录 3，返回 `PassedForScenarios / CanPrepareApply=true`。应将“没有适用行计数”与数值分开，累计时不能用 -1 抵消更新行数；明确保留每批次计数或规范化累计契约。

证据：[原文](verification/IMP-19-review/affected-sentinel-original.sql)、[候选](verification/IMP-19-review/affected-sentinel-candidate.sql)、[场景](verification/IMP-19-review/affected-sentinel-suite.json)、[误通过结果](verification/IMP-19-review/affected-sentinel-result.json)。语义依据见 [Microsoft SqlDataReader.RecordsAffected](https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqldatareader.recordsaffected?view=sqlclient-dotnet-standard-6.1)。

## 验证与结论边界

- 重新执行 Release 的 58 项 IMP-19 单元测试，全部通过：[TRX](verification/IMP-19-review/imp19-review-release.trx)。这些反例未进入原测试矩阵；测试通过不能排除上述误判。
- 本轮反例实际运行在专用 SQL Server 2025 LocalDB `17.0.4075.5`、兼容级别 170、`Latin1_General_100_CI_AS_SC`。只使用合成数据及本轮自有临时数据库。
- 前两项实际经过现有规则生成、选择及 `rewrite-validate` 应用准备链路；第三项经 `semantic-compare` 验证。不将独立比较报告本身视为写入凭据，本轮未执行对这些错误候选的应用。
- 已检查源/选择 hash、一次性凭据、备份与提交状态、取消、DUMP 及日志测试，本轮未确认这些路径有新的必修缺陷。没有宣称形式化证明、全部 SQL 语法或生产工作负载审查完成。
- `GO <重复次数>` 的疑点已排除：当前解析器拒绝该形状，未获得可应用状态，不列为缺陷。

复现以上已保存夹具，可在仓库根目录运行：

```powershell
$cli = 'SqlXmlAnalyzer.CLI/bin/Release/net8.0/SqlXmlAnalyzer.CLI.dll'
$cases = 'DOCS/verification/IMP-19-review'
dotnet $cli semantic-compare "$cases/affected-sentinel-original.sql" --candidate "$cases/affected-sentinel-candidate.sql" --scenarios "$cases/affected-sentinel-suite.json"
$proposal = Get-Content -Raw "$cases/real-rowcount-proposals.json" | ConvertFrom-Json
$id = $proposal.Review.Proposals[0].Id
dotnet $cli rewrite-validate "$cases/real-rowcount-source.sql" --select $id --scenarios "$cases/real-rowcount-suite.json" --show-sql
```

上述 ID 只适用于当前源文及规则版本；规则版本变化后须重新生成提案。
