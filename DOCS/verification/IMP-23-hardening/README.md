# IMP-23 审查加固验证

日期：2026-09-10。本目录记录本轮源码与产物的 SHA-256、命令输出和新增用例结果；前轮 [IMP-23](../IMP-23/README.md) 保留历史证据。完整 TRX 位于本地 `.tmp.imp23-hardening-results`，不提交包含全部逐测试记录的大文件。当前结果见 [summary.json](summary.json)。

## 全量构建与测试

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug -m:1 -p:IntermediateOutputPath=obj/imp23-hardening/Debug/
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build --logger 'trx;LogFileName=full-Debug.trx' --results-directory .tmp.imp23-hardening-results
dotnet build SqlXmlAnalyzer.sln -c Release -m:1 -p:IntermediateOutputPath=obj/imp23-hardening/Release/
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release --no-build --logger 'trx;LogFileName=full-Release.trx' --results-directory .tmp.imp23-hardening-results
```

两配置均 2480 通过、0 失败、0 跳过，构建均零警告/错误；IMP-23 专项 66 项，本轮新增 21 项。未知异常测试调用真实 WindowsMiniDumpWriter 并校验 minidump，包含写入失败、诊断组件失败、异常去重与 stderr/文件日志级别检查；测试后清理 DUMP/侧车，不提交转储。未生成覆盖率报告。

标准 `obj/Debug/net8.0-windows/SqlXmlAnalyzer.g.resources` 被 Google Drive 占用，已通过 Windows Restart Manager 查询定位。独立中间目录避开锁定文件，输出仍为标准 bin/Debug、bin/Release。只串行执行这些构建和探针，避免共享项目输出冲突。

## 探针

PowerShell 7 下在仓库根目录执行：

```powershell
foreach ($configuration in 'Debug', 'Release') {
    ./DOCS/verification/IMP-23-hardening/Run-MemoryProbe.ps1 -Configuration $configuration
    ./DOCS/verification/IMP-23-hardening/Run-XelProbe.ps1 -InputDirectory .tmp.imp23-real -Configuration $configuration
    ./DOCS/verification/IMP-23-hardening/Run-WpfProbe.ps1 -Configuration $configuration
}
```

- MemoryProbe：同一进程在预热后测量三种 7 进程对称图的线程分配，断言每次低于 8 MiB；另检查约 8 Mi 字符的大属性输入在 256 MiB 托管堆上限下仍可检索/追溯且独立分组。上限通过子进程环境变量设置，不改全局环境；这不是整体应用内存上限或大规模性能基准。
- XelProbe：前轮采集的两个 SQL Server 2019 合成死锁 XEL 在 `.tmp.imp23-real`，原始字节含机器采集信息，未加入版本库。核对两个源 SHA-256、2 死锁/4 未处理记录、1 组、原始位置和时间；新增按各源 `currentdbname` 命中 1 个事件的断言。缺少这两个本地文件时不能复现这项探针，须使用满足脚本前提的专用测试采集。
- WpfProbe：实际 App/MainWindow/检索窗口的离屏宿主。集合来自随库合成 XDL，增加初始化期间按钮禁用/程序触发保护，并验证成员、筛选、重复副本、原始事件导航和错误状态。截图位于对应配置目录；这不是桌面鼠标/键盘自动化。

探针 C# 以 `.cs.txt` 保存，运行时复制到系统临时工程，避免被 WPF 项目的默认源码 glob 编译。运行日志、JSON 和 PNG 与当前文件一同校验；不将临时工程、真实 XEL、DUMP 或大型逐测试 TRX 加入源码。
