# IMP-26 验证记录

日期：2026-09-10。范围及实现见 [IMP-26](../../IMP-26异步状态取消与大图性能.md)，计数与哈希见 [summary.json](summary.json)。当前工作区尚未提交，前置 IMP-23/24/25 修改保留。

## 构建和单元测试

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug -m:1
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build
dotnet build SqlXmlAnalyzer.sln -c Release -m:1
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release --no-build
```

两配置完整构建均零警告/错误，全量各 2629 项通过，零失败/跳过；相对 IMP-25 加固基线新增 32 项。日志在本目录 build/tests 文件，完整 TRX 保留于工作区 `.tmp.imp26-results`，摘要保存其 SHA-256。未运行覆盖率统计。

未知错误测试调用实际 WindowsMiniDumpWriter，并使用 MinidumpValidator 校验转储；还覆盖转储失败、同异常去重和取消回调异常。状态日志测试验证 Debug 四类级别、Release 仅 Error/Critical，包含运行时强制 verbose 的门禁。DUMP 和异常侧车是临时测试文件，测试结束清理，不放入发布物。

## 性能方法与最终数据

硬件、系统、SDK 见 [hardware.json](after/hardware.json)。均为 Release、1280×720 DIP、离屏实际 WPF 窗口、软件渲染。每个规模单独启动进程，连续加载两次：`process-cold` 是该进程首次分析，`same-process-warm` 为第二次；未清除操作系统文件缓存。耗时不含窗口初始化，另记 StartupMs。

计划样本为脚本生成的平衡二叉 Concatenation 树，固定估算行数与成本，没有运行时采集计数；缺失证据诊断仍照常运行。该样本用于比较规模和控件创建成本，不能代表不同实际 SQL、算子类型或执行计划深度的负载分布。

20 ms 定时器向 Dispatcher Input 队列投递探针，计算本次运行的等待延迟分位数。P95 是单次运行内的输入队列采样分位数，不是多轮基准统计，也不是实体键盘到像素更新的延迟。样本数量、最大值和各阶段见 JSON。PeakWorkingSetBytes 是进程累计峰值，热运行值不会随 GC 回落；不能据此单独推断泄漏。完整数据仍保留托管内存观测值。

先测未优化生产程序集，随后采用每页 64 节点、单项 Dispatcher 批次、树/表虚拟化和图标缓存。复核发现测试宿主会执行 App 排队的启动回调并创建第二个自动打开输入的窗口；最终宿主在构造 App 时观察并仅中止该唯一启动回调，仍加载真实 App 资源和 MainWindow。隔离后重测所有规模。早期受干扰的数据保存在 `before/after/pre-startup-isolation`，不参与下表和结论。

<!-- PERF_TABLE_START -->
| 输入 | 运行 | 总耗时 ms（前 → 后） | 输入 P95 ms（前 → 后） | 峰值 MiB（前 → 后） |
| --- | --- | ---: | ---: | ---: |
| plan-100 | process-cold | 1237.51 → 866.25 | 928.77 → 158.82 | 232.8 → 214.9 |
| plan-100 | same-process-warm | 1167.40 → 779.74 | 1033.56 → 68.70 | 272.2 → 248.4 |
| plan-1000 | process-cold | 8696.97 → 1185.21 | 7372.54 → 125.41 | 632.1 → 248.4 |
| plan-1000 | same-process-warm | 6258.43 → 1122.33 | 5298.81 → 76.49 | 1005.2 → 318.9 |
| plan-5000 | process-cold | 30245.12 → 2542.46 | 26162.71 → 118.45 | 2402.2 → 390.5 |
| plan-5000 | same-process-warm | 38310.26 → 3200.14 | 31741.34 → 70.61 | 4297.9 → 643.1 |
| xel-multiple | process-cold | 217.46 → 219.21 | 44.39 → 33.55 | 177.6 → 180.8 |
| xel-multiple | same-process-warm | 62.95 → 59.70 | 35.29 → 35.89 | 182.1 → 180.8 |
<!-- PERF_TABLE_END -->

阈值来自实施规划的候选目标：P95 <100 ms、取消确认 <500 ms。计划冷运行 P95 尚未全部达标；热运行和该小型 XEL 样本不能证明所有生产负载达标。首轮 JIT、SQL/XML 文本呈现及完整诊断集合仍可能阻塞 UI，需要后续针对实际负载继续评审。未把这些结果写成产品 SLA，也未确定普适输入规模/内存上限。

基线程序集保留在本机临时宿主，生产 DLL 哈希见 [production-binaries.json](before/production-binaries.json)，修改前源码清单见 [before/source-inputs.json](before/source-inputs.json)。Build-BaselineProbe 只重编测量程序并引用保存的生产 DLL；基线快照丢失时会停止，不能用当前代码冒充旧版本。原始计划测量程序和 XEL 测量程序单独保留。

```powershell
./DOCS/verification/IMP-26/Run-Performance.ps1 -Phase before
./DOCS/verification/IMP-26/Run-Performance.ps1 -Phase after
./DOCS/verification/IMP-26/Run-XelPerformance.ps1 -Phase before
./DOCS/verification/IMP-26/Run-XelPerformance.ps1 -Phase after
```

## 真实多事件 XEL

SQL Server 2019 LocalDB 15.0.4382.1 的独立测试数据库中制造两次 1205 死锁，同一 XE 文件捕获 xml_deadlock_report 和该测试数据库的 sql_batch_completed；两个死锁之间保留非死锁记录，因此原始记录编号不连续。数据库和 XE 会话已删除。原始 XEL 只保留于本机 `.tmp.imp26-real`，文件大小/哈希见 [xel-fixture.json](xel-fixture.json)，不提交其中的主机或登录信息。

基线 XEL 使用原版本的 DocumentOpenService 和事件选择/分析/绘图路径，测量程序省略阻塞的原生“部分读取”提示；优化后直接调用 AnalyzeFileAsync 并保留 Partial 状态。这个通知处理差异在两侧 `xel-dialog-policy.json` 明示，不能把省略的用户确认时间算作优化收益。只有两个死锁事件；未声称完成大规模 XEL 负载验证。

重新采集需本机具备脚本指定的 LocalDB 实例：

```powershell
./DOCS/verification/IMP-26/Capture-XelFixture.ps1
```

脚本仅操作随机命名的测试数据库/会话，临时 XE 文件先写入该 LocalDB 用户可写目录，停止后复制到 `.tmp.imp26-real`，并清理原文件及数据库/会话。

## WPF 与取消

```powershell
./DOCS/verification/IMP-26/Run-WpfProbe.ps1 -Configuration Debug
./DOCS/verification/IMP-26/Run-WpfProbe.ps1 -Configuration Release
./DOCS/verification/IMP-26/Write-Validation.ps1
./DOCS/verification/IMP-26/Write-Validation.ps1 -VerifySources
```

宿主复用 IMP-25 的 54 个尺寸/主题/缩放组合，增加实际 1000 算子分页、全部指标行、跨页证据和键盘 End、错误后重开、A 迟到无法覆盖 B、真实多事件 XEL 初次打开只创建一个请求和事件切换，并验证状态文字在两种主题下对比度均不低于 4.5:1。大图截图收起可折叠的诊断面板以检查节点可见性。两配置绑定错误均为零，截图在各自 `wpf-*` 目录。每次运行归档旧输出到 `.tmp.imp26-wpf-*`，确保历史文件不能满足新断言。

取消在 PreparingView 通知和已提交八个图节点时分别触发，验证不会转成 Ready，分批提交取消后图和 AllNodes 清空；具体确认耗时见 [Debug](wpf-Debug/cancellation.json) / [Release](wpf-Release/cancellation.json)。取消确认与后台工作退出是两个观测，未把前者当作第三方解析器的强制中止时间。

窗口为离屏渲染，命令/按键通过实际 WPF 路由事件、AutomationPeer 调用；文件对话框为注入对象。缩放截图不改变 OS 显示器 DPI。实体键盘、原生文件对话框、多显示器 DPI 和屏幕阅读器仍需人工验收。
