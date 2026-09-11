# IMP-28：模型、配置、报告与 CLI 兼容性

审查修复（2026-09-10）：已修复配置迁移时缩进及字符转义导致的膨胀，增加原始内容保留与精确 UTF-8 预算保护。新增 15 项回归，Debug/Release 全量各 2754 通过；见[修复说明](IMP-28审查修复与加固说明.md)及[最新验证](verification/IMP-28-hardening/README.md)。下列 48 项新增测试及 2739 项全量数据为初版结果。

实施日期：2026-09-10。基线为 IMP-27 审查加固后的本地 Release 产物，补充 IMP-01 历史配置和按历史写入结构重建的 2.0 会话。来源与程序集指纹见[样例说明](verification/IMP-28/fixtures.md)。这些样例均为合成数据。

本步新增 48 项测试。Debug/Release 全量各 2739 项通过，构建各零警告、零错误；实际 CLI 进程及文件转换每配置通过 22 项检查。结果、转换产物和失败恢复证据见[验证记录](verification/IMP-28/README.md)。本步不替代 IMP-29 用户场景验收或 IMP-30 发布回退演练。

## 格式兼容矩阵

“直接读取”表示当前正式入口可处理；“迁移”表示生成可重新读取的新副本；“保留原件”表示本版本拒绝或仅供外部查看，不承诺恢复为可编辑模型。

| 数据及版本 | 当前处理 | 未知字段 / 失败策略 | 证据或使用入口 |
| --- | --- | --- | --- |
| 规则配置：旧格式，无 `ConfigurationSchemaVersion` | 直接读取；可选迁移为版本字符串 `"1"` | 未知规则、根/规则扩展保留但不执行；别名保留原拼写 | `RuleConfigurationStore`；历史配置、扩展字段和别名测试 |
| 规则配置：`ConfigurationSchemaVersion: "1"` | 直接读取和编辑 | 大小写不敏感读取；歧义重复字段拒绝；未知非版本字段继续保留 | `RuleConfigurationDocument` |
| 规则配置：未来版本、非字符串或空版本 | 保留原件，拒绝应用 | `CONFIG_SCHEMA_UNSUPPORTED`；不替换活动配置；CLI 退出 2 | 配置恢复测试、实际 `future-config` 输出 |
| 规则配置：仅因添加版本字段而真正超过 1 MiB | 原无版本配置仍可直接读取，拒绝生成超限副本 | `CONFIG_MIGRATION_BUDGET_EXCEEDED`；不创建目标或更改源配置 | 修复后的 UTF-8 字节边界及保存失败回归 |
| 会话：正确命名空间、无版本或 `Version="2.0"` | 直接读取，状态栏提示另存；保存新副本为 2.1 | 原始 hash 仍按历史可信度处理，不提升为已验证来源；原件不变 | 旧会话加载、A/B 选择、转换样例 |
| 会话：`Version="2.1"` | 直接读取，写入新副本 | 校验内容 hash、快照 ID、比较引用与选择；保留已有方法重载及任意文件扩展名调用方式 | 升级前实际 2.1 样例及既有会话测试 |
| 会话：其他声明版本、未知管理字段、重复 ID / 容器、悬空引用 | 保留原件，拒绝读取 | `SESSION_VERSION_UNSUPPORTED`、`SESSION_EXTENSION_UNSUPPORTED`、`SESSION_ID_INVALID` 等；载入失败保留界面历史与 A/B | 结构与恢复测试；实际未来版本失败样例 |
| 执行计划 / 死锁 XML；内存模型 | 经 `SafeXmlHelper` 和统一识别入口直接读取；保留旧适配器 | 不把整个 C# 对象的任意 JSON 当作持久化格式；算子使用复合身份 | 旧/新计划诊断和死锁包装双路径比对 |
| IMP-22 报告 `SchemaVersion="IMP22-1.0"` | JSON / 文本 / HTML 导出供读取；旧字段保留 | 本程序无报告导入恢复入口；未知未来版本应保留原件，外部消费者自行校验版本 | 冻结报告字段类型与旧消费者反序列化测试 |
| CLI scan / read / refactor JSON | 保留各自数组或对象外形、旧字段及参数别名 | 新字段采用增量扩展；不新增统一外层包装；不承诺属性顺序、时间和绝对路径一致 | 冻结输出、旧消费者及真实进程输出 |
| `--read-options` 输入预算配置 | 既有严格字段契约 | 未知字段拒绝，`INPUT_OPTIONS_INVALID` / 退出 2，避免拼错预算后静默使用默认值 | CLI 回归 |
| IMP-27 诊断包 `IMP27-1.0` | 继续按包校验器读取和审核 | 不是可还原会话或应用备份；不提供跨未知版本自动迁移 | 既有 46 项诊断包测试在全量中保留 |

