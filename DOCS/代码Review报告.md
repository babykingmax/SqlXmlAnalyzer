# 代码 Review 报告

**实施进展（2026-09-08）：** R01 的常量、参数、身份和文本遗漏已修补并通过最小探针；R01 尚未整体关闭。 当前实现、验证及限制见[IMP-05 脱敏覆盖与输出入口验证](./IMP-05脱敏覆盖与输出入口验证.md)。下文保留原审查时点的发现与行号，不将历史失败改写成通过。

日期：2026-09-08；基线：`c5b1e0766cfcdf28f0d3a27d3238f5f0d6ae1a15` 加当前未提交导航/快捷键改动。审查方法与边界见 [验证记录](./检索来源与验证记录.md)。

以下按严重度排列，编号对应 [探针原始输出](./review-evidence/probe-results.json)。R01–R20 有本地 .NET 探针输出；R21/R22 为绑定和生成路径直接证明的静态问题。最小 XML 片段用于调用真实代码验证行为，不宣称全部通过完整 ShowPlan XSD 校验。涉及 SQL 结果变化的判断区分“已生成改写输出”和“依据 SQL Server 语义推导”，未连接数据库执行。

## P1：优先修复

### R01 [P1] 补齐脱敏中的常量和身份字段 — Core/Services/PlanObfuscatorService.cs:46

用户执行“导出脱敏执行计划”时，程序会替换 StatementText、ScalarString、编译/运行参数值和五类对象属性，但保留 `<Const ConstValue="...">`、Object.Server、Object.Alias 等内容。探针中 `SECRET_LITERAL`、`SECRET_SERVER`、`SECRET_ALIAS` 全部出现在输出，仅 Table 被替换；真实业务值因此可以随“脱敏”文件一起共享。[代码](E:/SqlXmlAnalyzer/Core/Services/PlanObfuscatorService.cs:46)

- **调用链**：脱敏导出 UI → PlanObfuscatorService.ObfuscatePlan → 保存 XML。
- **修复**：定义按 schema 分类的敏感字段处理及未知字段策略，覆盖常量、自由文本和身份属性；导出前给出处理摘要。保留必要结构与“完全清除敏感信息”应分别验证。
- **验收**：现有 `PlanObfuscatorServiceTests` 增加 ConstValue、Server、Alias、自由文本/注释和嵌套节点矩阵；敏感标记不得残留，原文档不得被改变。

### R02 [P1] 在没有数据约束时保留 LTRIM/TRIM — src/SqlXmlAnalyzer.Refactoring/Rules/TrimRefactorRule.cs:111

默认规则把 `WHERE LTRIM(UserName) = 'admin'` 改成 `WHERE UserName = 'admin'`，仅在 Context 写一条假设警告；对于值 `' admin'`，两者筛选结果不同。WPF 和 CLI 均默认注册该规则，WPF 的 `PlanAnalysisService:109–111` 只返回成功结果的 OutputSql，未把规则假设随 SQL 返回给界面。探针确认 IsSuccess=true 且 LTRIM 被移除。[代码](E:/SqlXmlAnalyzer/src/SqlXmlAnalyzer.Refactoring/Rules/TrimRefactorRule.cs:111)

