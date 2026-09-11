# IMP-24 审查修复与加固

2026-09-10。修复 IMP-24 审查确认的四项缺陷，不新增规则 ID 或修改默认严重度。

## 修复与行为

| 问题 | 修复后的行为 | 回归证据 |
| --- | --- | --- |
| 加载 B 时取消，然后重算 A，混用两份来源 | 文档、路径和输入信息通过 `PlanAnalysisSource` 随成功结果一起提交；文件打开和历史记录打开均不提前改写当前来源。重算捕获这一个对象；控制器拒绝文档与输入对象不匹配。 | `PlanAnalysisSourceTests`；实际 WPF 中阻塞 B、应用配置取消 B、重算 A |
| 改写分析重新读取已变化的文件 | `ApplicationOrchestrator.Execute` 新增可选 `planInput`，桌面传入同一份已读取输入，改写与规则诊断使用相同 XML。原 CLI 路径调用仍沿用文件读取。 | 源文件替换、删除后仍分析 A，且不读取原路径；WPF 将文件内容由 A 改为 C 后重算 |
| 长属性名与宽数组造成路径分配放大 | JSON 遍历只维护深度不超过 32 的路径片段，在报错时才格式化；单个属性显示最多 64 字符，完整路径最多 1024 字符并附省略标记。保留 1 MiB、1024 规则及最多 64 个重复属性错误的限制；遍历支持取消。 | 长祖先、5000 个元素的分配测试；256 MiB 托管堆下的近上限输入探针 |
| 配置切换误取消死锁或文件读取 | 会话记录内容类型；识别为执行计划后才受规则切换取消。未知类型读取、死锁、XEL 请求保留。请求 ID 校验与取消处于同一锁内，旧请求不能取消或重新分类新请求。 | `AnalysisSessionCoordinatorTests`；实际 WPF 应用回调保留死锁请求 |
| 非法 Unicode 被归为未知故障并生成 DUMP | 在 JSON 属性名和字符串解码边界，将非法代理项转换为带位置的 `InvalidDataException`；同时校验未知扩展值，防止保存时才失败。拒绝未转义的无效 UTF-16，正常代理项对可往返保存。 | 字段、属性名、未知值及合法补充字符测试；真实加载器和编辑器均不调用 DUMP 写入器，失败保留草稿 |

“重新分析当前计划”使用上次成功分析的内存输入，包括其原有来源信息。磁盘文件发生变化时，需重新打开文件才会使用新内容。应用规则配置只取消正在执行的计划分析；主动打开其他文档或主动重算仍遵循原有的新请求替代旧请求规则。

未知程序异常仍走统一诊断策略生成 Windows minidump；仅将已知的损坏 Unicode 输入归为校验失败。Debug 输出调试、警告、错误、致命日志，Release 仅错误与致命日志。

## 验证结果

- 新增 **22 项**回归测试，并调整历史记录打开测试以验证来源只在成功后提交。
- Debug、Release 全量各 **2549 通过、0 失败、0 跳过**；解决方案构建各 **0 警告、0 错误**。
- 初始 JSON 回归在修复前为 7 失败、1 通过，修复后通过；新增用例覆盖内存放大、长错误路径、取消、Unicode 和来源一致性。
- 两种构建模式的实际 WPF 宿主流程均通过，绑定错误为零。验证入口、编辑、保存冲突、默认恢复、图与列表共用上下文，以及 A/B 交错取消和改写快照一致性。
- 既有真实 DUMP 成功/失败、异常去重、诊断组件失败及分模式日志测试在两种模式中重新通过；新增损坏 Unicode 用例验证不生成 DUMP。

Release 内存探针在 .NET 8.0.30、256 MiB 托管堆限制下运行生产解析器，JSON 依赖沿用仓库中央包配置。下表是累计分配量，不是峰值驻留内存；时间仅为本机单次观测。

| 输入 | 字节数 | 累计分配 | 耗时 |
| --- | ---: | ---: | ---: |
| 8192 字符属性名、5000 个元素 | 18,209 | 182,488 字节 | 1.74 ms |
| 400,000 字符属性名、250,000 个元素 | 900,017 | 10,015,056 字节 | 35.72 ms |

第一次审查的同尺寸小输入曾分配约 83 MB；本次改为按输入规模遍历，不再为每个子项重复复制祖先名称。

源码摘要、构建/测试计数、复现脚本与截图见[验证入口](verification/IMP-24-hardening/README.md)和[机器可读结果](verification/IMP-24-hardening/summary.json)。本次没有数据库行为变更，不宣称 SQL Server 性能验证。WPF 宿主不替代真实桌面键盘、DPI、主题和屏幕阅读器验收。

## 检索依据

按 Microsoft 官方资料优先的顺序检索。本次缺陷属于 .NET 取消、JSON 解码和 WPF 输入生命周期，官方资料及官方运行库源码已足够定位原因，因此没有引入 SQL Server 社区中的替代实现。

- [Microsoft：托管线程中的取消](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads)：取消由调用方发起并由接收方协作处理；本项目据此将配置取消限定到相应计划请求。
- [Microsoft：GC 堆硬限制](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector#heap-hard-limit)：内存探针通过 `System.GC.HeapHardLimit` 设置托管堆限制，并记录运行时实际可用额度。
- [dotnet/runtime v10.0.3：JSON Unicode 解码源码](https://github.com/dotnet/runtime/blob/v10.0.3/src/libraries/System.Text.Json/src/System/Text/Json/Reader/JsonReaderHelper.Unescaping.cs)：与当前 `System.Text.Json/10.0.3` 依赖对应，非法代理项会抛出 `InvalidOperationException`；该异常需在已确认的字符串解码边界转换，不能全局吞掉同类型程序错误。

Web 检索工具连接失败，GitHub 插件没有提供可调用连接器；随后直接通过 HTTPS 读取 Microsoft 官方页面和 GitHub 官方原始源码，均返回 200。未修改 GitHub 远程仓库。
