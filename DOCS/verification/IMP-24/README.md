# IMP-24 验证记录

2026-09-10，Windows / .NET 8。当前工作目录验证；包含此前 IMP-23 的未提交实现，不代表已提交或已发布版本。

| 配置 | 解决方案构建 | 全量测试 | 配置相关测试 | 实际 WPF 宿主 |
| --- | --- | --- | --- | --- |
| Debug | 0 警告 / 0 错误 | 2527 通过 / 0 失败 / 0 跳过 | 54 通过 | 通过，0 绑定错误 |
| Release | 0 警告 / 0 错误 | 2527 通过 / 0 失败 / 0 跳过 | 54 通过 | 通过，0 绑定错误 |

相对于此前 2480 项测试新增 47 项，另更新旧的未知规则拒绝测试。单元测试实际调用 Windows minidump writer 并用 `MinidumpValidator` 验证流目录/线程/模块等结构；也验证 DUMP 失败、同异常去重、诊断组件失败和 Debug/Release 日志门禁。测试 DUMP 在临时目录生成并在测试清理时删除，不将进程转储提交到仓库。

## 复现

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug -m:1 -p:IntermediateOutputPath=obj/imp24/Debug/
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build --logger "trx;LogFileName=imp24-full-Debug.trx"
dotnet build SqlXmlAnalyzer.sln -c Release -m:1 -p:IntermediateOutputPath=obj/imp24/Release/
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release --no-build --logger "trx;LogFileName=imp24-full-Release.trx"
./DOCS/verification/IMP-24/Run-WpfProbe.ps1 -Configuration Debug
./DOCS/verification/IMP-24/Run-WpfProbe.ps1 -Configuration Release
./DOCS/verification/IMP-24/Run-CliProbe.ps1
./DOCS/verification/IMP-24/Collect-Evidence.ps1
```

本机默认 Debug 中间资源被 Google Drive 文件系统进程占用，使用独立的 `obj/imp24/<Configuration>/` 中间路径。未停止外部程序；编译输出仍在正常 `bin/<Configuration>/`。上述构建包含正常还原步骤。

## 证据

- 构建日志 `build-Debug.log` / `build-Release.log`、测试日志 `tests-Debug.log` / `tests-Release.log` 及完整 TRX `SqlXmlAnalyzer.Tests/TestResults/imp24-full-<Configuration>.trx` 属于本机原始材料，不随 GitHub 源码提交。摘要保留当时的 SHA-256 和配置相关用例结果。
- [WPF Debug 结果](wpf-Debug/verification.json)、[Release 结果](wpf-Release/verification.json)，以及各目录的 `binding-errors.txt`、运行日志和 PNG 截图。无效输入与文件冲突的错误日志属于刻意注入的场景。
- [配置变更预览](wpf-Debug/configuration-change-preview.png)、[旧结果明确提示重算](wpf-Debug/workspace-needs-reanalysis.png)、[Info 覆盖后的工作区](wpf-Debug/workspace-info-override.png)、[无效配置保留草稿](wpf-Debug/invalid-config-retains-draft.png)、[保存冲突](wpf-Debug/save-conflict.png)。
- [机器可读摘要](summary.json)、[源文件指纹](source-inputs.json)。源清单覆盖 Git 管理及当前未跟踪的 C# / XAML / 项目文件和配置，排除生成目录与文档；不是系统 SDK/外部依赖的完整锁定证明。
- [CLI 兼容验证](cli-verification.json)：两配置均运行真实 CLI。发行版旧配置成功读取并按已有 Critical 门禁返回 1；包含未来规则结构且禁用两个基数来源的配置成功读取并返回 0。退出 1 在此是预期的诊断门禁，不是配置读取失败。

WPF 脚本在系统临时目录构建独立宿主，运行实际 `App` 资源、`MainWindow`、配置入口、窗口、复选框与下拉框绑定、保存和重新分析事件。用合成 ShowPlan 输入核对真实规则结果、图/列表上下文引用、未知字段保留和外部修改保护。截图由 WPF 渲染得到，已查看配置和工作区截图；这不是实际桌面输入自动化，也不替代 IMP-25 的键盘、DPI、主题和屏幕阅读器验收。本步没有新增数据库行为，因此不宣称 SQL Server 性能或线上负载验证。
