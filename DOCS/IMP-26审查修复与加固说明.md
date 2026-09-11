# IMP-26 审查修复与加固

日期：2026-09-10。对应 IMP-26 代码审查的四项发现。构建、单元测试及实际 WPF 回归记录见 [验证目录](verification/IMP-26-hardening/README.md)。

## 修复行为

| 场景 | 修复后的行为 |
| --- | --- |
| 初次加载尚在准备视图时切换语句 | 使用显式的文档渲染作用域；等待最新选择完成后，原加载流程继续更新 SQL 对比、统计和最终状态。 |
| 已加载文档中连续切换语句 | 后续渲染继承同一选择操作的完成责任及 Partial 状态；旧渲染不能结束新渲染。 |
| 首屏分批提交时翻页或重新布局 | 只替换当前页面任务；初始加载等待最新页面。用户取消或替换文档仍会停止整个渲染。 |
| 超过 64 个算子时切换布局 | 同步更新全部连线的布局方向，后续页面保持正确的连接端点和箭头。 |
| 后台准备期间修改显示选项 | 提交模型时应用当前的视图、颜色、连线指标及布局选项；小图也重新计算当前布局。 |

## 进一步加固

- 证据定位在提交期间再次变化时，等待并呈现最新定位的页面。
- 显示失败后，清空选择产生的后续任务不会被当成成功；Failed/Cancelled 状态不能继续提交 SQL 对比并报告完成。
- 新页面继承所属文档的取消令牌；已取消的文档不能通过翻页重新提交节点。
- 同步兼容加载替换异步页面后，清除旧 PendingPage 任务，避免后续键盘聚焦等待已取消的旧任务。
- 翻页或重新布局后定位当前页节点，避免保留旧视口位置导致画布看起来没有节点；证据定位仍优先展示目标节点。
- 页面任务的真实异常继续向加载流程传播；未知错误沿用 ExceptionPolicy / UnexpectedErrorReporter 生成 DUMP。正常替换与用户取消不生成 DUMP。

新增 16 项单元回归，覆盖替换、取消、迟到结果、失败清理、Ready/Partial 收尾、大小图选项同步、视口定位和同步加载兼容。控件状态测试使用最小测试样式；完整模板、主题和主窗口流程由独立进程的 WPF 验证补充。未知异常的实际 Windows MiniDump、写入失败、去重以及 Debug/Release 日志门禁继续由全量测试验证。

本次没有改变日志级别：Debug 为 Debug/Warning/Error/Critical；Release 仅 Error/Critical。没有新增 SQL 正文或业务标识日志。

## 检索与依据

优先使用 Microsoft 官方资料。当前缺陷属于 WPF 异步生命周期问题，官方资料已覆盖所需机制，因此没有以 SQL Server CSS 案例替代 UI 线程模型依据。GitHub 插件技能已读取，但本次会话没有可调用的连接器工具；网页搜索工具连接失败后，通过 HTTPS 直接读取官方文档和官方仓库源码。

- [Microsoft：WPF 线程模型](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/threading-model)：UI 操作留在 Dispatcher，异步等待避免阻塞输入队列。
- [Microsoft：监听多个取消请求](https://learn.microsoft.com/en-us/dotnet/standard/threading/how-to-listen-for-multiple-cancellation-requests)：链接取消令牌，并区分取消的来源和作用域。
- [CommunityToolkit：AsyncRelayCommand](https://github.com/CommunityToolkit/dotnet/blob/225436753b179be40ccce22af658835afa46a0ba/src/CommunityToolkit.Mvvm/Input/AsyncRelayCommand.cs)：监视异步任务时，通过任务引用确认它仍是当前任务，再更新完成状态。本项目采用该身份检查思路，没有引入新的运行时依赖。

检索日期：2026-09-10。版本和验证输入见验证目录。本次重新验证交互正确性，没有重测 100/1000/5000 算子的完整性能矩阵；[原性能记录](verification/IMP-26/README.md)及其冷运行 P95 未全部达标的限制继续保留。
