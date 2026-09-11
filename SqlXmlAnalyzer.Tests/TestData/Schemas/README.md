# IMP-06 ShowPlan 夹具来源

`showplanxml-sql2019.xsd` 于 2026-09-08 从[微软 SQL Server 2019 ShowPlan XSD](https://schemas.microsoft.com/sqlserver/2004/07/showplan/sql2019/showplanxml.xsd)下载，保留原始内容及版权声明。

- Schema version：1.539。
- SHA-256：`845B3FAA55748D442DFC71071315FF1EBB611E4E298E8D06A21EBF946B8C93A8`。
- 无外部 schema 引用；测试从嵌入资源经 `SafeXmlHelper` 读取，`XmlSchemaSet.XmlResolver = null`，不联网。
- `plan_no_relop.sqlplan` 与 `plan_prefixed_no_relop.sqlplan` 是人工构造、通过上述完整 XSD 验证的正向夹具，并非 SQL Server 实际采集文件；未执行其中的 SQL。
- `StmtSimpleType.QueryPlan` 为可选，`StmtUseDb` 不含 RelOp；`StmtBlockType` 允许零条语句。测试同时验证这些边界和前缀命名空间。
- `deadlock_multiple_events.xdl` 为合成 XE XML 包装夹具，验证两事件归属、原始带时区时间字符串及命名空间兼容，不声称复现真实数据库死锁。

产品只执行最低结构识别，不在运行时加载此 XSD，不将识别成功等同于完整 schema 或所有计划字段校验通过。
