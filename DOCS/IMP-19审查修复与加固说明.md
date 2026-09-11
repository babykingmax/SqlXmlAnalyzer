# IMP-19 审查修复与加固

**后续联合审查更新：** UTF-16、sql_variant、读取预算、建库清理及界面状态五项问题的后续修复见 [联合审查修复说明](IMP-18-19联合审查修复与加固说明.md)。本页计数及附件为上一轮三项修复的历史证据。

日期：2026-09-09。对应 [IMP-19 代码审查](IMP-19代码审查.md)中的 2 项 P1 和 1 项 P2。三项已修复，新增 41 项单元/入口回归；Debug/Release 全量各 2182 项通过，完整构建各 0 警告、0 错误。初次实现与审查反例输出保留为历史证据，本轮输出独立保存在 `verification/IMP-19-hardening`。

## 修复与边界

| 审查问题 | 修复后的行为 | 防止再次回归的检查 |
| --- | --- | --- |
| ObserveSql 可擦除不同数据 | 独立 AST 规则只允许只读 SELECT；禁止 DML、DDL、SELECT INTO、嵌入 DML、变量赋值及事务/控制语句；清理由验证器负责 | 16 种原本可作 SetupSql 的写操作在 ObserveSql 中被拒绝；注释/字符串中的关键字及普通只读查询仍可用 |
| 辅助 SELECT 污染会话状态 | 受测 SQL 结束后立即执行只读 ObserveSql，再执行内部事务/选项查询和对象快照；观察器出错不能取得应用资格 | 真正执行 @@ROWCOUNT、ROWCOUNT_BIG() 及多语句观察器，保留原文/候选的 2 与 1 差异 |
| RecordsAffected 的 -1 抵消更新数 | -1 表示“不适用”；只有非负 DML 计数参与累计，纯查询/空脚本保持 -1，零行更新保持 0 | 真实更新总数 3 与 4 不再均记为 3；混合批次、纯查询分批、零行更新、非法负值及累计溢出均有断言 |

`SqlSemanticSandboxPolicy.Validate` 在打开数据库前检查场景，运行观察器时再次使用 `ParseObserverBatches`，应用准备也经过同一策略。合法 SELECT 外壳中的 SELECT INTO、嵌入 DML 或赋值不能绕过检查；后续批次中的写操作同样拒绝。错误包含 `ObserverReadOnly`，CLI 参数入口返回 2；直接验证服务返回 Failed，均不能生成应用凭据，也不会进行备份/写入。

只读观察器仍按其原始语句顺序执行。第一条查询可读取受测 SQL 留下的 `@@ROWCOUNT` / `@@ERROR`；如果观察器自身先执行了其他语句，后面的查询看到的是这些语句之后的状态。例如 `SELECT @@ROWCOUNT; SELECT @@ROWCOUNT;` 第二个结果反映第一条 SELECT。这与 SQL Server 自身行为一致，不会为每条观察语句伪造会话快照。

事务计数、XACT_STATE 和选项在只读观察器之后采集；观察器成功时不会提交、回滚或修改这些设置。观察器的 SQL 错误会使本轮不能应用；失败执行后的状态不能作为受测 SQL 成功完成的证据。类型/NULL/重复行/对象观测等既有比较继续保留。

`SqlSemanticAffectedRows.Combine` 对小于 -1 的计数及 Int32 累计溢出抛出可预期的输入/证据错误，不把无效计数、回绕值或截断值发布为成功证据。报告字段仍为整数：旧空脚本曾输出 0，现在为 -1；不得把 -1 解释为“负一行更新”。该值与零行 DML 有明确区别。

本轮没有扩大可证明的 SQL 范围。只有声明的场景能通过；TRIM、表变量等既有高风险限制继续生效，不宣称任意输入等价或性能改善。

## 测试与实际输出

新增 41 项：观察器策略/直接服务 24 项、行计数语义 11 项、应用准备入口 3 项、CLI 三命令入口 3 项。修复前观察器测试实际得到 **17 失败、7 通过**，证据见 [修复前日志](verification/IMP-19-hardening/observer-before.log)；修复后全部通过。

| 验证 | 证据 |
| --- | --- |
| Debug 构建与全量测试 | [构建](verification/IMP-19-hardening/build-debug.log)、[测试](verification/IMP-19-hardening/test-debug.log)：0 警告/错误，2182 通过、0 失败、0 跳过 |
| Release 构建与全量测试 | [构建](verification/IMP-19-hardening/build-release.log)、[测试](verification/IMP-19-hardening/test-release.log)：0 警告/错误，2182 通过、0 失败、0 跳过 |
| 新增真实数据库与应用阻断回归 | [Debug](verification/IMP-19-hardening/database-debug/summary.json)、[Release](verification/IMP-19-hardening/database-release/summary.json)：每配置 26/26，包含 2019/2025 两版本 |
| 既有 SQL Server 场景回归 | [Debug](verification/IMP-19-hardening/baseline-debug/summary.json)、[Release](verification/IMP-19-hardening/baseline-release/summary.json)：每配置 42/42；与新增检查合计每配置 68/68 |
| 正常 CLI 实际应用 | [Debug](verification/IMP-19-hardening/apply-debug/summary.json)、[Release](verification/IMP-19-hardening/apply-release/summary.json)：所选预览、原文验证期间不变、BOM、备份和报告失败后的真实提交状态通过 |
| WPF 生产窗口 | [日志](verification/IMP-19-hardening/wpf.log)、[验证后截图](verification/IMP-19/wpf-Release-8664fde1fffd440cbc9e02e4f2c88e2d/review-validated.png)、[应用结果](verification/IMP-19/wpf-Release-8664fde1fffd440cbc9e02e4f2c88e2d/application.txt)：真实验证、确认、精确写入/备份、四张截图及零绑定错误通过 |
| 数据库版本与清理 | [结果](verification/IMP-19-hardening/server-cleanup.json)：2019 为 15.0.4382.1、2025 为 17.0.4075.5，最终两实例剩余验证数据库均为 0 |

