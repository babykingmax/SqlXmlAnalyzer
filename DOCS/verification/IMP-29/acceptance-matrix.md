# IMP-29：R / D / UI 验收矩阵

日期：2026-09-10。对象：本地累积工作树，版本由 [sources.json](sources.json) 和 [artifacts.json](artifacts.json) 标识；不是已发布版本。

审查修复后，自动化证据以 [build-evidence.json](evidence/build-evidence.json) 与各阶段 attestation 绑定执行版本；新增 25 项脚本回归，详见[修复说明](../../IMP-29审查修复与加固说明.md)。可见桌面注入记录属于原实施，生产报告窗口未在本次更改，不能把它宣称为新增实机覆盖。

“关闭”仅表示该行所列问题在已声明支持范围内达到原规划的最小关闭条件；不表示任意 SQL/Schema/部署环境均已验证。“缓解”表示默认保护及有限验证可用，完整语义或发布恢复能力仍有限制。“部分验收”表示具体用户环境验收尚未完成。所有行均关联本轮测试/探针证据；没有把历史通过直接当作本轮通过。

证据索引：T = [双配置完整测试及 12 项新增测试](summary.json)，原 TRX/hash 在 [artifacts.json](artifacts.json)；W20–W27 = [Release WPF 记录](evidence/release/wpf-20.json)及对应编号（Debug 同目录结构）；SQL = [语义](evidence/release/sql-semantics.json)、[观察器](evidence/release/sql-observer.json)、[类型](evidence/release/sql-types.json)、[真实写回](evidence/release/sql-apply.json)；DDL = [2019](evidence/release/index-15.0.json)、[2025](evidence/release/index-17.0.json)；XEL = [真实事件检索](evidence/release/xel.json)。两种配置的证据均被摘要校验。

| 编号 | 本轮结论 | 已验证行为与证据 | 保留边界 |
| --- | --- | --- | --- |
| R01 脱敏遗漏 | 关闭 | T：Privacy / DiagnosticReport / DiagnosticPackage；[W22](evidence/release/wpf-22.json)、[W27](evidence/release/wpf-27.json)：跨格式无样本标记泄露，未知字段阻断 | 仅声明支持的字段；显式原始附件仍含敏感信息 |
| R02 TRIM 改写语义 | 缓解 | T：RewriteProposal / Semantic；SQL 的字符串/空白/排序规则反例和候选审核 | 不开放前提不明的自动 TRIM 转换；有限场景不证明一般等价 |
| R03 备份失败仍写回 | 关闭 | T：写回故障、并发、取消、通知失败；SQL apply：真实备份、精确输出、BOM、报告失败保留提交状态 | 发布包恢复演练属于 IMP-30 |
| R04 无效计划通过 CLI | 关闭 | [独立 CLI 进程](evidence/release/cli-invalid.json)：错误根、损坏 XML、DTD 均 Failed/退出 1；兼容正常计划退出 0 | 取消退出 130 由 T 验证，未用 OS 信号模拟 |
| R05 死锁包装入口不一致 | 关闭 | T：InputRecognition / DeadlockEventSelection；W20 和 XEL：多事件选择与来源一致 | 不支持的包装明确拒绝 |
| R06 priority 未读取 | 关闭 | T：InputFieldMapping / DeadlockParseContract；W20：字段和选中项证据 | 缺失优先级不推断为 0 |
| R07 范围锁误判 | 关闭 | T：DeadlockUnified / Pattern；W20：观测锁模式与假设分列 | 不由单次死锁快照证明根因 |
| R08 重叠环成员遗漏 | 关闭 | T：DeadlockUnifiedGraph / Hardening；W20：图、选择、推演成员共享 | 超预算明确截断，不声称全枚举 |
| R09 并行基数计算错误 | 关闭 | T：Cardinality / OperatorFacts；W20、W22：累计值与每次执行口径一致 | worker 执行次数不一致时不比较 |
| R10 残差谓词遗漏 | 关闭 | T：Residual / OperatorFacts；W20：第二语句、算子、证据定位 | 残差存在不直接换算 IO/耗时 |
| R11 执行属性映射错误 | 关闭 | T：InputFieldMapping / OperatorFacts；W20、W22 | 未采集字段仍为未知 |
| R12 模拟顺序依赖 | 关闭 | T：Simulation / IndexScoring；[W21](evidence/release/wpf-21.json)：假设和评分限制可见 | 工具评分不代表 SQL Server 实测收益 |
| R13 引用标识符被拆分 | 关闭 | T：IndexDdl；DDL：点号、方括号转义、中文/空格对象真实 CREATE/ROLLBACK、键顺序 | 未做所有版本/版本功能组合覆盖 |
| R14 索引跨对象误关联 | 关闭 | T：IndexTarget / PlanIdentity；DDL：跨库/架构同名对象、错误服务器阻断；W21 对象审核 | 不自动猜测服务器排序规则 |
| R15 缺失指标按 0 比较 | 关闭 | T：PlanComparison / 新增会话往返场景；W21：缺失成本 N/A，无虚假改善率 | 捕获环境差异仍限制可比性 |
| R16 仅比较首语句 | 关闭 | T：MultiStatement / SelectionIsolation / 新增场景；W20、W21：选择、配对、未匹配项和保存恢复 | 重复 SQL 的不唯一配对须明确选择 |
| R17 父节点混入子节点属性 | 关闭 | T：OperatorFacts / scoped parsing；W20：本节点详情和源 XML | 不跨 RelOp 借用子节点事实 |
| R18 同一环重复计数 | 关闭 | T：DeadlockUnifiedGraph；W20：共享环分析 | 不同资源边不能合并为一个事实 |
| R19 表变量回滚语义变化 | 缓解 | SQL：rollback、嵌套事务、savepoint、TRY/CATCH；T：默认限制与提案 | 不普遍自动转换表变量 |
| R20 临时表名称冲突 | 缓解 | SQL：名称冲突、多个声明；T：作用域/预算/只清理自建对象 | 受支持转换才可进入验证，动态 SQL 不作普遍保证 |
| R21 实际行绑定读取行 | 关闭 | 新增场景与 T：输出 10000 / 读取 100000；W20、W22：图、表、详情、报告一致 | 缺失读取值不可补 0 |
| R22 合成步骤误称真实回放 | 关闭 | T：DeadlockPlayback / Display；W20、可见主窗口：明确“依赖推演” | 快照没有真实时间线 |

