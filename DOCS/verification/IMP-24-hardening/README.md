# IMP-24 审查加固验证

本目录对应当前未提交工作区，包含先前 IMP-23 和 IMP-24 实现；不代表已发布提交。新增 22 项回归，Debug/Release 全量各 2549 通过，构建各零警告/错误。

## 复现

在仓库根目录运行（将 Debug 换为 Release 可验证另一模式）：

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug -m:1 -p:IntermediateOutputPath=obj/imp24/Debug/
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build --no-restore --logger 'trx;LogFileName=imp24-hardening-full-Debug.trx'
./DOCS/verification/IMP-24-hardening/Run-WpfProbe.ps1 -Configuration Debug
./DOCS/verification/IMP-24-hardening/Run-MemoryProbe.ps1 -Configuration Release
```

使用独立 WPF 中间目录以避开此机器上原默认 `obj/Debug` 资源文件的外部占用。探针项目建立在系统临时目录并沿用仓库 `Directory.Packages.props`；不改变产品依赖版本。WPF 辅助进程隐藏运行，实际窗口置于屏幕之外。

## 文件

- `summary.json` / `source-inputs.json`：测试计数、源文件 SHA-256、TRX 与附件摘要。
- `build-*.log` / `tests-*.log`：本地完整构建与测试输出；原始 TRX 在 `SqlXmlAnalyzer.Tests/TestResults`。
- `WpfProbe.cs.txt` / `Run-WpfProbe.ps1`：实际主窗口交错操作、配置应用与重算验证；`wpf-*/verification.json` 和截图保留结果。
- `MemoryProbe.cs.txt` / `Run-MemoryProbe.ps1` / `memory-Release.json`：在 256 MiB 托管堆限制下验证小型放大样本和 900 KB 输入，记录累计分配和单次耗时。

未知异常 DUMP、损坏输入不生成 DUMP、Debug/Release 日志门禁均通过全量测试中的诊断测试验证。测试创建的临时 DUMP 会在测试结束后清理，不将内存转储纳入仓库。证据不代替人工桌面兼容性验收；源码改动后应重新生成本目录的验证结果。初版 2527 项验证保留在相邻 `IMP-24` 目录，属于历史快照。
