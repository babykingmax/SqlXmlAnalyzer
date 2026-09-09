# IMP-13 审查修复与加固

2026-09-09：修复 IMP-13 review 的四项 P2 问题，并加固推演索引、并行关系显示、重新绘图及取消传播。本轮新增 17 项回归测试。原始实施的 1524 项历史基线保留在原目录；本轮验证放在 `implementation/IMP-13-hardening/`。

后续 [IMP-09 至 IMP-13 联合审查修复](IMP-09至13联合审查修复与加固说明.md)修正了平行边端点：对偏移后的直线重新求矩形边界交点，沿线留出 3 像素间距，并限制过大偏移、拒绝非有限数。正常 ±16 间距保留，资源/进程组合偏移上限为 24。最新 Debug/Release 各 1579 项通过；下文 1541 项为本阶段历史验证。

## 修复与边界

| 审查问题 | 修复 | 回归验证 |
| --- | --- | --- |
| 每个受害者扫描全部进程，O(V×P) | 进程 ID 哈希集合复用于重复检测及 victim 校验；victim 去重、最小兼容 ID 与缺失警告在一次遍历中产生，检查取消 | 10,000 个进程和受害者、重复与缺失 victim、大小写与序号排序；另有 100,000 项本机测量 |
| 同端点的关系覆盖 WPF 缓存并串用推演状态 | `DeadlockPlaybackEdgeKey(FromId, ToId, EvidenceId)` 贯穿绘图缓存、拖动、推演与步骤标记；同一身份重复绘制幂等；不同身份保持独立 | 真实生产 Canvas 中 5 条关系全部入缓存，步骤 0 全部淡化，两条同端点 owner 分别激活并显示自己的步骤；拖动、退出及重新绘图均验证 |
| RANGE 诊断从通用文本猜测证据，混入资源名称或丢失悬空关系 | 从 `owner.Mode`、`waiter.Mode`、资源 `mode` 构建证据；判断与输出使用同一规范化逻辑。模式大小写与首尾空白被规范化，保留原始字段和来源 | objectname/indexname/id/activity/requestType 冒充 Range、未知模式、缺失 owner/waiter 进程、仅资源 mode、旧直接构图兼容、64 项截断 |
| Mermaid 丢失 IndexName | 资源标签保留类型、SourceId、对象及索引；整个标签经过既有转义 | 同一 TableA 的 IX_TableA 与 PK_TableA 均出现在 Mermaid/HTML 模型中；带引号、换行及尖括号的索引名称不直接插入标签 |

受害者与进程 ID 继续按 `Ordinal` 区分大小写；缺失 victim 仍保留并警告。仅改变校验复杂度，不放宽 XML 或图预算。

每次 `BuildState` 只遍历事件一次，建立关系观察及受害者步骤索引，再投影节点和边，复杂度为 O(事件数 + 节点数 + 边数)。带 EvidenceId 的边必须精确匹配，不会回退到同端点的其他关系。旧的两个参数构造仍可用，并按端点汇总；展示该汇总中已激活事件的最早步骤。

并行关系按照 EvidenceId 稳定分配偏移，源关系重排不会交换位置；总偏移跨度限制为 32 个布局单位，避免连接移出节点。拖动同时更新线、箭头、标签与步骤标记。重新绘图清空步骤标记缓存，防止复用已从 Canvas 移除的控件。大量重叠关系仍受屏幕空间限制，不承诺密集图的所有标签同时可读。

原始 mode 证据以 `owner.mode` / `waiter.mode` / `mode` 命名，带进程、资源和 XML 位置，不把尚未建立有效图关系的原始观察赋予等待边 EdgeId。旧直接构图入口的 RequestedMode/HeldMode 仍受支持，分别归属 waiter/owner。RANGE 的 High 表示实际观测到已知模式，不表示缺失端点已连通或根因已确诊；规则 ID、默认 Medium 等级与配置文件不变。诊断仍最多展示 64 项，所有观测模式参与摘要，超出部分保留在原始资源事实中。

`DeadlockTimelineParser.ParseResult` 将取消令牌传入 `SafeXmlHelper.ParseSafe`；进程、资源及受害者的时间线投影也检查取消。

## 异常、DUMP 与日志

沿用 `ExceptionPolicy` / `UnexpectedErrorReporter`：输入错误、预算失败与用户取消不生成 DUMP；未知错误生成真实 Windows minidump 与 `exception.json`，同一异常对象只捕获一次。DUMP 写入或报告组件失败不能覆盖原始异常。

本轮全量验证包含 `UnexpectedGraphFailure_WritesNativeDumpOnceAndPreservesOriginalException`、DUMP 写入失败、报告器失败、预算/取消和日志测试。`MinidumpValidator` 检查真实 dump 格式；测试结束清理其临时 DUMP，不把进程内存文件提交到文档目录。

