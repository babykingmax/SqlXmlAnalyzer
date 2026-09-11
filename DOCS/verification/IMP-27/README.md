# IMP-27 验证记录

本目录保留初次实施时的历史验证。审查修复后的 2691 项测试和源码哈希见[IMP-27 加固验证](../IMP-27-hardening/README.md)；本目录的旧源码清单不代表修复后的工作区。

日期：2026-09-10。实现与隐私边界见 [IMP-27](../../IMP-27可审核的本地诊断数据包.md)，机器可读结果见 [summary.json](summary.json)。验证基于现有工作区，包括先前未提交的 IMP-23～26 修改。

## 构建和测试

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug -m:1
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build --logger "trx;LogFileName=full.trx" --results-directory .tmp.imp27-results/Debug
dotnet build SqlXmlAnalyzer.sln -c Release -m:1
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release --no-build --logger "trx;LogFileName=full.trx" --results-directory .tmp.imp27-results/Release
```

两配置构建各零警告、零错误；全量各 **2684** 通过，零失败/跳过。相对 IMP-26 加固基线 2645 项新增 **39** 项。测试名称和计数从最终完整 TRX 提取，哈希见摘要；未生成覆盖率报告。原始日志及 TRX 留在本机 `.tmp.imp27-*`，不提交进 Git。

新测试验证：默认包所有文件敏感标记排除、有效分析配置与活动配置区分、采集/运行时证据缺失、Skipped/Failed 规则摘要、附件明确选择、UTF-8/大小/DUMP 结构、审核字节冻结、选项改变失效、取消、已有文件/并发创建保护、损坏或被替换 ZIP 拒绝、路径穿越/重复项/清单损坏、临时文件清理及异常处理。

`DiagnosticPackageDiagnosticTests` 调用实际 `WindowsMiniDumpWriter`，使用 `MinidumpValidator` 检查转储，并验证显式附带真实 DUMP。转储故障、诊断组件故障、同异常去重和 Debug/Release 级别门禁均有断言。测试转储及其异常侧车在临时目录生成并清理，不进入提交物。默认包始终不自动包含新转储。

## WPF 验证

```powershell
./DOCS/verification/IMP-27/Run-WpfProbe.ps1 -Configuration Debug
./DOCS/verification/IMP-27/Run-WpfProbe.ps1 -Configuration Release
./DOCS/verification/IMP-27/Write-Validation.ps1
./DOCS/verification/IMP-27/Write-Validation.ps1 -VerifySources
```

独立 STA 进程加载真实 App 资源和 MainWindow，以命令路由打开诊断包窗口，通过按钮 AutomationPeer 执行预览和保存。验证无结果元数据、计划所选范围固定、死锁事件、所有文件的敏感标记、实际 ZIP 与清单/预览哈希、改变原始 XML 选项后禁用保存、损坏快照降级、正常尺寸有界预览和紧凑尺寸滚动。两个配置的 `verification.json` 均须明确成功且无绑定错误。

- [Debug 验证](wpf-Debug/verification.json)、[浅色界面](wpf-Debug/review-light.png)、[深色界面](wpf-Debug/review-dark.png)、[紧凑界面](wpf-Debug/review-compact.png)。
- [Release 验证](wpf-Release/verification.json)、[浅色界面](wpf-Release/review-light.png)、[深色界面](wpf-Release/review-dark.png)、[紧凑界面](wpf-Release/review-compact.png)。

默认包用脚本内合成 SQL 夹具生成，没有生产敏感输入；生成的 ZIP 保留于本地输出目录，不纳入 Git。包内 JSON 的源版本/采集时间可能缺失时保留缺失项。截图显示的是审核时的配置快照，不包含日志、DUMP 或原始 SQL。

截图检查发现无限尺寸测量导致长 JSON 撑大清单，以及深色主题标题/表头/选中行对比度不足；修复后为预览设置有界内容区域，并显式绑定语义颜色。追加正常窗口无需外层滚动和深浅主题文字对比度不低于 4.5:1 的断言后复跑。现目录只保留最后一轮成功输出；早期输出移动至本机 `.tmp.imp27-wpf-*`。

这是离屏软件渲染验证，文件对话框为注入对象；没有声称验证实体键盘、真实显示器 DPI、原生文件对话框、屏幕阅读器或 DUMP 的调试器级完整性。既有 IMP-26 性能差距不在本步关闭。
