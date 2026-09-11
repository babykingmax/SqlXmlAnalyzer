# IMP-23 审查修复与加固

日期：2026-09-10。修复审查发现的 5 项问题；本轮新增 21 项回归，IMP-23 专项共 66 项。Debug/Release 全量各 2480 通过、0 失败、0 跳过，完整构建均 0 警告、0 错误。工作区源码和验证产物的 SHA-256 见[验证摘要](verification/IMP-23-hardening/summary.json)。

## 修复行为

| 问题 | 修复与回归 |
| --- | --- |
| 对称图反复嵌套序列化，少量输入也可能耗尽内存 | `structure-v2` 使用带长度前缀的分块 SHA-256 标签，统一编号并复用整数缓冲区比较完整拓扑；不为每个排列重复复制属性文本。超出标签、属性数或比较量预算时独立分组，继续提供搜索和追溯。保留六节点环与两组三节点环不能误合并的回归。 |
| 数据库名称筛选遗漏 `currentdbname` | 从死锁 XML 提取该属性，与已有 ID、字段/动作一起参与包含匹配；进程结构标签同时保留该名称，避免不同已采集数据库名称误合并。两个真实 XEL 按各自采集名称检索均命中 1 个事件。 |
| 追加文件重建目录会覆盖被修改事件的指纹 | `XelSearchSource` 保留首次接受的基准；追加前、构建前后及导航时校验。事件成员替换、分析 XML、原始 XML、采集字段/动作、源字节变化均拒绝，原有基准不被重新计算的内容覆盖。首次接受还核对规范化副本与原始元素的一致性，兼容读取器的命名空间规范化。 |
| 合法无死锁滚动文件令整批失败 | 仅接纳明确的 `Unsupported / INPUT_XEL_NO_DEADLOCK`，且无死锁及无 `xml_deadlock_report` 记录的 XEL。保留文件、未处理记录和通知，空文件计为已载入的零事件来源；损坏、超限、未知失败和其他 Unsupported 仍拒绝整次导入。 |
| 首次载入期间筛选取消初始输入，误报 0 文件成功 | 初始化完成前禁用添加/筛选控件，ViewModel 入口也拒绝抢占。首次载入取消后可重新导入；已有完整目录时继续支持新查询取消过期追加操作，旧任务不能覆盖新结果。 |

快照校验失败属于已知输入失效，显示“事件快照已改变”并要求重新载入；追加失败保留上次结果和预览。当前输入取自主窗口已有快照；若磁盘内容更新，须先关闭检索窗口，在主窗口重新打开文件，再打开检索窗口。额外来源重新添加；单纯重开检索窗口不保证重读当前输入。

## 聚合边界与兼容性

每事件最多 128 进程、128 受害者项、512 资源、4096 依赖、720 排列；另在标签排序和复制前检查 **1 Mi 结构标签字符、16,384 属性**，并限制排列数乘候选长度不超过 **2,000,000 个整数项**。标签预算保守统计进程/受害者属性及资源子树的名称和属性，SQL 文本不作为结构标签。候选长度为进程数 + 2 × 资源数 + 2 × 依赖数。超过任一聚合预算仅影响分组，事件仍完整保留；集合/读取预算超限仍拒绝整次导入。

结构键版本由 `structure-v1` 改为 `structure-v2`，会改变具体键值。窗口没有持久化这些键，重新构建即可；不要把键解释为事故 ID 或根因证明。属性值保留大小写和空格，忽略局部 ID/SPID，完整保留支持资源的属性、owner/waiter 方向、平行依赖、隔离级别、受害者及已采集数据库名称。

原有集合限制（32 文件、10,000 记录、128 MiB、32 Mi 字符）、UTC 排序、时间端点包含、重复副本保留和原始位置保持不变；没有新增诊断规则 ID 或更改严重度。

## 依据

按要求先核对 Microsoft 官方资料；已能直接确认字段与文件行为，无需用社区推断替代。GitHub 插件的连接器本轮没有可调用接口，公共源码通过 GitHub API 核对。

- Microsoft [SQL Server deadlocks guide](https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-deadlocks-guide?view=sql-server-ver16) 的 XML 示例含 `currentdb` 和 `currentdbname`。采用报告已有名称，不反查数据库或猜测名称。
- Microsoft [Targets for Extended Events：event_file](https://learn.microsoft.com/en-us/sql/relational-databases/extended-events/targets-for-extended-events-in-sql-server?view=sql-server-ver16#event_file-target) 说明目标可写入多文件并滚动保留。允许可解码但不含目标事件的文件参与集合是本项目的处理策略；不代表损坏文件有效。
- Microsoft [.NET IncrementalHash.AppendData](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.incrementalhash.appenddata?view=net-8.0) 提供追加块的 API；据此采用 4096 字节缓冲区处理 UTF-8，并以标准 SHA-256 和跨块 Unicode 回归核对结果。
- [.NET 8 IncrementalHash 源码](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Security.Cryptography/src/System/Security/Cryptography/IncrementalHash.cs) 核对 `ReadOnlySpan<byte>` 到哈希提供器的追加与最终化路径；Git blob `022476b0bd3cc752d96417b161b0bea77a6e4bd4`。图规范化与预算值是本项目实现决策，并非 Microsoft 推荐的图算法或性能阈值。

## 验证与限制

- 21 项新增回归覆盖五个缺陷及源字节/元数据修改、命名空间、未知格式、空来源、UTF-8 边界、标签和比较量预算；保留原有 DUMP、日志、取消、时间、重复和拓扑回归。
- [Debug 内存记录](verification/IMP-23-hardening/memory-Debug.json) / [Release](verification/IMP-23-hardening/memory-Release.json)：7 进程、16,377 字符的样例从审查时约 224 MiB 分配降到约 0.70 MiB。这里是当前线程累计分配量，不是峰值工作集；本轮在预热后测量。8,388,955 字符的大属性样例在 256 MiB 托管堆限制下完成构建、搜索及追溯校验，按标签预算单独分组；此前相同堆限制下出现 OutOfMemoryException。
- [真实 XEL Debug](verification/IMP-23-hardening/real-xel-Debug.json) / [Release](verification/IMP-23-hardening/real-xel-Release.json)：复用前轮 SQL Server 2019 独立测试采集，两文件共 6 记录、2 死锁、4 未处理记录、1 组；增加数据库名称命中断言，继续核对时间、位置和损坏文件拒绝。
- [WPF Debug](verification/IMP-23-hardening/wpf-Debug/verification.json) / [Release](verification/IMP-23-hardening/wpf-Release/verification.json)：生产视图和事件处理器通过，增加初始化时禁用筛选/添加以及程序触发筛选不能丢失初始目录的断言，绑定错误 0。
- 未知异常仍生成经过格式验证的真实 Windows DUMP 和侧车，失败明确报告；Debug 记录 DEBUG/WARN/ERROR/CRITICAL，Release 仅 ERROR/CRITICAL。本次两配置全量测试均包含这些诊断用例。

标准中间目录曾被 Google Drive 锁定，由 Windows Restart Manager 确认。本次使用独立 `IntermediateOutputPath` 完成全量构建，命令见[复验说明](verification/IMP-23-hardening/README.md)。未改项目编译选项或停止同步程序。

WPF 证据来自离屏测试宿主；前轮桌面交互工具访问受限的人工鼠标/键盘验收仍保留。合成内存回归不替代大规模采集性能验收，未生成覆盖率报告，也不据此关闭 IMP-26 或 M6 全部门槛。