Debug 输出 DEBUG、WARN、ERROR、CRITICAL；Release 仅 ERROR、CRITICAL，verbose 不能提升 Release 日志等级。测试同时确认正常分析日志不包含合成 SQL 和未知进程标识。修复路径没有新增 SQL、对象或索引名称的日志输出。

## 验证记录

Debug、Release 的完整解决方案构建均为 **0 警告、0 错误**；完整测试各 **1541 通过、0 失败、0 跳过**。本轮 17 项新增测试位于 `DeadlockUnifiedHardeningTests` 与 `DeadlockUnifiedHardeningViewTests`；原 IMP-13 的 43 项以及既有全部测试一并运行。

```powershell
.\DOCS\implementation\IMP-13\Verify-IMP13.ps1 -OutputDirectory "$PWD\DOCS\implementation\IMP-13-hardening"
# 在新的 PowerShell 进程中运行，避免加载到旧版本程序集：
.\DOCS\implementation\IMP-13-hardening\Measure-IMP13.ps1
```

[验证摘要与源码/程序集 SHA256](implementation/IMP-13-hardening/verification.json)、[Debug TRX](implementation/IMP-13-hardening/debug-20260909T020950614.trx)、[Release TRX](implementation/IMP-13-hardening/release-20260909T020950614.trx)、[性能测量](implementation/IMP-13-hardening/performance.json)。TRX 使用每次运行的新文件名，报告缺失或为空即失败，避免覆写失败时误读旧结果。

本机 Release 单次解析测量：10,000 项约 144 ms、50,000 项约 354 ms、100,000 项约 557 ms；最后一项输入约 5.98 百万字符，只有一个资源和一条潜在依赖。时间从安全读取 XML 后计起，包含事实解析及指纹生成，不包含图构建和 GUI。审查时 100,000 项受害者曾测得约 18.2 秒；前后输入的 SPID 字段不同，因此不将两次记录描述为严格同输入基准。测量用于复核二次扫描已消除，不设依赖机器速度的单元测试阈值。

下图来自生产 WPF 绘图、推演和节点组件的离屏渲染。测试检查每条边对应的缓存/Canvas 元素、步骤、坐标和透明度，并对 PNG 执行非透明像素断言；不把透明空图当成通过。当前桌面没有活动显示设备，证据采集通过 WPF 的 `ShouldRenderEvenWhenNoDisplayDevicesAreAvailable` 开关启用离屏渲染，仅在测试采集环境启用。Debug/Release 分目录保存图片，避免两次运行覆写正在读取的图片。

![全部关系及分别显示的步骤](implementation/IMP-13-hardening/release/deadlock-parallel-relations.png)

![回到步骤 0 后全部关系淡化](implementation/IMP-13-hardening/release/deadlock-parallel-relations-step0.png)

未连接 SQL Server 执行真实并发事务，未生成覆盖率报告，也未做完整应用窗口的人工交互验收。

## 检索依据

先查 Microsoft 官方资料；本轮问题的字段含义可由官方文档直接确认，无需将较低优先级材料当作新的 SQL Server 规则。GitHub 作为成熟实现交叉检查，未复制整段外部代码或引入依赖。

- [Microsoft 死锁指南](https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-deadlocks-guide?view=sql-server-ver17)：资源身份、indexname、owner-list 与 waiter-list 的原始字段。
- [Microsoft 锁定与行版本控制指南](https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-transaction-locking-and-row-versioning-guide?view=sql-server-ver17)：已知键范围锁模式。
- [Microsoft HashSet.Contains](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.hashset-1.contains?view=net-8.0) 与 [.NET 8 HashSet 源码](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/HashSet.cs)：哈希集合查询与成熟实现。
- [First Responder Kit / sp_BlitzLock](https://github.com/BrentOzarULTD/SQL-Server-First-Responder-Kit/blob/main/sp_BlitzLock.sql)：从 owner/waiter 的 mode 与资源 indexname 明确提取字段；仅作结构交叉检查，不将本项目输出冠名为该工具。
- [WPF MediaContext](https://github.com/dotnet/wpf/blob/v8.0.0/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/MediaContext.cs) 与 [CoreAppContextSwitches](https://github.com/dotnet/wpf/blob/v8.0.0/src/Microsoft.DotNet.Wpf/src/PresentationCore/MS/internal/CoreAppContextSwitches.cs)：无活动显示设备时的渲染策略与测试开关。

网页检索工具本轮连接失败，GitHub 插件没有可调用连接器；上述页面通过 HTTPS 直接读取。检索时间、状态与内容 SHA256 见[来源记录](implementation/IMP-13-hardening/sources.json)。
