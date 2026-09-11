# IMP-25 验证记录

本目录对应未提交工作区；包含前置 IMP-23 / IMP-24 实现与本次 IMP-25。完整结果、日志哈希及源码输入清单见 [summary.json](summary.json)，下列脚本可重新执行。

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug -m:1 -p:IntermediateOutputPath=obj/imp25/Debug/
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build --logger "trx;LogFileName=full-Debug.trx" --results-directory DOCS/verification/IMP-25
.\DOCS\verification\IMP-25\Run-WpfProbe.ps1 -Configuration Debug
# 将 Debug 替换为 Release，重复构建、测试和 WPF 验证。
.\DOCS\verification\IMP-25\Write-Validation.ps1
```

新增回归覆盖 DIP 边界、空列表/过期选择/循环导航、CanExecute 阻断、减少动画与手动单步、语义画刷对比度和比较树在 Light → Dark → Light 下更新。诊断测试实际调用 Windows MiniDump 写入并校验文件，覆盖 DUMP 失败和去重；分别在 Debug / Release 验证日志门禁。

每个配置的 `wpf-*/verification.json` 记录 54 个组合：1280×720、1366×768、1920×1080 × 125% / 150% / 200% × Light / Dark × 计划 / 死锁 / 比较。窗口实际 DIP、滚动视口和内容范围、真实宿主 DPI、SQL 控件对比度、折叠侧栏宽度及图片内容检查均有记录。另验证紧凑窗口中比较 B 面板和报告保存按钮随键盘焦点滚动进入视野。

操作链覆盖打开、诊断选择、证据及焦点返回、图节点方向键、F6、历史快照 A/B 设置及实际 HTML 导出。主题切换经过生产导航按钮事件；生成图片前验证像素确有内容，空白图会使探针失败。

| 代表截图 | 用途 |
| --- | --- |
| [深色 1280×720 / 200% 计划](wpf-Debug/dark-1280x720-200-Plan.png) | 紧凑布局、SQL 对比度与滚动入口 |
| [深色 1920×1080 / 125% 比较](wpf-Debug/dark-1920x1080-125-Compare.png) | 动态比较树颜色 |
| [紧凑比较 B 面板焦点](wpf-Debug/compact-comparison-B-focus.png) | 水平滚动后的可达性 |
| [紧凑报告保存焦点](wpf-Debug/compact-report-save-focus.png) | 对话框底部操作可达性 |

边界：这是离屏生产 WPF 宿主，使用路由事件/命令及 AutomationPeer，文件选择由注入服务完成；截图以目标 DIP 和像素缩放模拟矩阵。未操作真实显示器 DPI、实体键盘或屏幕阅读器。真实桌面无鼠标链和多显示器验收保留为发布前人工项，不能把此记录当作已完成的人工验收。

构建日志、TRX 和 DUMP 按仓库约定留在本地；摘要记录其结果和哈希，DUMP 不纳入版本库。
