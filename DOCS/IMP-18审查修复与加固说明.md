# IMP-18 审查修复与加固

**后续联合审查更新：** IMP-18 / IMP-19 联合审查的五项问题已修复，当前应用状态和验证支持范围见 [联合审查修复说明](IMP-18-19联合审查修复与加固说明.md)。

日期：2026-09-09。针对 IMP-18 审查中的两项 P2 问题实施修复。提案仍只供审核；数据库语义验证和可靠应用属于 IMP-19。

## 修复与行为

### 文本报告使用已选组合

此前，CLI 使用 `--select` 只选择第一条提案时，JSON `Review.PreviewSql` 正确，但终端和 `.txt` 文件末尾仍输出包含所有改写的 `OutputSql`。

现在两个文本出口共用 `FormatSqlComparison`，从 `Review.PreviewSql` 取得 SQL，标题为 `Selected review preview (not applied)`。不再 Trim 原文和预览，空选择保留源 SQL 的空白、换行和注释。`--show-sql` 才展开 SQL/diff；帮助文字同步修正。提案清单仍展示所有候选及其选择状态，以便审核未选项。

旧引擎没有 `Review` 时保留兼容展示。`RefactorResult.OutputSql` / JSON `RefactoredSql` 仍为完整候选，不能作为所选组合或应用许可。

实际合成输入包含两个改写：移除 Unicode 前缀、折叠 `Age + 10 > 50`。只选择第一条后，[文本报告](verification/IMP-18-hardening/cli-first-selected.txt)与 [JSON](verification/IMP-18-hardening/cli-first-selected.json)均保留 `Age + 10 > 50`；[未选报告](verification/IMP-18-hardening/cli-unselected.json)保留完整原文。[输入文件](verification/IMP-18-hardening/sample.sql)运行前后 SHA-256 均为 `241ECDE6CCD8ACD2A842BAEC75CC0D327862B46F2CA82F25F5C2D5B8002D1386`。

### 补齐 SELECT INTO 并加固对象事件比较

原验证器只收集显式临时表 `CREATE TABLE` / `DROP TABLE`、表变量声明及批次边界，遗漏了由 `SELECT INTO` 创建的表。

现在通过 ScriptDom `SelectStatement.Into` 收集目标，拒绝新增、移除、改名、临时/全局临时/永久目标切换和跨批次移动。永久 `SELECT INTO` 目标也进入检查，避免改变创建方式或目标类别后绕过保护。

加固措施：

- 对象名按完整标识符分段比较，保留大小写，不再仅取大写后的末段；离线不能假定数据库排序规则。引号差异解析成相同值时允许通过。
- 临时表 `DROP` 同时记录 `IF EXISTS`，保留错误行为差异。表变量声明名称也保留大小写。
- 事件带上 IF 的条件摘要及 THEN/ELSE 分支、WHILE 条件摘要、TRY/CATCH 分支。对象事件被移出控制语句、移至另一分支或控制条件改变时拒绝组合。未涉及对象事件的普通条件改写不因此被阻断。
- 保留 `CREATE TABLE` 与 `SELECT INTO` 的操作种类区别；不把同名对象的两种创建方式当成相同事件。
- 组合审核仍重新解析和收集完整预览。即使某条提案原先标记为有效，修改 diff/hash 后加入对象创建也会被最终复检拒绝；引擎失败时丢弃候选并保留源 SQL。

错误继续使用 `TemporaryObjectOwnership`。该静态检查不证明列绑定、完整对象定义、模块/动态 SQL 作用域、完整生命周期或语义等价；这些限制明确列入 `UnprovenProperties`。所有结果的 `Equivalence=Unproven`、`CanApply=false`。

## 检索依据

遵循优先读取 Microsoft 官方资料的顺序。官方文档与官方源码已能直接解释本次问题，无需以社区经验代替定义。当前网页检索服务连接失败，GitHub 插件没有可调用的检索接口，因此使用 HTTPS 读取官方页面、GitHub 公共源码及提交信息。

| 来源 | 已核对的事实及采用方式 |
| --- | --- |
| [Microsoft Learn：SELECT INTO](https://learn.microsoft.com/en-us/sql/t-sql/queries/select-into-clause-transact-sql?view=sql-server-ver17) | SELECT INTO 同时涉及创建表与插入数据；据此补齐创建事件，且不将其视为 CREATE TABLE 的等价替换。 |
| [Microsoft Learn：数据库标识符](https://learn.microsoft.com/en-us/sql/relational-databases/databases/database-identifiers?view=sql-server-ver17) | 标识符受排序规则影响，`#` / `##` 有临时对象含义；据此保留大小写、分段与目标类别。 |
| [Microsoft SqlScriptDOM：Ast.xml 固定版本](https://github.com/microsoft/SqlScriptDOM/blob/b583737682dbb1682b97d0ac5a5261dc94279b8d/SqlScriptDom/Parser/TSql/Ast.xml) | `SelectStatement.Into` 是 `SchemaObjectName`；IF、WHILE 和 DROP 具有专门的结构属性。实现使用这些 AST 属性，不通过正则匹配 SQL。 |

本地编译及测试使用项目既有 ScriptDom `180.18.1` 和 `TSql160Parser`，未升级依赖，也未把公开仓库的最新代码直接复制为项目依赖。

## 测试、异常与日志

新增 36 项回归，涵盖无/部分/全部选择、终端/文件与 JSON 一致性、完整原文恢复、不展开 SQL、对象目标/大小写/分支/批次变化、合法结构不误拒、组合重验和引擎失败回退。新增校验失败测试确认已知错误不会触发未知异常 DUMP。

修复前的定向运行有 28 项失败，记录在 [反例日志](verification/IMP-18-hardening/tests-before.log)。修复后 [75 项相关测试通过](verification/IMP-18-hardening/tests-focused-debug.log)。最终全量结果：

| 配置 | 完整构建 | 全量测试 |
| --- | --- | --- |
| Debug | [0 警告、0 错误](verification/IMP-18-hardening/build-debug.log) | [2083 通过、0 失败、0 跳过](verification/IMP-18-hardening/test-debug.log) |
| Release | [0 警告、0 错误](verification/IMP-18-hardening/build-release.log) | [2083 通过、0 失败、0 跳过](verification/IMP-18-hardening/test-release.log) |

异常路径继续复用 `ExceptionPolicy` / `UnexpectedErrorReporter`；未知异常生成 Windows DUMP，DUMP 或诊断组件失败不替换主错误。现有真实 DUMP、DUMP 失败、去重和诊断失败测试纳入全量回归。Debug 日志为 DEBUG/WARN/ERROR/CRITICAL，Release 仅 ERROR/CRITICAL；提案风险说明属于业务结果，不按日志级别隐藏。没有新增记录 SQL 或对象名称的常规日志。

本次没有修改 WPF 布局，也未执行数据库语义验证；原 IMP-18 的桌面截图验收限制仍保留。未生成覆盖率报告。