配置版本使用专用名称 `ConfigurationSchemaVersion`。全量回归证明旧配置已有数值型 `schemaVersion: 9` 扩展，因此不能把通用 `SchemaVersion` 突然解释成必需遵循的新协议。该旧扩展在编辑和转换后保持原值；只有专用字段参与版本校验。迁移不改变有效配置指纹，也不会自动改写载入的文件。

会话扩展的处理不同于 JSON 配置：现有快照模型不能无损存储未知管理字段，因此遇到这类字段会拒绝加载，避免另存时静默丢失。内嵌 Showplan 内容仍完整保存并交由既有安全解析和身份模型处理；新保存文件接受当前读取预算（最多 1024 个快照、64 MiB 输入字节、32 MiB XML 字符）。

## 迁移与失败恢复

1. 载入旧配置或会话时保留源文件。会话 `SourceVersion` 和 `RequiresMigration` 公开旧格式状态；GUI 在载入成功信息中提示另存。
2. 会话请选择新名称，如 `session-upgraded.pesession`。保存会先校验结构和可重新读取性，再在目标目录下的私有临时目录写入、落盘、校验 SHA-256，最后以 `overwrite: false` 移动到目标。默认扩展名仍为 `.pesession`，既有服务调用可以使用其他扩展名。
3. 配置无须迁移即可继续使用。需要显式版本副本时，对载入的 `RuleConfigurationDocument` 调用 `WithCurrentSchema()`，再用 `RuleConfigurationStore.SaveAsAsync` 保存新名称；没有新增 GUI 或 CLI 迁移命令。普通配置编辑/原位保存仍使用 IMP-24 的备份和并发修改保护。
4. 磁盘写入异常、提交前取消、临时内容损坏或目标被并发创建时，不发布部分会话，也不覆盖源文件或并发目标。清理错误通过公共异常机制记录；不能清理时不掩盖主错误。实际转换脚本每次创建独立结果目录。
5. 未来版本或不支持的扩展请用匹配版本的程序打开。不要删除版本字段尝试降级。失败后可继续使用保留的原件；本步没有把“换回旧应用”当作已完成的发布回退验收。

转换样例位于验证目录每次运行的 `converted.json` 和 `converted.pesession`；同目录包含 `legacy.*`、`future.*`、旧报告和错误输出。[Debug](verification/IMP-28/latest-Debug.json) / [Release](verification/IMP-28/latest-Release.json) 索引给出精确路径和 SHA-256。2.0 会话转换会生成当前保存时间、快照标识和内容 hash；保留计划内容、捕获时间和 A/B 对应关系，不承诺 XML 字节或生成 ID 不变。

## 外部调用方需要知道的变化

| 契约 | 保留内容 | 调整或限制 |
| --- | --- | --- |
| 会话保存 | 无参构造、原 Save 重载、Load 和 CaptureSnapshot | 不再覆盖已有目标；需改为新文件名，已有目标报 `SESSION_DESTINATION_EXISTS` |
| CLI 参数 | `-p/--path`、`-f/--format`、`-o/--output`、read、refactor 等 | 修正帮助：默认配置来自应用目录；显式相对 `--config` 仍相对当前目录 |
| CLI 退出码 | 成功 0；扫描失败/阈值不通过 1；用法/配置/输出失败 2；取消 130 | 0 不代表 SQL 已写入；取消 130 为入口令牌测试，未新增真实 Ctrl+C 进程验收 |
| scan JSON | 顶层数组与 `FileName/Status/MaxSubtreeCost/ContainsScans` 等旧字段 | 缺指标时不能将旧数值字段单独当作完整证据；同时读取状态和缺失清单，见既有 CLI 加固说明 |
| refactor JSON | 对象、`ProposalSchemaVersion="1"`、审核与验证字段 | IMP-18 起默认仅候选，`SourceWritten=false`；需显式选择并按 IMP-19 验证/应用流程，不能恢复旧自动写回行为 |
| 规则目录 | 34 个 ID、规则版本、默认严重度与 IMP-27 加固基线一致 | `RULE_016_WAIT_STATS` 为配置别名，执行 ID 为 `RULE_036_WAIT_STATS`；严重度值为规则协议，和日志级别分开 |
| 模型 / 报告 | 老字段和适配器继续服务现有 GUI、Analysis、Application 与 CLI | `NodeId` 不是全局唯一键；使用完整位置；未知指标读状态，不推断为 0；报告导出不能用于恢复会话 |

