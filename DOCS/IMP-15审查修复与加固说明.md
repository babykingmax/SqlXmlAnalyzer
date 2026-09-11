# IMP-15 审查修复与加固

日期：2026-09-09。完成两项审查缺陷，并修复真实 SQL Server 验证中发现的原始列名误解码问题。保留现有规则 ID、范围、配置、异常策略及未校准模型提示。

## 修复内容

### 实体列身份与沙盒 DDL

`ColumnReference` 不一定代表实体列。参数、书签和内部表达式也使用该元素；仅因它位于目标算子中就补全对象身份，会让 `@p`、`Bmk1000` 等进入可选列及 INCLUDE。

`IndexTargetResolver.FindColumns` 现在要求 Database、Schema、Table 及列名有效并匹配捕获对象，Server 同样精确匹配；拒绝缺失、冲突、带参数元数据或无效名称的引用。保留当前 QueryPlan、算子及命名空间边界。不按名称前缀封禁列：具有完整实体身份的 `@real`、`Expr1000`、`Bmk1000` 仍可使用。

覆盖评分与沙盒只使用核验后的实体输出。无法证明归属的列不会进入可选列，参数或内部输出不会被误认为缺少的 INCLUDE 列。该结果仍是候选覆盖假设，不证明既有索引覆盖、列类型可索引或实际性能收益。

### Sort 取证与方向

Sort 自身没有扫描目标的 `Object`；其子 RelOp 才包含 Object。原先先过滤带 Object 的算子，再从中寻找 Sort，使正常排序证据无法参与评分。

现在从建议所属 QueryPlan 的 Sort 算子读取直接 OrderBy 载荷，逐项核验完整列身份与必需的 Ascending 属性。任一项缺失、冲突、跨对象、位于扩展/内部包装中或结构不明确，整项排序不参与评分，避免截取有效子集后制造匹配。多个 Sort 独立比较，不拼接列列表，也不叠加分数。

当前生成的键默认为 ASC；全 ASC 与可反向扫描的全 DESC 可匹配，混合方向不计排序分。保持排序项最多 15 分、总分上限 100。回归中未封顶的等值前导键场景从错误的 70 分恢复到 85 分；不会借用同名表、其他数据库、语句或 QueryPlan 的排序。

### 原始列名与 DDL 标识符分离

真实 LocalDB 计划证实 `ColumnReference/@Column` 保存原始列名：实体列 `[literal]` 的属性值仍是 `[literal]`，不可再次解码为 `literal`。对象名称段和 MissingIndex 的 DDL 名称使用各自原有分隔符约定。

实体列提取、Sort、覆盖比较与沙盒现在保留 Column 原值，只在构造 DDL 列标识符时引用一次。例如 `[literal]` 生成 `[[literal]]]`，`R]ange` 生成 `[R]]ange]`。真实捕获计划已保存为测试资源，避免仅用手写 XML 的分隔符假设验证自己。

## 异常与日志

保留编译器、计划提取、规则执行和沙盒的 ExceptionPolicy 边界。未知异常仍记录原异常并生成 minidump/exception.json；DUMP 写入失败保留捕获失败信息。无实体身份、参数引用及不完整排序属于证据不足，直接排除；名称校验中的预期 InvalidDataException 不升级为未知故障。

新增日志只记录有效/未采用列计数、排序证据数量，不记录 SQL 或对象原值。扩展既有日志测试覆盖这两条路径：Debug 记录 DEBUG/WARN/ERROR/CRITICAL，Release 仅 ERROR/CRITICAL。日志使用 stderr/文件，CLI stdout 保持 JSON。

## 验证结果

新增 `IndexEvidenceHardeningTests` **46 项**。首轮 34 项在修复前 17 失败、17 通过；随后补充方向、结构、特殊原始名称与实际计划回归。修正旧夹具的完整列归属，并保留原覆盖行为断言。IMP-15 初版 36 项加本轮 46 项，累计新增 82 项；不据此宣称代码覆盖率变化。

| 检查 | Debug | Release |
| --- | --- | --- |
| 完整解决方案构建 | 0 警告、0 错误 | 0 警告、0 错误 |
| 全量 xUnit | 1766 通过、0 失败、0 跳过 | 1766 通过、0 失败、0 跳过 |
| 原生 DUMP、捕获失败、分模式日志 | 通过 | 通过 |
| CLI 合成多语句夹具 | 退出 0，4 条已绑定候选，无分析失败 | 同 Debug，无 DEBUG/WARN/INFO 输出 |

在独立 LocalDB **17.0.4075.5** 中创建测试表，捕获 SQL Server 原生计划，核验参数排除、7 个可选实体列、完整 Sort 及特殊列名。以 R 为前导键、其他实体列为 INCLUDE 的候选隔离排序贡献，得到覆盖 40 + Sort 15 = 55 分；生成的 CREATE/回滚均实际执行通过。使用 ONLINE=OFF、ROW、SORT_IN_TEMPDB=OFF，专用数据库和实例已清理；未操作既有 LocalDB 实例或生产库。

完整评分模型仍未校准。原有 ScalarString 谓词文本启发式可能漏判紧凑写法；此项属于 IMP-16 的评分/模拟整改范围，本次实际计划验证不把该启发式的结果当作 SQL Server 成本。未进行完整 WPF 窗口/SSMS 人工验收。

证据：[验证清单与源码哈希](verification/IMP-15-review/validation.json)、[Debug 构建](verification/IMP-15-review/build-debug.log)/[测试](verification/IMP-15-review/test-debug.log)、[Release 构建](verification/IMP-15-review/build-release.log)/[测试](verification/IMP-15-review/test-release.log)、[首次反例](verification/IMP-15-review/red-tests.log)、[LocalDB 结果](verification/IMP-15-review/localdb-results.json)、[实际捕获计划](verification/IMP-15-review/captured.sqlplan)、[复现脚本](verification/IMP-15-review/verify-localdb.ps1)。

## 检索依据

按官方资料优先。网页检索工具连接失败，改用只读 HTTPS 读取 Microsoft Learn、官方 XSD 与微软 GitHub 样本；未从未知脚本复制可执行修复代码。官方资料已覆盖关键结构与方向约束，无需用社区推测代替这些定义。

- [Microsoft Showplan XSD](https://schemas.microsoft.com/sqlserver/2004/07/showplan/sql2019/showplanxml.xsd)：ColumnReferenceType 的对象属性可选且可承载参数元数据；SortType 的 OrderBy 与子 RelOp 分开；OrderByColumn 的 Ascending 为必需布尔属性。
- [SQL Server 索引设计指南](https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-index-design-guide?view=sql-server-ver17#index-sort-order-design-guidelines)：键的排序方向，以及索引反向扫描的适用关系。本工具据此保守排除无法满足的混合方向。
- [Showplan 算子参考](https://learn.microsoft.com/en-us/sql/relational-databases/showplan-logical-and-physical-operators-reference?view=sql-server-ver17)：Sort、RID/Key Lookup 等算子的语义，内部书签不能仅凭列引用标签当成用户列。
- [Microsoft sql-server-samples 的实际计划](https://github.com/microsoft/sql-server-samples/blob/master/samples/demos/Plan-Comparison/Offline/Compare1_Plan1_Slow.sqlplan)：通过 GitHub Blob API 读取，blob `bdd4b9679a8761a1253facfc100ddd0211aa4307`。其中无实体归属的 Expr 引用与 Sort/子 RelOp 结构用于交叉核对；原始特殊列名另由本次 LocalDB 捕获验证。
