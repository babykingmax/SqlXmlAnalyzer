# IMP-25 审查修复验证

本目录验证三项审查缺陷的修复，包含前置 IMP-23 / IMP-24 / IMP-25 的当前未提交工作区。原 IMP-25 验证目录保留为历史记录；本次源码输入、构建和测试哈希见 [summary.json](summary.json)。

Debug / Release 全量各 2597 项通过，零失败/跳过，构建各零警告/错误；每配置 WPF 54 组合，绑定错误为零。

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug -m:1 -p:IntermediateOutputPath=obj/imp25/Debug/ -v minimal > DOCS/verification/IMP-25-hardening/build-Debug.log 2>&1
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build --no-restore -p:IntermediateOutputPath=obj/imp25/Debug/ --logger "trx;LogFileName=full-Debug.trx" --results-directory DOCS/verification/IMP-25-hardening
.\DOCS\verification\IMP-25-hardening\Run-WpfProbe.ps1 -Configuration Debug
# 将 Debug 替换为 Release，重复执行。
.\DOCS\verification\IMP-25-hardening\Write-Validation.ps1
.\DOCS\verification\IMP-25-hardening\Write-Validation.ps1 -VerifySources
```

新增 22 项单元回归：8 项列宽计算、12 项真实 WPF 控件焦点行为、2 项循环索引溢出边界。53 项定向测试同时复验未知错误实际 DUMP、写入失败、异常去重和 Debug/Release 日志门禁。

生产窗口探针在每种构建配置下执行 54 种原布局/主题矩阵，并验证：

- 左右侧栏分别恢复 800 DIP、修改到 950 DIP；两栏同时展开；反复收起后范围缩回；列宽总和不超过可滚动内容边界。
- 无比较结果时仅保留可用历史焦点；已有 A/B、历史取消选中时，F6 跳过禁用按钮，反向导航和首尾循环正确。
- Enter、Automation Invoke、鼠标双击都展开并聚焦属性栏；属性位于窄窗口可见范围；Escape 返回原节点。
- 原证据焦点返回、图方向键、主题切换、历史捕获、报告导出和对话框滚动等操作链继续通过。

| 截图 | 验证内容 |
| --- | --- |
| [左侧栏恢复与修改](wpf-Debug/restored-sidebar-0.png) | 大列宽下最右侧仍可到达 |
| [右侧栏恢复与修改](wpf-Debug/restored-sidebar-4.png) | 右栏大列宽仍在滚动内容范围内 |
| [Enter 打开属性](wpf-Debug/compact-details-Enter.png) | 紧凑布局自动展开和聚焦 |
| [Invoke 打开属性](wpf-Debug/compact-details-AutomationInvoke.png) | 自动化入口共用详情操作链 |
| [双击打开属性](wpf-Debug/compact-details-MouseDoubleClick.png) | 鼠标入口共用详情操作链 |
| [比较反向循环](wpf-Debug/compact-F6-wrap.png) | 跳过禁用按钮并到达比较 B 区域 |

边界：离屏真实 WPF 窗口、路由事件/命令、AutomationPeer、注入文件对话框与 DIP/像素缩放；反向导航直接调用生产方法。未完成实体键盘、原生对话框、多显示器真实 DPI 或屏幕阅读器人工验收。构建日志、TRX、DUMP 留在本地，摘要保存验证结果和哈希；DUMP 不提交版本库。