新数据库矩阵每个版本有 10 项直接比较和 3 项实际提案/验证/应用阻断流程。覆盖 ROWCOUNT/ROWCOUNT_BIG、顺序观察、-1 计数、纯查询/混合分批正向控制、零行 DML、活动事务、观察器 SQL 错误及关键字文本。三项工作流都由真实规则生成提案；场景拒绝或发现差异后，`rewrite-apply` 返回失败，源字节不变且不创建备份。

WPF 探针显示生产窗口并驱动生产 ViewModel，在真实 LocalDB 中执行验证，再完成确认和文件应用；本轮已检查验证后截图。文件选择器的人工鼠标操作仍不属于本次自动化验收范围。

典型结果：

- [real/ROWCOUNT 验证](verification/IMP-19-hardening/database-release/2025-apply-rowcount-validated.json)：原文与候选最后更新行数不同，状态为 Different、CanApply=false；[应用尝试](verification/IMP-19-hardening/database-release/2025-apply-rowcount-applied.json)保持 SourceWritten=false。
- [写入型观察器](verification/IMP-19-hardening/database-release/2025-apply-mutating-observer-validate.log)：在数据库工作之前拒绝 `DELETE FROM dbo.T`，不能擦除数据差异。
- [只读观察器对照](verification/IMP-19-hardening/database-release/2025-apply-readonly-difference-validated.json)：真实数据变化继续返回 Different。
- [混合批次计数](verification/IMP-19-hardening/database-release/2025-sentinel-result.json)：两侧 RecordsAffected 正确为 3 与 4。

未知异常 DUMP、DUMP 捕获失败、故障 reporter、取消及分配置日志均由全量测试重新执行。常规日志不新增 SQL 文本；可预期的观察器/计数错误进入既有失败边界。Debug 记录调试、警告、错误、致命信息；Release 仅错误、致命信息。业务差异/限制作为结果内容继续显示，原始报告和 DUMP 不因此脱敏。

## 重跑与迁移

已有场景的 SetupSql 不变。ObserveSql 若包含清理、写入、SELECT INTO 或变量赋值，须改为只读查询；不要把清理语句移入受测候选来制造相同结果。数据库生命周期清理由验证器完成。需要某个瞬时会话值时，在观察器第一条查询中读取。

在准备好 `SqlXmlAnalyzer_IMP19_2019` 和 `SqlXmlAnalyzer_IMP19_2025` 后：

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug
dotnet build SqlXmlAnalyzer.sln -c Release
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release --no-build
./DOCS/verification/IMP-19-hardening/Run-Hardening.ps1 -Configuration Debug
./DOCS/verification/IMP-19-hardening/Run-Hardening.ps1 -Configuration Release
./DOCS/verification/IMP-19/Run-Regression.ps1 -Configuration Release -OutputDirectory DOCS/verification/IMP-19-hardening/baseline-release
./DOCS/verification/IMP-19/Run-ApplyCheck.ps1 -Configuration Release -OutputDirectory DOCS/verification/IMP-19-hardening/apply-release
```

既有验证脚本新增可选 OutputDirectory，默认行为不变，便于保留历史证据。新增回归使用合成数据及自有隔离库；没有生成覆盖率报告，不宣称覆盖率提升。

## 检索依据

本轮优先核对 Microsoft 官方文档，已足以解释三个缺陷；再核对 GitHub 中微软维护的 SqlClient 与 ScriptDom 源码。GitHub 插件连接器没有可调用工具，网页检索工具连接失败，使用直接 HTTPS 读取官方页面及公开仓库，未将检索失败当成来源结论。

- [Microsoft @@ROWCOUNT](https://learn.microsoft.com/en-us/sql/t-sql/functions/rowcount-transact-sql?view=sql-server-ver17)及[官方源文档](https://github.com/MicrosoftDocs/sql-docs/blob/live/docs/t-sql/functions/rowcount-transact-sql.md)：DML 更新会话行数，无 FROM 的 SELECT 会将其设为 1，因此内部取证查询不能插在受测 SQL 与用户观察器之间。
- [Microsoft @@ERROR](https://learn.microsoft.com/en-us/sql/t-sql/functions/error-transact-sql?view=sql-server-ver17)及[官方源文档](https://github.com/MicrosoftDocs/sql-docs/blob/live/docs/t-sql/functions/error-transact-sql.md)：其值随每条语句重置，必须在目标语句之后及时读取。
- [Microsoft SqlDataReader.RecordsAffected](https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqldatareader.recordsaffected?view=sqlclient-dotnet-standard-6.1)：区分 SELECT 的 -1 与 DML 的非负累计数；修复按照这一契约组合批次结果。
- [dotnet/SqlClient v6.1.6 源码](https://github.com/dotnet/SqlClient/blob/v6.1.6/src/Microsoft.Data.SqlClient/src/Microsoft/Data/SqlClient/SqlDataReader.cs)：核对驱动中 -1 初始值、RecordsAffected 及关闭 reader 后的缓存行为；未依赖私有字段修改驱动状态。
- [Microsoft SqlScriptDOM AST](https://github.com/microsoft/SqlScriptDOM/blob/main/SqlScriptDom/Parser/TSql/Ast.xml)：按 SelectStatement.Into、SelectSetVariable、DataModificationTableReference 等结构检查观察器，不以关键字文本搜索判断只读。
