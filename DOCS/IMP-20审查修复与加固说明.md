# IMP-20 审查修复与加固

日期：2026-09-09。修复代码审查发现的三项 P2 问题，并加固相邻的清空、过期结果和异常传播路径。

## 修复结果

| 问题 | 修复后的行为 | 回归证据 |
| --- | --- | --- |
| 手动选择死锁进程/资源后仍显示旧证据 | 原文目标与证据选择一起更新；手动选择清除下拉框的选中值，保留可用证据列表。重新选择同一证据可恢复原进程、资源、连线与诊断详情。 | 模型测试、真实 WPF 进程→证据与资源→证据往返 |
| 折叠后定位证据导致不同节点重叠 | 仅展开目标的折叠祖先，先重算图坐标再更新可见性。保留目标本身和其他分支的折叠状态；选择已可见节点不会重算用户拖动后的坐标。 | 七节点夹具，横向/纵向坐标及真实控件选中断言 |
| 重构失败、跳过原因和 DUMP 信息未传到工作区 | `PlanAnalysisOutput.RefactoringNotices` 独立传递重构阶段提示，工作区明确标注其文档范围，界面、复制和便携文本报告保留提示；SQL 仍只包含 SQL。 | 从分析服务经 UI Action 到工作区/报告的故障注入测试、真实提示框绑定 |

额外加固：清除证据时清除图高亮及进程/资源列表选中；源 XML 修改后重新选择同一证据也必须验证有效性；清空事件后拒绝迟到的分析结果；计划工作区初始化失败时向调用方返回失败，不再继续标记成功。新文档及清空操作释放上份文档的重构提示。

`RefactoringNotices` 是兼容性新增字段；既有 `WarningsText` 仍包含完整诊断和重构提示，保持旧 API/测试调用语义。没有修改诊断规则 ID、严重度默认值、SQL 改写资格或 SQL Server 诊断推断。

## 技术依据与检索

按 Microsoft 官方资料优先，并核对本项目实际依赖的开源版本：

- Microsoft 的 [WPF 属性变更通知说明](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/data/how-to-implement-property-change-notification)要求在绑定源属性改变时通知界面。此次手动选择同步更新 `SourceTarget`、`SelectedEvidence` 并发送通知，避免下拉框和原文指向不同对象。
- Microsoft 的 [UIElement.UpdateLayout 文档](https://learn.microsoft.com/en-us/dotnet/api/system.windows.uielement.updatelayout?view=windowsdesktop-8.0)说明该方法执行 WPF 测量/排列，并建议在必要的属性变更完成后调用。它不会执行应用自己的图坐标算法。
- 核对 GitHub 上 [NodifyCanvas.ArrangeOverride](https://github.com/miroiu/nodify/blob/e9a6023c42cabc7e70bb6bde6df907310e8ad2c9/Nodify/NodifyCanvas.cs#L32)及 [NodifyEditor.BringIntoView](https://github.com/miroiu/nodify/blob/e9a6023c42cabc7e70bb6bde6df907310e8ad2c9/Nodify/NodifyEditor.cs#L801)：前者使用节点已有 `Location` 排列控件，后者调整视口。该提交是本项目 Nodify 6.0.0 NuGet 元数据记录的源码版本，因此保留本项目的图布局服务，在展开后显式重新计算坐标。

GitHub 插件本次未暴露可调用的检索接口，网页工具连接失败后，通过公开 Microsoft 页面和 GitHub API/原始源码完成核对。本次问题属于 WPF 状态与呈现层，官方资料和依赖源码已能支持修复，不借用 SQL Server CSS/社区案例作不相关的诊断依据。

## 异常与日志

继续使用 `ExceptionPolicy`、`UnexpectedErrorReporter` 和经校验的 Windows minidump。预期输入、存储、权限和取消异常不要求 DUMP；未知异常记录致命日志并生成 DUMP，失败时明确保留失败原因及原异常信息。重构阶段产生的 DUMP 路径或诊断组件失败信息现在能到达可见提示和报告。

Debug 输出 DEBUG/WARN/ERROR/CRITICAL，Release 仅 ERROR/CRITICAL。新增正常日志仅包含选择阶段和展开计数。原有真实 DUMP 成功/失败/去重及两配置日志过滤测试随全量套件重新执行。

## 验证

新增 `WorkspaceSelectionHardeningTests` 15 项；IMP-20 累计新增 44 项。另加强既有安全重构测试对独立提示字段的断言。

Debug 和 Release 完整构建均为 **0 警告、0 错误**，全量测试均为 **2273 通过、0 失败、0 跳过**。两配置真实 MainWindow 验证均通过，绑定错误均为 0。测试日志、TRX、源码和截图摘要见[加固验证摘要](./verification/IMP-20/hardening-summary.json)。初次实施的 2258 项结果及原截图保留为历史记录，不能替代本次结果。

| 本次状态 | Debug | Release |
| --- | --- | --- |
| 重构失败与 DUMP 提示 | [提示框](./verification/IMP-20/wpf-Debug-b24f117d54cd45729e633683c41adcec/refactoring-notices.png) | [提示框](./verification/IMP-20/wpf-Release-3290ec704c5d4241b381d3ffcdac67a9/refactoring-notices.png) |
| 定位证据后的节点布局 | [图布局](./verification/IMP-20/wpf-Debug-b24f117d54cd45729e633683c41adcec/plan-evidence-layout.png) | [图布局](./verification/IMP-20/wpf-Release-3290ec704c5d4241b381d3ffcdac67a9/plan-evidence-layout.png) |
| 手动选择后的证据空选中 | [死锁选择](./verification/IMP-20/wpf-Debug-b24f117d54cd45729e633683c41adcec/deadlock-manual-selection.png) | [死锁选择](./verification/IMP-20/wpf-Release-3290ec704c5d4241b381d3ffcdac67a9/deadlock-manual-selection.png) |

WPF 提示截图中的 `synthetic-verification.dmp` 是明确标注的故障注入展示文本，不代表该截图生成了 DUMP；真实 DUMP 的创建与校验由诊断测试负责。全部输入为合成回归夹具，不声称已验证真实 SQL Server 并发时序。HTML 仍为全文档报告，跨启动选择持久化仍在后续范围。

## 复核方式

1. 打开 `deadlock_multiple_events.xdl` 的第二事件，选择 Lock Chain Details；手动切换进程或资源，确认证据下拉框清空，再选择原证据，确认原文、图高亮和诊断详情恢复。
2. 打开 `imp20_navigation_branches.sqlplan`，折叠 Node 1，再从指标表选择 Node 3；确认展开后 Node 3 与 Node 5 不重叠。分别复核横向、纵向布局；折叠 Node 4 后选择已可见节点，Node 4 应保持折叠。
3. 复核工作区“SQL 改写阶段（文档分析结果）”和复制文本都包含重构提示；打开新文档及清空后不遗留旧提示。

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug
dotnet build SqlXmlAnalyzer.sln -c Release
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release
& DOCS/verification/IMP-20/Run-WpfVerification.ps1 -Configuration Debug
& DOCS/verification/IMP-20/Run-WpfVerification.ps1 -Configuration Release
```

`Write-VerificationSummary.ps1 -Phase Hardening` 使用 `.tmp.imp20-hardening-*` 构建日志和 TRX，并接受上述脚本返回的两个 WPF 目录；它会校验计数和文件摘要后生成加固记录。
