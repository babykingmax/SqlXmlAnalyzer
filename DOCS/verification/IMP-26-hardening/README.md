# IMP-26 审查修复验证

范围见 [修复说明](../../IMP-26审查修复与加固说明.md)。此目录记录修复后的新结果；原 IMP-26 目录保留实施时的数据，不将旧测量标记为修复后的结果。

## 自动化回归

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug -m:1
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build
dotnet build SqlXmlAnalyzer.sln -c Release -m:1
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release --no-build
./DOCS/verification/IMP-26-hardening/Run-WpfProbe.ps1 -Configuration Debug
./DOCS/verification/IMP-26-hardening/Run-WpfProbe.ps1 -Configuration Release
```

新增 16 项单元测试。首次运行图控件回归时，原实现出现 5 项预期失败，分别涉及页面替换、两个规模的显示选项、连线布局及取消后翻页；修复后保留这些回归。另补充工作区状态收尾、初次加载作用域、旧作用域释放、失败清理、同步加载兼容及视口定位验证。

最终测试计数、构建结果和证据哈希见 `summary.json`；源码清单见 `source-inputs.json`。完整 TRX 保留在本地 `.tmp.imp26-hardening-results`，摘要记录文件哈希。未运行覆盖率统计。

最终结果：Debug、Release 全量各 **2645 项通过，0 失败、0 跳过**；完整解决方案构建各 **0 警告、0 错误**。两个配置的 WPF 探针各 **365 项断言通过、54 组矩阵组合、0 绑定错误**。635 项构建输入的哈希已复核；探针加载的五个生产程序集与对应配置的测试构建输出一致。

## WPF 主窗口验证

脚本复用原 IMP-26 WPF 探针，再编入本目录的 ReviewRegressionProbe，保留原有主题/窗口尺寸/位图缩放矩阵及真实双事件 XEL 回归。它通过可校验的标记插入附加调用；基础探针契约变化时停止构建，避免静默漏跑。

新增验证在主窗口的节点分批提交过程中切换语句、翻页，在后台准备开始后改变选项，并检查最终请求号、忙碌状态、节点身份、SQL 对比及连线布局。额外截图展示纵向大图和更新后的显示选项。

使用真实 WPF 控件和 App 资源，离屏窗口、软件渲染。单元测试中的最小样式不能代替这些模板/绑定检查。未验证实体显示器 DPI、物理键盘或屏幕阅读器；没有重新执行完整性能基准。

截图：[Debug 翻页后视口](wpf-Debug/large-graph-vertical-after-review.png)、[Release 当前显示选项](wpf-Release/large-graph-current-options-after-review.png)。查看原始断言：[Debug](wpf-Debug/verification.json)、[Release](wpf-Release/verification.json)。