- **证据边界**：改写输出已执行验证；结果差异按 [LTRIM 定义](https://learn.microsoft.com/en-us/sql/t-sql/functions/ltrim-transact-sql) 推导，未执行 SQL。
- **现有测试**：`Run_WithLTrimRTrim_ShouldOptimizeAndWarn` 刻意接受现状。这是当前默认行为的语义设计缺陷，不是断言失败或新提交回归。
- **修复**：缺少“列不含前导空格”等可验证约束时只提出建议；在 UI 中保留规则前提。不要把常量无空格误当成列值无空格。
- **验收**：包含前导空格、NULL、纯空格、不同字符串类型的反例；默认模式保留无法证明等价的表达式。

### R03 [P1] 备份失败后终止覆盖原 SQL — src/SqlXmlAnalyzer.Application/ApplicationOrchestrator.cs:89

非 dry-run 分支创建 `.bak` 失败后只记日志，随后仍调用 WriteAllText 覆盖原 SQL。探针让备份写入抛 IOException，得到 `BackupAttempted=true`、`SourceWritten=true`、`IsSuccess=true`、返回 Warnings 为空；调用者会认为完整流程成功，失去预期恢复路径。[代码](E:/SqlXmlAnalyzer/src/SqlXmlAnalyzer.Application/ApplicationOrchestrator.cs:89)

- **修复**：备份属于写回前置条件，失败必须停止；使用同目录临时文件和源内容指纹实现受控替换，避免反复覆盖唯一 `.bak`。
- **验收**：注入备份失败、源写失败、外部修改场景；原文件保持完整且结果明确失败。探针使用内存 IFileHandler，没有覆盖任何真实 SQL 文件。

### R04 [P1] 在 CLI 判定通过前验证文档确为执行计划 — SqlXmlAnalyzer.CLI/Program.cs:303

扫描路径只验证 XML 能解析及 Root 非空，没有确认 ShowPlanXML/命名空间/支持结构。把 `<notAPlan/>` 保存为 `.sqlplan`，真实 CLI 输出 `Status="Passed"`、`Issues=[]`、成本为 0 且退出码 0。`SqlXmlAnalysisEngine` 对相同输入也返回零问题，因此错误文件可以被自动化门禁当成健康计划。[CLI 代码](E:/SqlXmlAnalyzer/SqlXmlAnalyzer.CLI/Program.cs:303)、[分析入口](E:/SqlXmlAnalyzer/src/SqlXmlAnalyzer.Analysis/SqlXmlAnalysisEngine.cs:30)

```powershell
dotnet run --project SqlXmlAnalyzer.CLI --no-build -- --path <not-a-plan.sqlplan> --format json
```

- **修复**：GUI、CLI、分析适配器共享文档分类契约；不支持格式返回明确错误和非零退出码。不要用“必须有 RelOp”粗略替代根类型验证，以免拒绝合法的特殊语句计划。
- **验收**：无关 XML、死锁文件误作为计划、错误命名空间、带前缀的合法 ShowPlan 和合法无算子语句分别测试。

### R19 [P1] 保留表变量的事务回滚语义 — src/SqlXmlAnalyzer.Refactoring/Rules/TableVariableRefactorRule.cs:18

只要收集到表变量声明就允许改写，未检查事务语义。探针输入先声明 `@T`，再 BEGIN TRAN、INSERT、ROLLBACK，最后 COUNT；输出把它无警告地变成 `#T` 并返回成功。原表变量的数据修改不会按普通事务回滚方式恢复，临时表的数据修改会回滚，因此返回行数可以变化。[规则入口](E:/SqlXmlAnalyzer/src/SqlXmlAnalyzer.Refactoring/Rules/TableVariableRefactorRule.cs:18)、[替换实现](E:/SqlXmlAnalyzer/src/SqlXmlAnalyzer.Refactoring/Rules/TableVariableVisitor.cs:34)

```sql
DECLARE @T TABLE (id int);
BEGIN TRANSACTION;
INSERT INTO @T VALUES (1);
ROLLBACK TRANSACTION;
SELECT COUNT(*) FROM @T;
```

- **证据边界**：引擎转换已复现；1 与 0 的结果差异按官方 [table 类型说明](https://learn.microsoft.com/en-us/sql/t-sql/data-types/table-transact-sql) 推导，未执行数据库测试。
- **修复**：默认作为需人工验证的建议；事务、错误处理、过程/函数上下文和作用域未证明安全时不自动转换。
- **验收**：增加实际 SQL Server 上的回滚、SAVEPOINT、TRY/CATCH 等价测试，并记录引擎版本/兼容级别。

## P2：常规缺陷

### R05 [P2] 让文件分类接受解析器已支持的死锁包装结构 — SqlXmlAnalyzer.Core/Services/DocumentOpenService.cs:152

分类器只接受根为 deadlock，根为 deadlock-list 或 XE 包装时返回 Unknown，后续 UI 不进入死锁分析；下游 `TryParseDeadlockXml` 已会查找嵌套 deadlock。探针同一文档得到 `openKind=Unknown`、`parserAccepts=true`，形成入口与核心能力不一致。[代码](E:/SqlXmlAnalyzer/SqlXmlAnalyzer.Core/Services/DocumentOpenService.cs:152)

**修复/验收**：共享包装结构识别；单条包装可打开，多条应显示事件选择而非静默取第一条；为 GUI 分类和解析器添加同一组契约测试。

### R06 [P2] 读取标准 priority 属性 — SqlXmlAnalyzer.Core/DeadlockXmlParser.cs:166

解析器只读取 currentdeadlockpriority/deadlockpriority，遗漏标准 deadlock process 的 `priority`。探针输入 `priority="7"`，模型变成字符串 `"0"`，造成节点显示和优先级模式诊断失真。[代码](E:/SqlXmlAnalyzer/SqlXmlAnalyzer.Core/DeadlockXmlParser.cs:166)

**修复/验收**：读取 priority，兼容别名但定义冲突优先级；缺失与实际 0 分开处理。以 -10、0、7、缺失/非法值验证，并参照 [Microsoft 死锁字段说明](https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-deadlocks-guide)。

### R07 [P2] 不要把 REPEATABLE READ 判断为范围锁证据 — SqlXmlAnalyzer.Core/DeadlockGraph.cs:1103

`HasRangeLockPattern` 看到 isolationlevel 含 repeatable 就返回 true，继而输出 SERIALIZABLE 范围锁死锁和防幻读解释。探针只有 S/X 锁且隔离级别为 repeatable read，仍命中该模式。REPEATABLE READ 允许新匹配行产生幻读，不能由它推出已有键范围锁。[代码](E:/SqlXmlAnalyzer/SqlXmlAnalyzer.Core/DeadlockGraph.cs:1103)、[隔离级别定义](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-transaction-isolation-level-transact-sql)

**修复/验收**：以资源上的 Range 锁模式作为直接证据；隔离级别仅作上下文。REPEATABLE READ + S/X 不命中，真实 RangeS-S/RangeX-X 等保留适当诊断。

### R08 [P2] 使用完整环成员集合生成关键路径 — SqlXmlAnalyzer.Core/Parsers/DeadlockTimelineParser.cs:228

时间线 DFS 使用全局 visited；遍历已返回的节点时直接跳过，不能识别所有重叠环的成员。探针边集合为 `a→b、b→a、a→c、c→b`，c 属于 `a→c→b→a`，主图包含 c，而时间线 `c.IsInCycle=false`。开启“聚焦关键环”会错误隐藏参与死锁的进程。[代码](E:/SqlXmlAnalyzer/SqlXmlAnalyzer.Core/Parsers/DeadlockTimelineParser.cs:228)

**修复/验收**：主图与步骤状态共享 SCC/环成员计算；遍历顺序置换、交叠环和无环附属进程应获得稳定且正确的可见集合。

### R09 [P2] 修正基数规则对并行执行次数的归一化 — SqlXmlAnalyzer.Core/Rules/CardinalityErrorRule.cs:39

RULE_030 把各 worker 的 ActualExecutions 相加，再将总 ActualRows 除以它，与算子 EstimateRows 比较。探针估算 16,000，16 个 worker 各执行 1 次、各输出 1,000，实际总量完全吻合；RULE_030 却报告“实际单次 1,000、偏差 16 倍”的 Critical，RULE_004 对同样节点不报错。[代码](E:/SqlXmlAnalyzer/SqlXmlAnalyzer.Core/Rules/CardinalityErrorRule.cs:39)

**修复/验收**：定义并行/重执行计数契约并让两规则共享；加入 DOP=1/2/16、协调线程、Nested Loops 多次执行的夹具。不能简单用所有线程的平均值替代算子基数。

### R10 [P2] 从算子载荷读取残差谓词 — SqlXmlAnalyzer.Core/PlanDiagnosticAnalyzer.cs:309

ExtractResidualPredicate 只找 RelOp 的直接 Predicate，而常见 Index Seek 的残差在 `RelOp/IndexScan/Predicate`。探针输出 100 行、读取 1,000 行，UI 详情能取到残差，但该方法返回空，RULE_034 不产生读放大警告。RULE_006 仍可能给出一般 Info，不能弥补 RULE_034 的具体诊断缺失。[代码](E:/SqlXmlAnalyzer/SqlXmlAnalyzer.Core/PlanDiagnosticAnalyzer.cs:309)

**修复/验收**：在算子自身 IndexScan/TableScan 等载荷范围提取；不要跨越子 RelOp。覆盖估算计划、实际读放大和无残差反例，核对 CLI/图详情的一致性。

### R11 [P2] 按 XML 填充执行模式和执行属性 — Core/Services/PlanGraphNodeBuilderService.cs:152

节点构造固定 `ExecutionMode="Row"`，IsParallel 不接受 XML 的 `Parallel="true"`，Ordered 仅检查 LogicalOp 名称是否含 Sort。探针提供 Batch、Parallel=true、IndexScan Ordered=true，最终显示 Row/false/False；UiActionService 原样复制这些值，影响图、表格及节点详情。[代码](E:/SqlXmlAnalyzer/Core/Services/PlanGraphNodeBuilderService.cs:152)

**修复/验收**：分别解析估算/实际模式、XML 布尔词法和载荷 Ordered；不支持或缺失值显示 Unknown。以 Batch/Row、true/false/1/0、有序 Seek 验证三个属性映射。

### R12 [P2] 在索引模拟前完成分母计算 — SqlXmlAnalyzer.Core/Simulation/CostImpactSimulator.cs:47

遍历到每条语句才累计 totalOriginalCost，同时立即把该语句算子成本除以当前累计值，使结果依赖文件顺序。两个成本分别为 10 与 100 的同表扫描交换位置，探针收益从 100% 变成 65%，而输入工作量与索引定义未改变；100% 上限又掩盖了累计超量。[代码](E:/SqlXmlAnalyzer/SqlXmlAnalyzer.Core/Simulation/CostImpactSimulator.cs:47)

**修复/验收**：先计算完整分母，再以统一成本口径估算；语句置换应保持结果不变。另外，固定扫描/回表/排序收益比例属于模型假设，需按详细设计文档降低产品承诺。

### R13 [P2] 保留单个引用标识符内部的点号 — SqlXmlAnalyzer.Core/Refactoring/IndexDdlCompiler.cs:69

EscapeName 对每个名字执行 Split('.')，把合法单列名 `[Order.Detail]` 转成 `[Order].[Detail]`。该函数被用于索引列和对象名，生成 DDL 的标识符层级与原对象不同，可能语法失败或定位错误。[代码](E:/SqlXmlAnalyzer/SqlXmlAnalyzer.Core/Refactoring/IndexDdlCompiler.cs:69)

**修复/验收**：一个函数只转义一个标识符；多段名称由独立字段组装。补充点号、右方括号转义、空格、Unicode、保留字及 schema/table 分段测试；不要破坏 `]]` 表示的字面右括号。

### R14 [P2] 用完整对象身份关联缺失索引 — Core/Services/PlanGraphMissingIndexAssociationService.cs:35

输入节点只有 TableName，关联仅返回同表名的首条建议，忽略 schema、数据库和语句。探针为 sales.T 与 audit.T 准备两条建议，两节点都关联 sales.T；用户在 audit.T 上打开沙盒或复制建议时，会看到另一对象的方案。[代码](E:/SqlXmlAnalyzer/Core/Services/PlanGraphMissingIndexAssociationService.cs:35)

**修复/验收**：节点和建议传递完整对象键；`ExtractMissingIndexes` 还需保留 XML 中的 Database。不同 schema/数据库同名表、自连接别名、多语句建议都不得串联。

### R15 [P2] 把缺失运行指标保留为未知 — Core/Services/PlanComparisonController.cs:168

GetRuntimeMetrics 在没有运行信息时返回四个零，比较器会把估算计划与实际计划的差异当作真实改善。探针 A 有 elapsed=100、logical reads=200，B 只有估算信息，B 显示 Elapsed=-100、Logical reads=-200。[代码](E:/SqlXmlAnalyzer/Core/Services/PlanComparisonController.cs:168)

**修复/验收**：指标值携带 HasValue/来源；只在双方存在且可比时计算差值，否则 N/A。覆盖实际/实际、实际/估算、缺字段和真实零值。

### R16 [P2] 对每个选定语句进行计划比较 — Core/Services/PlanComparisonController.cs:66

GetRootRelOp 只取全文第一个 RelOp，所以第二条及以后语句的变化不进入对比树。探针两份文档首语句相同，第二语句成本由 2 变为 999，对比仍仅返回成本 1 与 1；界面没有提示其余语句未参与。`PlanAnalysisService:47–51` 的 SQL 展示同样只取首语句，进一步模糊全文诊断与单语句视图的关系。[代码](E:/SqlXmlAnalyzer/Core/Services/PlanComparisonController.cs:66)

**修复/验收**：加入语句选择和语句匹配，报告参与/未匹配范围；仅第二条变化、语句重排、重复 NodeId、多 Batch 场景必须可识别。

### R17 [P2] 阻止算子详情穿透子 RelOp — Core/Services/PlanGraphRelOpDetailsService.cs:52

解析父算子载荷时用 Descendants 查 Object/Predicate，穿透其下的子 RelOp。探针 Nested Loops 没有自身表或残差，子 Index Seek 有 ChildTable/ChildFilter，父节点却得到这两个值；图详情、缺失索引关联和诊断容易归错节点。[代码](E:/SqlXmlAnalyzer/Core/Services/PlanGraphRelOpDetailsService.cs:52)

**修复/验收**：遍历到子 RelOp 即止，区分算子自身与子树聚合字段；用嵌套 Join、Filter、Seek 的夹具验证父子边界。

### R18 [P2] 在死锁环去重前去掉闭合重复节点 — SqlXmlAnalyzer.Core/DeadlockGraph.cs:389

DFS 把起点追加到 ProcessIds 尾部表示闭环，去重直接排序整个列表。单环 `a→b→a` 的两个起点产生 `a,a,b` 和 `a,b,b`，不能合并；探针唯一双进程环被计为 2 个。由此环数、报告解释和后续选择包含重复结果。[代码](E:/SqlXmlAnalyzer/SqlXmlAnalyzer.Core/DeadlockGraph.cs:389)

**修复/验收**：用不含重复尾节点的有向环规范表示；旋转等价应合并，不同资源边形成的解释路径按明确规则保留。2/3 节点环和多资源边分别测试。

### R20 [P2] 为表变量替换分配不冲突的临时表名 — src/SqlXmlAnalyzer.Refactoring/Rules/TableVariableVisitor.cs:36

名称直接把 @ 替换为 #，未检查当前 batch 已有临时表。探针 `CREATE TABLE #T...; DECLARE @T TABLE...` 被改成两个 CREATE TABLE #T，仍返回 IsSuccess=true；语法再解析不会发现对象名冲突，执行会失败，生成的清理 DROP 还可能指向原本已有对象。[代码](E:/SqlXmlAnalyzer/src/SqlXmlAnalyzer.Refactoring/Rules/TableVariableVisitor.cs:36)

**修复/验收**：维护作用域符号表并分配唯一名称，清理仅针对新创建对象；覆盖同名临时表、大小写、多个声明和嵌套过程。更广泛的表变量语义限制见 R19。

### R21 [P2] 为实际输出行数列绑定 ActualRows — Views/PlanWorkspaceView.xaml:93

“实际行 (ActRows)”列绑定 ActualRowsRead，而图节点使用 ActualRows。R11 探针中输出 100、读取 1,000，表格将显示 1,000 为实际行，造成读放大判断和跨视图核对错误；`Views/PlanView.xaml:87` 也有同样绑定。[代码](E:/SqlXmlAnalyzer/Views/PlanWorkspaceView.xaml:93)

**修复/验收**：把“实际输出行数”绑定 ActualRows，另列“实际读取行数”绑定 ActualRowsRead；缺失显示 N/A。用二者明显不同的样例进行绑定/界面验收。

### R22 [P2] 将合成步骤明确标为死锁依赖推演 — ViewModels/DeadlockPlaybackViewModel.cs:160

UI 文案为“回放死锁形成过程”，而时间线把所有 Grant 按 SPID 排序，再把所有 Request 按 SPID 排序，最后附加 Victim。XML 没有为这些步骤提供真实获取/请求时间；当前展示容易让用户把程序构造的顺序当成调查事实。[文案](E:/SqlXmlAnalyzer/ViewModels/DeadlockPlaybackViewModel.cs:160)、[顺序生成](E:/SqlXmlAnalyzer/SqlXmlAnalyzer.Core/Parsers/DeadlockTimelineParser.cs:164)

**修复/验收**：改为“依赖关系推演（非实际时间线）”，将快照事实和演示顺序分别表达；SPID 顺序变化不得产生被宣称为真实的新历史。若以后导入可关联的带时间事件，再增加独立的时间轴功能。

## 总体评价与剩余风险

当前项目已经具备良好的功能覆盖与服务拆分基础，构建及 807 项现有测试通过。本次独立探针揭示的主要风险集中在：输入契约、SQL 改写语义、诊断指标定义、死锁模型一致性，以及共享输出和文件写回。建议先完成 P1 修复，再统一领域模型与 UI 表达。

尚需单独验证的大文件内存/取消延迟、复杂图性能、完整 SQL Server 版本矩阵、真实 XEL 错误恢复、深浅色/DPI/可访问性，属于后续验证计划，未伪装成已测出的缺陷。当前 HTML 安全编码、安全 XML 加载和过期请求防覆盖已经存在，应继续保留。

本次不是对某次变更的回归归责：R02 等行为在现有测试中被显式接受；评审指出的是当前产品的错误安全前提。导航/快捷键工作树 diff 已阅读，未发现可证明的新缺陷，不将其纳入上述 22 项。
