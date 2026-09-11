# IMP-25 审查修复与加固

2026-09-10。修复审查中发现的三项交互缺陷。原实施验证保留在 `verification/IMP-25`，本次验证与源文件哈希独立记录在 [IMP-25-hardening](verification/IMP-25-hardening/README.md)。

## 修复内容

| 问题与触发条件 | 修复后的行为 | 回归验证 |
| --- | --- | --- |
| 将侧栏拖到 800 DIP，缩小窗口后重新展开；固定 280 DIP 的最小宽度估算导致列溢出滚动范围 | 最小宽度计入当前像素宽度、Auto 内容需求、列宽限制、图区域和分隔条；展开和侧栏尺寸变化均更新范围 | 左右侧栏分别恢复 800 DIP，再改为 950 DIP；同时展开两栏；三轮展开/收起；断言所有列与最右边缘均可滚动到达 |
| 比较页已有 A/B，但取消历史选中后“设为 A/B”被禁用，F6 一直尝试同一按钮 | 有界循环跳过禁用、不可见、不可聚焦和拒绝焦点的目标；只有实际获取焦点才结束 | 正向、反向、首尾循环、空集合、全部不可用、焦点事件拒绝；空比较页只有历史区可用时安全保留该焦点 |
| 紧凑布局下 Enter / Automation Invoke 只更新数据，属性侧栏仍关闭 | 鼠标双击、Enter、Invoke 共用详情入口：填充属性、展开右栏、刷新布局并滚动聚焦；属性区 Escape 返回同一图节点 | 三种入口分别验证展开、属性数据、焦点、水平可见范围以及 Escape 返回身份 |

`WorkspaceAccessibility.Focus` 先刷新布局，再判断可见状态，避免刚展开的 Expander 子控件被误判为隐藏。返回值反映真实键盘焦点，导航不会因焦点请求被控件拒绝而停住。循环索引计算同时去除大计数下的整数相加溢出。

像素列按配置宽度计入范围，Auto 列按内容需求计算，Star 列只计入最小宽度；不把 Star 列上次分配的 `ActualWidth` 当作下次最小宽度，避免反复布局导致滚动范围无法缩回。收起侧栏后恢复紧凑范围。

## 异常与日志

新增的侧栏范围更新、打开详情和返回节点均接入 `ExceptionPolicy`。未知异常通过 `UnexpectedErrorReporter` 生成 Windows MiniDump，同一异常对象去重，写入失败明确显示失败。F6 继续通过主窗口 `WorkspaceInteractionService` 的统一异常边界。

日志只添加布局 DIP 数值和操作状态，不记录 SQL、对象名或节点标识。沿用并复验构建门禁：Debug 输出 Debug / Warning / Error / Critical；Release 仅 Error / Critical，verbose 不能绕过。诊断测试实际写入并校验 DUMP，也验证写入失败、去重及两种配置的日志输出。

## 检索依据

按照 Microsoft 官方资料优先的顺序核对。本次缺陷位于 WPF 布局和焦点层，采用下列官方文档及 Microsoft 公开源码；没有将无关的 SQL Server CSS 案例作为修复依据。

- [Microsoft：WPF Focus Overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/focus-overview)：键盘焦点要求控件可聚焦、可见；初始焦点应在控件加载和布局之后设置。
- [Microsoft：WPF Layout](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/layout)：Measure 与 Arrange 分别计算内容需求和分配布局空间；本次据此核对列实际边界与外层滚动内容边界。
- [Microsoft：ScrollViewer.ExtentWidth](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.scrollviewer.extentwidth?view=windowsdesktop-8.0)：水平滚动范围的定义。
- [GitHub：dotnet/wpf v8.0.0 KeyboardNavigation.cs](https://github.com/dotnet/wpf/blob/v8.0.0/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Input/KeyboardNavigation.cs)：官方焦点目标检查使用 Focusable、IsEnabled、IsVisible；Tab 停靠还检查 IsTabStop。本次 F6 为显式区域导航，按真实焦点结果继续尝试下一目标。

## 验证与边界

新增 22 项单元回归；Debug 定向测试 53 项通过。Debug / Release 全量各 2597 项通过，零失败/跳过，构建均零警告/错误。实际窗口探针与哈希见 [summary.json](verification/IMP-25-hardening/summary.json)。WPF 探针复跑原有每配置 54 种尺寸/缩放/主题/工作区组合，并追加上述三项缺陷的操作链断言和截图。

实际窗口探针使用路由键盘事件、鼠标双击事件及 AutomationPeer，反向 F6 调用同一个区域导航方法，文件对话框通过测试服务注入。它验证离屏生产 WPF 的布局与交互，不模拟操作系统按键状态，不改变真实显示器 DPI。实体键盘、原生对话框、多显示器 DPI 切换及屏幕阅读器人工验收仍待执行。
