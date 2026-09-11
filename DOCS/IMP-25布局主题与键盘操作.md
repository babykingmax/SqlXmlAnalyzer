# IMP-25：布局、主题与键盘操作

2026-09-10。已实现主工作区布局、语义主题和键盘命令；自动化结果见[验证记录](verification/IMP-25/README.md)。真实显示器 DPI、实体键盘和屏幕阅读器的人工验收仍单独保留，不能由离屏窗口截图代替。

审查后的三项缺陷修复见[修复与加固说明](IMP-25审查修复与加固说明.md)：恢复/拖动侧栏后的滚动范围同步、F6 跳过不可用目标、节点详情自动展开和 Escape 返回。后续验证以 [IMP-25-hardening](verification/IMP-25-hardening/README.md) 为准。

## 布局与主题

主标签和计划子标签移除负边距，标签标题支持水平滚动。三个工作区按可用 DIP 宽度排版，计划与死锁在宽度不足 1050 DIP 或高度不足 620 DIP 时收起侧栏。计划、死锁内容最小为 520×620 DIP；比较内容最小为 960×620 DIP。更小窗口保留滚动入口，打开侧栏后相应扩展滚动范围。导航栏也可垂直滚动，Tab / F6 聚焦控件会将它带入可见区域。

计划侧栏采用支持横向展开的系统 Expander 模板，避免主题模板折叠后仍占用 280 DIP；忽略内部属性分组 Expander 的冒泡事件，防止误改外层宽度。配置、报告、索引审核、改写审核、XEL 搜索和快捷键帮助窗口根据所属主窗口及工作区限制尺寸，必要时提供滚动内容。

`WorkspaceThemeService` 提供背景、表面、正文、次要文字、边框、强调色、选中、焦点、禁用和告警等 20 种语义画刷。主题切换同步更新 Material Design 与语义资源；已生成的比较树、SQL 文本、导航图标和状态栏即时更新。诊断级别、比较状态、数值正负和节点身份保留文字或图标，颜色不作为唯一依据。普通文字语义组合按 4.5:1 检查；实际 SQL 控件另外在 WPF 窗口中读取前景和背景验证。

## 键盘操作

| 按键 | 操作 |
| --- | --- |
| Ctrl+O / Ctrl+Shift+O | 打开执行计划 / 死锁或 XEL |
| Alt+1 / Alt+2 / Alt+3 | 死锁 / 计划 / 比较工作区 |
| F6 / Shift+F6 | 下一个 / 上一个工作区焦点区域 |
| F8 / Shift+F8 | 计划当前范围的下一个 / 上一个诊断列表条目 |
| Ctrl+Shift+G | 聚焦执行计划图节点 |
| 方向键、Home、End | 切换图节点，不接管文本编辑器的方向键 |
| Enter | 打开选中节点详情；历史列表中打开选中快照 |
| Ctrl+Shift+E / Escape | 打开原始 SQL/XML 证据 / 返回原焦点 |
| Ctrl+R | 打开当前计划或死锁的 HTML 报告预览 |
| Ctrl+Shift+C / Ctrl+L | 复制结果 / 清空结果 |
| Ctrl+, / Ctrl+F1 / F1 | 规则配置 / 快捷键与显示选项 / 关于 |

导航按钮和快捷键共用 `RoutedUICommand`、`CommandBinding` 和 `CanExecute`，无可用分析结果时禁止报告、复制等操作。命令注册在 ViewModel 和相关服务初始化完成后执行。

无鼠标操作路径：打开计划 → F8 选中诊断 → Ctrl+Shift+E 查看证据 → Escape 返回 → Alt+3 → Tab 到“捕获当前计划” → 在历史列表用方向键选取并通过“设为 A / B”比较 → Alt+2 → Ctrl+R → Tab 到“保存为新文件”。比较页面本身不生成计划/死锁报告，应切回目标来源导出。

`AccessiblePlanNode` 的 AutomationPeer 暴露语句、NodeId、算子名称、诊断数和 Invoke 操作。图节点选中时同步更新现有属性及诊断上下文。Escape 优先留给打开的 ComboBox，关闭对话框后恢复原控件焦点。

“减少动画”位于 Ctrl+F1 帮助窗口，初值遵循 Windows 客户区动画设置；开启时停止自动依赖推演并禁止重新启动，保留单步、重置和手动选择。关闭该选项不会自动开始推演。该选项当前仅在应用会话中保留。

## 异常与日志

交互命令和焦点导航通过 `WorkspaceInteractionService` 进入现有 `ExceptionPolicy`；取消及已知输入错误按原策略处理。未知故障交给 `UnexpectedErrorReporter`，生成 Windows MiniDump，同一异常对象去重。DUMP 写入失败时明确报告失败，不返回虚假的成功路径。报告保存对话框也纳入异常边界，可注入已有文件对话框服务进行操作链验证。

沿用构建级日志门禁：Debug 允许 Debug / Warning / Error / Critical；Release 仅 Error / Critical，强制 verbose 也不能绕过。新增日志只记录命令、主题及操作状态，不加入 SQL 内容或对象标识。诊断回归实际写入并验证 DUMP，同时覆盖写入失败及各配置日志级别。

## 依据与验证边界

辅助技术实现参考 [Microsoft：自定义 WPF 控件的 UI Automation](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/ui-automation-of-a-wpf-custom-control)。无显示设备的渲染探针开关来自 [Microsoft dotnet/wpf 源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/MS/internal/CoreAppContextSwitches.cs)，只在验证进程启用，不修改应用或系统设置。

验证使用实际生产窗口、实际分析器、命令路由、键盘路由事件及 AutomationPeer，文件对话框用固定测试路径代替。矩阵将物理尺寸除以目标缩放得到窗口 DIP，再按目标像素尺寸渲染；同时记录宿主真实 DPI。这验证布局和滚动，不等价于修改真实显示器缩放。发布前仍需在真实显示设备上按矩阵核对实体键盘、原生文件对话框、焦点可见性及屏幕阅读器。