CLI 及模型的较早行为变更仍见 [CLI 扫描与报告加固](CLI扫描与报告加固-2026-09-09.md)、[IMP-17 比较](IMP-17多语句与可比性检查的AB比较.md)、[IMP-18 提案](IMP-18可审核SQL改写提案.md)及 [IMP-19 验证与应用](IMP-19SQL语义验证与可靠应用.md)。本步未修改规则结论、严重度或改写算法。

## D01–D09 追踪与适配层

| 设计契约 | 当前消费者与本步证据 | 尚未据此宣称的结论 |
| --- | --- | --- |
| D01 输入类型 | `InputRecognitionService`、read CLI；旧死锁包装 / 显式事件比对 | 不替代所有 XEL 和 SQL Server 版本验收 |
| D02 身份上下文 | `PlanIdentityAdapter`、比较视图、Analysis；ID/位置与 A/B 恢复测试 | 不以裸 NodeId 作为跨语句身份 |
| D03 指标一致性 | Core 诊断、Analysis 和报告；双路径字段、成本状态测试及既有指标回归 | 不把缺失值解释为已测量的 0 |
| D04 共享死锁算法 | 旧解析入口与选中事件入口；进程优先级及资源比对 | 不新增死锁算法或在线追踪验收 |
| D05 规则运行状态 | `AnalyzeDetailed` → `ToLegacyResults`、`FromDiagnostics`；规则目录冻结 | 旧 Issue 列表本身不能表达所有跳过/缺证据状态 |
| D06 改写审核 | CLI / Application / WPF；候选 JSON 与源 SQL 不变测试 | 不重复宣称数据库语义等价验收 |
| D07 文件写回 | 会话 writer、配置 store、CLI 输出；部分写入、取消、并发和原件 hash | 不保证突然断电或损坏存储介质后可恢复 |
| D08 输出隐私 | IMP-22 报告 / IMP-27 包；既有完整回归保留 | 普通报告、会话原文、日志和 DUMP 不自动成为匿名数据 |
| D09 指标口径 | 比较、规则、报告及 scan 状态字段；当前字段消费者测试 | 不证明模拟收益或性能目标达成 |

没有充分证据证明旧接口已无消费者，本步保留 `PlanDiagnosticAnalyzer.AnalyzePlan`、`ToLegacyResults`、`SqlXmlAnalysisEngine.FromDiagnostics`、`PlanIdentityAdapter`、死锁旧入口与 `RuleConfigurationDocument.ToLegacy`。双路径比对针对冻结样例和既有测试，并非任意第三方二进制的 ABI 保证。

## 异常与日志

会话加载、保存和清理边界复用 `ExceptionPolicy` / `UnexpectedErrorReporter`。格式、版本、路径、I/O 和取消按预期失败处理；未知异常调用 `WindowsMiniDumpWriter`，生成原生 DUMP 与诊断侧车记录，同一异常去重。DUMP 创建失败会记录原因，原异常继续传播，不把失败伪装成保存成功。配置和 CLI 继续使用各自已有公共诊断边界。

Debug 记录 Debug、Warning、Error、Critical；Release 仅记录 Error、Critical，显式 `forceVerbose` 也不能绕过 Release 门禁。IMP-28 新日志只记录阶段、数量和错误，不主动写入快照 SQL。测试实际校验 Windows minidump、失败侧车、去重及两种构建的日志级别；测试 DUMP 由测试清理。

## 参考依据

优先查阅 Microsoft 官方资料（2026-09-10）：[System.Text.Json 未映射字段及 DOM](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/handle-overflow)说明额外字段需要显式保留；本实现延用 JSON DOM 保存未知内容。[File.Move](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move?view=net-8.0)提供不覆盖目标的移动语义，[FileStream.Flush(Boolean)](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=net-8.0)用于提交缓冲数据。本步只涉及本地 .NET 文件与契约行为，无需引入 SQL Server 社区替代方案。
