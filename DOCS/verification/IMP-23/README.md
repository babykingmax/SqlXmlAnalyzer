# IMP-23 验证记录

日期：2026-09-10。最终计数见 [summary.json](summary.json)。源码哈希记录为工作区 SHA-256，不表示已经提交或发布；Git 换行转换可能改变工作区字节。

## 自动化测试

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build
dotnet build SqlXmlAnalyzer.sln -c Release
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release --no-build
```

两配置全量各 2459 项通过，0 失败、0 跳过；完整构建均 0 警告/错误。摘要保留新增 45 项用例及结果、全量 TRX 哈希，旁边的 build/tests 日志保留命令结果。完整 TRX 在工作区 `.tmp.imp23-results`，不把大型逐测试运行记录加入源码。未生成覆盖率报告。

未知错误测试调用实际 WindowsMiniDumpWriter，再通过 MinidumpValidator 验证；另覆盖转储失败、诊断提供器失败、同一异常去重，以及 Debug/Release 的 stderr/文件日志门禁。转储和异常侧车仅用于测试，验证后清理，不作为发布文件保存。

## 真实 XEL

本次在 SQL Server 2019 15.0.4382.1 LocalDB 的独立 `IMP23_<随机标识>` 数据库，创建两行的 DeadlockFixture 表。两个连接分别更新第 1/2 行、等待 3 秒、再更新对方行；低优先级连接收到 1205。XE 会话捕获 xml_deadlock_report 和该测试数据库的 sql_batch_completed，保存 event_file；重复采集两次，随后删除专用数据库和 XE 会话。没有使用用户数据库。

原始 XEL 保留在本机 `.tmp.imp23-real`，包含机器/登录等采集信息，未加入版本库。持久证据 [Debug](real-xel-Debug.json) / [Release](real-xel-Release.json) 保存文件大小/哈希、事件位置、带偏移时间、数量和结果。

```powershell
./DOCS/verification/IMP-23/Run-XelProbe.ps1 -InputDirectory .tmp.imp23-real -Configuration Debug
./DOCS/verification/IMP-23/Run-XelProbe.ps1 -InputDirectory .tmp.imp23-real -Configuration Release
```

XelProbe 专门验收上述两文件：2 个死锁、至少 2 个非死锁记录、1 组、独立发生而非重复、完整偏移，以及对象/文本/时间组合筛选和损坏文件拒绝。复用其他采集时须满足这些样例前提；该脚本不是通用 XEL 内容验收器。

## WPF 视图、绑定与截图

```powershell
./DOCS/verification/IMP-23/Run-WpfProbe.ps1 -Configuration Debug
./DOCS/verification/IMP-23/Run-WpfProbe.ps1 -Configuration Release
```

宿主加载实际 App/MainWindow/检索窗口，调用生产控件事件及导航服务；仅多来源检索数据使用随库的两个合成 XDL 和一个副本。截图和结果位于 `wpf-Debug`、`wpf-Release`。验证工具栏自动载入、分组数量、重复保留、偏移显示、筛选后选择清空、回到正确输入，以及无效偏移保留旧结果；绑定错误为 0。

桌面交互工具启动时两次返回 `GetCursorPos failed: 拒绝访问 (0x80070005)`。本次使用离屏 WPF 渲染，不能视为鼠标/键盘端到端验收。没有开展大规模 XEL 性能实测，也没有关闭 IMP-26 或 M6 的全部验收门槛。
