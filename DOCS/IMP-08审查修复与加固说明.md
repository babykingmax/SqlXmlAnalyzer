# IMP-08 审查修复与主题加固

日期：2026-09-08。修复审查发现的 P2：依赖推演说明使用固定棕色，在深色卡片上的对比度约为 1.96:1，难以读清关键限制条件。

## 修复范围

- `DeadlockPlaybackControl.xaml` 的固定依赖推演说明改用 `DynamicResource MaterialDesignBody`，跟随现有主题文字资源。
- `IndexSandboxWindow.xaml` 的收益 N/A、模型限制、预测暂停说明、输入假设、假设状态及详情共六处文字使用同一主题资源。状态文字不再由固定灰色 `TippingPointStatusColor` 控制；该 ViewModel 属性保留兼容性，判断与数值契约未变。
- 比较说明的固定棕色配固定白底对比度为 6.55:1，保留这组前景/背景搭配，并纳入回归，防止仅改变前景造成白字对白底。

产品改动仅涉及上述两个 XAML 文件的七处前景绑定。没有新增主题切换服务、依赖包或诊断机制。已有 `ExceptionPolicy` / `WindowsMiniDumpWriter` 和日志策略继续负责未知故障：Debug 为 DEBUG/WARN/ERROR/CRITICAL，Release 仅 ERROR/CRITICAL。两个配置的全套测试均重新验证了三个 IMP-08 DUMP/日志回归，包括真实 DUMP 校验及原异常保留。

这次加固针对 IMP-08 说明文字，不代表已完成全应用主题、DPI 或可访问性验收；完整布局主题工作仍属于 IMP-25。

## 检索依据

按 Microsoft 官方资料优先核对，搜索工具连接失败后通过 HTTPS 读取。GitHub 插件没有暴露可调用的连接工具，使用官方仓库的公开 HTTPS 接口定位源码。该问题属于 WPF 主题资源，无需借用 SQL Server 锁或优化器资料推断 UI 行为。

1. [Microsoft：WPF 动态资源](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/dynamicresource-markup-extension)及[资源查找与主题变化](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/systems/xaml-resources-overview)：运行时可能变化的主题资源应保留动态引用，不能用固定本地值覆盖动态表达式。
2. [Microsoft：文本可访问性与对比度](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessible-text-requirements)：本次把 4.5:1 作为解释文字的回归阈值，按实际前景、背景及透明度计算；这是局部验证，不是全应用合规声明。
3. [MaterialDesignInXamlToolkit：ResourceDictionaryExtensions](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit/blob/7ea409750f32de9ad6647921d54e4971e2621e5e/src/MaterialDesignThemes.Wpf/ResourceDictionaryExtensions.cs)及[BundledTheme](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit/blob/7ea409750f32de9ad6647921d54e4971e2621e5e/src/MaterialDesignThemes.Wpf/BundledTheme.cs)：核对资源更新及主题构造。提交号来自本机 MaterialDesignThemes 5.3.2 包的 nuspec，与当前依赖对应。

## 回归证据

新增 `AnalysisDisplayThemeTests` 的 **8 项**测试，加载生产 XAML 中的真实 TextBlock，绑定实际沙盒 ViewModel，用已安装组件的 `BundledTheme` 替换资源字典，依次验证浅色、深色、浅色、深色。计算时考虑文字颜色 Alpha 和 Brush.Opacity；同时检查绑定后文本非空且布局高度非零。

| 验证 | 结果 | 证据 |
| --- | --- | --- |
| 修复前主题测试 | 7 失败、1 通过；包括原始 1.96:1 反例 | [红灯 TRX](implementation/IMP-08/hardening/tests/theme-red.trx) |
| 主题测试与 DUMP/日志定向回归 | 11/11 通过 | [绿灯 TRX](implementation/IMP-08/hardening/tests/theme-green.trx) |
| Debug 完整非增量构建及全套测试 | 0 警告、0 错误；1183/1183 通过，0 跳过 | [构建](implementation/IMP-08/hardening/build-debug.log)、[TRX](implementation/IMP-08/hardening/tests/full-debug.trx) |
| Release 完整非增量构建及全套测试 | 0 警告、0 错误；1183/1183 通过，0 跳过 | [构建](implementation/IMP-08/hardening/build-release.log)、[TRX](implementation/IMP-08/hardening/tests/full-release.trx) |
| 实际 WPF 主题切换 | 27 项观测通过，进程退出码 0 | [观察值](implementation/IMP-08/hardening/ui/observed.json)、[进程记录](implementation/IMP-08/hardening/ui-process.json)、[源码](implementation/IMP-08/hardening/ThemeUiProbe.cs.txt) |

WPF 探针使用真实 App 资源与三个生产控件，在同一组窗口宿主中调用 `PaletteHelper` 切换浅色→深色→浅色。验证七处动态文字确实解析到当前应用主题的画刷，以及八处说明与一个无效假设状态的实际背景对比度。沙盒使用默认 1000×620 内容尺寸，滚动到说明与无效假设状态后保存截图。没有显示用户窗口或执行 SQL Server。

深色状态中，依赖推演说明为 **10.66:1**，沙盒说明及状态为 **8.12:1**；固定白底的比较说明为 **6.55:1**。已检查[深色推演截图](implementation/IMP-08/hardening/ui/dependency-dark.png)、[深色沙盒说明](implementation/IMP-08/hardening/ui/sandbox-notice-dark.png)及[无效输入状态](implementation/IMP-08/hardening/ui/sandbox-invalid-dark.png)。

首轮探针中的推演控件未连接到 Window 树，没有接收应用资源失效通知，虽然对比度通过，却仍显示浅色。检查截图后修正宿主，并增加画刷必须匹配当前应用主题的断言；[首轮观察值](implementation/IMP-08/hardening/ui-attempt1-observed.json)及[首轮进程记录](implementation/IMP-08/hardening/ui-attempt1-process.json)保留用于说明验证局限，最终证据以 `ui/observed.json` 为准。

本轮不重跑 IMP-02 反例矩阵；[初次 IMP-08 实施说明](IMP-08展示纠错与证据边界说明.md)中的 13 项最低条件通过、9 项 NotMet 是此前矩阵结果。R12 的底层模拟顺序依赖仍由 IMP-16 修复。未生成测试覆盖率报告。构建、测试及实际加载程序集的指纹见[本轮汇总](implementation/IMP-08/hardening/verification.json)。

## 复现

```powershell
$env:UseArtifactsOutput = 'true'
$env:ArtifactsPath = 'E:\SqlXmlAnalyzer\bin\imp08-20260908'
dotnet build SqlXmlAnalyzer.sln -c Debug --no-incremental
dotnet test SqlXmlAnalyzer.Tests\SqlXmlAnalyzer.Tests.csproj -c Debug --no-build
dotnet build SqlXmlAnalyzer.sln -c Release --no-incremental
dotnet test SqlXmlAnalyzer.Tests\SqlXmlAnalyzer.Tests.csproj -c Release --no-build
.\DOCS\implementation\IMP-08\hardening\Run-ThemeUiProbe.ps1
```
