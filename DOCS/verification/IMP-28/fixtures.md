# IMP-28 冻结样例来源

输入目录为 [SqlXmlAnalyzer.Tests/TestData/Compatibility](../../../SqlXmlAnalyzer.Tests/TestData/Compatibility)。样例在 IMP-28 修改及重建 Release 前保存。基线为包含尚未提交 IMP-23～27 工作的本地 IMP-27 加固产物，不能仅用 Git HEAD 重建该产物；程序集 SHA-256 和捕获时间见 [baseline-origin.json](../../../SqlXmlAnalyzer.Tests/TestData/Compatibility/baseline-origin.json)。最终 [fixture-hashes.json](fixture-hashes.json) 记录所有样例字节，`.gitattributes` 禁用该目录换行转换。

| 样例 | 来源与用途 |
| --- | --- |
| `configuration-imp01.json` | `git show c5b1e0766cfcdf28f0d3a27d3238f5f0d6ae1a15:RuleConfiguration.json`；历史配置，统一 LF |
| `configuration-legacy.json` | 合成旧格式：规则别名、大小写、未知规则、根和规则扩展；验证保留行为 |
| `session-v2.0.pesession` | 按上述历史提交实际 writer 字段重建，包含两个合成快照和 A/B；不是旧二进制生成的历史用户会话 |
| `session-v2.1-before.pesession` | 升级前 Release `TuningSessionService` 实际写出；合成计划、固定捕获时间、相对夹具文件名 |
| `plan.sqlplan`、`query.sql`、`deadlock.xdl` | 合成的单算子计划、候选 SQL、两进程死锁；无生产输入 |
| `cli-scan-before.json`、`cli-read-before.json`、`cli-refactor-before.json` | 升级前 Release CLI 实际输出；工作区夹具根路径规范为 `<fixture-root>`，保留其他字段 |
| `report-imp22-before.json` | 从冻结 scan 输出取 `DiagnosticReport` 后经 PowerShell 重新序列化；是语义样例，非原 CLI 输出字节切片 |
| `rules-before.json` | 升级前 Release `RuleMetadataCatalog` 的 34 个 ID、版本、默认严重度 |
| `baseline-origin.json` | 捕获环境和历史来源元数据 |

测试通过程序集嵌入资源读取，不依赖运行时当前目录。冻结样例不得随产品版本变化自动重写；必要契约变更应明确新增版本样例与迁移说明。时间、来源路径和生成 ID 不要求转换后保持字节一致；保留原件的要求用 SHA-256 / 字节断言另行验证。