| 编号 | 本轮结论 | 证据与剩余条件 |
| --- | --- | --- |
| D01 输入类型契约 | 关闭 | T、独立 CLI、W20、真实 XEL：Success/Partial/Invalid 和取消区分 |
| D02 完整身份与上下文 | 关闭 | T、W20、W21、DDL、真实 XEL 字节追溯 |
| D03 XML 作用域及指标一致性 | 关闭 | T、W20、W22、新增跨模块作用域场景 |
| D04 共享死锁算法 | 关闭 | T、W20、真实 XEL 所选事件分析 |
| D05 事实/假设/建议与运行状态 | 关闭 | T、W20、[W24](evidence/release/wpf-24.json)：不可变结果、配置变更待重算 |
| D06 SQL 改写语义与审核 | 缓解 | SQL + W21；只证明列出的有限场景，不宣称一般语义等价 |
| D07 文件写回失败边界 | 缓解 | T + SQL apply 的本地文件边界已通过；IMP-30 发布/升级/恢复演练未执行 |
| D08 脱敏及输出边界 | 关闭 | T + W22/W27 + 可见窗口实际保存；原始附件必须显式选择 |
| D09 实测/估算/假设区分 | 关闭 | T + W20/W21/W22；N/A、计算方法和假设保留 |

| 编号 | 本轮结论 | 用户场景与证据 | 尚未验收 |
| --- | --- | --- | --- |
| UI-01 指标统一 | 关闭 | T、W20/W22，输出/读取行跨模块往返 | — |
| UI-02 语句/事件上下文 | 关闭 | W20、XEL、新增两语句/两事件场景、可见窗口切换第二语句 | — |
| UI-03 问题与节点联动 | 关闭 | W20、可见窗口 F8 定位、SQL/XML 证据 | — |
| UI-04 死锁事实与推演 | 关闭 | W20、真实 XEL，固定快照说明 | — |
| UI-05 改写审核 | 关闭 | W21 逐项提案、A/B、索引；SQL 独立进程验证/写回；T 通知失败 | 能力限制另列 D06 |
| UI-06 A/B 比较 | 关闭 | W21 未匹配/采集差异；T 会话保存恢复后双语句配对 | — |
| UI-07 脱敏预览 | 关闭 | W22/W27、可见审核与新文件保存，格式/范围均可见 | — |
| UI-08 取消与错误恢复 | 关闭 | [W26](evidence/release/wpf-26.json) 初次提交/换语句/分页取消；T 写入重试和 DUMP | 不覆盖所有 OS 崩溃或硬件故障 |
| UI-09 索引及模拟假设 | 关闭 | W21 对象/选项/风险可见，DDL 双版本真实目标校验 | — |
| UI-10 统一报告 | 部分验收 | W22 多格式内容一致；本轮修复长报告布局，原生保存通过 | PDF/Word/SSMS/浏览器外部阅读器的完整呈现矩阵 |
| UI-11 XEL 搜索聚合 | 关闭 | [W23](evidence/release/wpf-23.json)、双配置真实 XEL 筛选/聚合/追溯 | 超大现场采集仍受预算限制 |
| UI-12 规则配置 | 关闭 | W24 当前命令入口、重算/冲突/取消；兼容转换 22 检查、T 字节预算 | — |
| UI-13 布局主题 | 部分验收 | [W25](evidence/release/wpf-25.json) 主题/尺寸/缩放位图；长报告普通/紧凑回归与可见窗口 | 实际 OS DPI 切换、多显示器搬移 |
| UI-14 键盘可访问性 | 部分验收 | W25 路由/AutomationPeer；桌面接口注入 Ctrl+O、F8、Ctrl+R、Escape | 实体键盘端到端、Narrator/NVDA 等辅助技术；注入不等于硬件操作 |
| UI-15 大图性能 | 部分验收 | [冷/热测量](summary.json)、W26 页面预算/取消完成责任 | 100 ms P95 候选目标没有全面通过；无约定 SLA、无统计置信结论 |
| UI-16 诊断数据包 | 关闭 | W27 默认附件、审核固定字节、新 ZIP 和 SHA 校验；新增失败重试场景 | 不能把包内 hash 当作真实性签名 |

合计：38 项在声明范围内关闭，5 项缓解，4 项部分验收。M7 不标为全面关闭；发布/回滚工作 IMP-30 未执行。本表中的部分验收项须补充对应实机/阅读器/性能证据，不能由单元测试数代替。
