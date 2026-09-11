# IMP-29 审查修复与加固

2026-09-10。本轮修复三项审查发现，并完成增量构建、验证器加载隔离和 XEL 空值检查加固。完整入口最终 **36 个阶段全部通过**，结果见 [summary.json](verification/IMP-29/summary.json) 和执行时的 [build-evidence.json](verification/IMP-29/evidence/build-evidence.json)。原实施记录保留在 [prior-review](verification/IMP-29/prior-review/summary.json)，不能代替修复后验收。

## 修复与版本绑定

原摘要生成器在读取旧 TRX/场景结果之后，对当前源码及 DLL 重新计算指纹；修改源码或重新构建后可能把旧通过结果关联到未测试版本。现在 `Run-BuildTests.ps1` 在 restore/build/test 前保存源码清单和 SHA-256，在每种配置测试前保存实际 WPF、CLI、测试宿主的 17 个生产/测试 DLL 指纹，在执行后核对文件集合、字节长度与哈希。只在双配置成功且输入未变化时，以 CreateNew 写出 `build-evidence.json`。

每个 SQL、索引、CLI、WPF、XEL、性能和诊断阶段都必须提供 `-BuildEvidence`，执行前后再次核对这份构建证明、源码清单、DLL、构建日志和完整 TRX。成功阶段保存 `stage-evidence.json`，绑定构建证明的哈希与该阶段的实际结果字节；摘要生成器先检查这些绑定，再发布复制证据。源码增删、同长度内容变更、CLI DLL 更新、构建证明或结果 JSON 变更都会拒绝。不会将旧结果重新打上当前版本的标签。

`sources.json` 中执行相关源码使用构建时冻结的记录；说明文档单独在发布摘要时记录。`artifacts.json` 同时覆盖当前 `summary.json`。`-VerifySources` 只校验，新增源码也会触发清单不一致。SHA-256 用于一致性校验，不是对抗有权限重写整套记录的攻击者的真实性签名。

## 构建警告与证据交付

移除依赖中文输出的 `0 个警告` 子串匹配。restore 和 build 使用 MSBuild 的 `-warnaserror`，build 同时强制 `-t:Rebuild`；构建记录保存实际参数和原生退出码。强制重建解决了复查发现的另一条复现路径：先普通编译带 CS0168 警告的源码，再执行增量严格构建，编译器可能不再运行，错误地显示零警告。现在重新编译并拒绝该输入。摘要只接受同时启用强制重建、警告门禁且退出为 0 的绑定记录，不从自由文本推断零警告。

`.gitignore` 只对 `DOCS/verification/IMP-29/evidence/debug/` 与 `release/` 增加精确例外。原来被忽略的 34 份摘要可随仓库提交，新增阶段证明和诊断结果同样可交付。普通 bin/obj、日志、TRX、DUMP 仍作为本地产物保存。

## 回归与异常日志

新增 25 项 xUnit 场景，启动真实 PowerShell 7 子进程调用共享生产验收函数。在隔离的合成 Git 仓库中验证未变化通过、源码修改/新增/删除、DLL 修改、TRX 修改、阶段期间改动、结果改动、跨构建混用、缺少绑定、重复记录、路径越界、已有证据保留和 Git 可交付性；中英文环境各执行 0、1、10、20 个真实 MSBuild 警告，另覆盖已经成功增量编译过的警告源码，以及同名但不同实现的 Debug/Release 校验程序集连续加载。

第一轮修复复跑在加入强制重建修复时，实际被版本守卫中止，错误为 `Evidence changed: DOCS/verification/IMP-29/AcceptanceEvidence.ps1`；记录保留在 `.tmp.imp29-hardening-1/stages.json`。它没有被补录或纳入最终验收，最终选择另一个全新目录。

新增双配置诊断探针注入未知异常，要求真实非成功退出、失败 boundary、通过结构校验的原生 minidump 和正确日志门禁同时成立，才将“故障处理验收”记录为通过。Debug 保留 Debug/Warning/Error/Critical，Release 仅 Error/Critical。未知异常继续复用 `AcceptanceProbeBoundary` 与生产 `ExceptionPolicy`，不存在假 DUMP。校验不一致属于预期验收失败，脚本中止并保留日志和失败阶段；不会把它转为成功。

顺序复跑还暴露了验收器的程序集加载冲突：Debug/Release 的 Core 同名，默认加载上下文不能同时接纳两套字节。改为每次使用独立、可卸载的 `AssemblyLoadContext` 验证对应配置的 DUMP。回归在同一进程交替加载两套同名但行为不同的校验器四次，核对各自结果；同时已用两份实际 DUMP 核实加载隔离。失败的第二轮保留于 `.tmp.imp29-hardening-2`。

第三轮严格编译又拦截了原 XEL 探针的 CS8602 警告：普通 `Require` 断言不能让编译器推导事件位置非空。改为先取得位置局部变量，缺失时明确抛出异常，再使用该变量校验并记录字节范围；没有使用空引用抑制符或关闭警告。XEL 的双配置和剩余性能探针均完成零警告严格编译预检。该中止记录保留于 `.tmp.imp29-hardening-3`，当前最终选择见 `accepted-runs.json`。

## 完整复跑结果

最终目录 `.tmp.imp29-hardening-4`，由完整 `Run-Acceptance.ps1` 顺序执行，36 阶段通过、0 失败。799 项执行源码及每种配置 17 个 DLL 均通过执行前后校验；13 个兼容性冻结样例字节保持不变。文档在汇总时另行记录，未生成覆盖率报告。

| 验证 | Debug | Release |
| --- | --- | --- |
| solution 强制重建 | 0 警告 / 0 错误 | 0 警告 / 0 错误 |
| 全量 xUnit | 2791 通过 / 0 失败 / 0 跳过 | 2791 通过 / 0 失败 / 0 跳过 |
| 新增脚本回归 | 25 通过 | 25 通过 |
| SQL 2019/2025 语义、观察器、类型 | 100 检查通过 | 100 检查通过 |
| 审核写回、备份、提交后报告失败 | 通过 | 通过 |
| 双版本索引目标与回滚 | 10 目标通过 | 10 目标通过 |
| CLI/配置/会话及无效输入 | 22 + 3 检查通过 | 22 + 3 检查通过 |
| WPF 实际窗口测试宿主 | 8 组通过，0 绑定错误 | 8 组通过，0 绑定错误 |
| 未知异常与日志 | 1 个新原生 DUMP，通过结构校验 | 1 个新原生 DUMP，通过结构校验 |
| 本轮真实 XEL | 2 事件、1 分组，筛选/字节追溯/损坏拒绝通过 | 同左 |

当前顺序性能观测如下；冷/热各一次，不构成统计基准或 SLA：

| 输入 | 冷/热总时间 ms | 冷/热 UI 调度 P95 ms | 终态 |
| --- | --- | --- | --- |
| 100 算子 | 1804.6 / 1668.2 | 149.8 / 59.5 | Ready |
| 1000 算子 | 1883.7 / 1947.5 | 221.6 / 83.5 | Ready |
| 5000 算子 | 2567.4 / 2453.3 | 147.8 / 39.9 | Ready |
| 两事件 XEL | 320.4 / 63.8 | 61.9 / 40.4 | Partial（另有非死锁记录） |

计划冷打开仍未达到 100 ms P95 候选目标，UI-15 继续部分验收。本轮没有修改生产报告窗口；其 Release DLL 字节与原可见桌面记录相同，但没有新增实机 DPI、辅助技术或外部阅读器覆盖。47 项矩阵仍为 38 项范围内关闭、5 项缓解、4 项部分验收，不授予发布许可。

## 检索依据

优先核对 [Microsoft MSBuild 命令行文档](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-command-line-reference) 的 `warnAsError` 行为，以及 [Get-FileHash 官方文档](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.utility/get-filehash?view=powershell-7.5) 的 SHA-256 一致性语义。另核对 Microsoft 维护的 [dotnet/msbuild LoggingService](https://github.com/dotnet/msbuild/blob/main/src/Build/BackEnd/Components/Logging/LoggingService.cs) 对警告升级为错误的实现。网页搜索/连接器不可用时，本轮通过 HTTPS 读取这些一手来源；本次缺陷属于验收工具，不需要 SQL CSS 案例来代替工具契约。

加载上下文隔离依据 [Microsoft AssemblyLoadContext 文档](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext)；这里用于装载两种构建的验证器，不作为安全沙箱。本轮运行环境为 PowerShell 7.6.5、.NET SDK 10.0.400 和 .NET 8 目标程序集。

## 复跑与限制

需要 Windows、PowerShell 7、.NET SDK、SQLLocalDB 2019/2025、sqlcmd，以及原有两个专用语义测试实例。从仓库根运行 `pwsh -NoProfile -File ./DOCS/verification/IMP-29/Run-Acceptance.ps1`。每次创建全新目录；只运行单阶段时必须提供本次 `Run-BuildTests.ps1` 生成的 `-BuildEvidence`。修改执行源码后必须重新构建及执行场景，不能更新指纹来接受旧结果。

当前交付选择见 `accepted-runs.json`。新复跑完成后明确选择对应目录及预期测试数，再运行 `Write-Validation.ps1`，最后执行 `-VerifySources`。真实 DPI、多屏、辅助技术、外部报告阅读器和性能目标仍按原矩阵保留；本轮没有执行 IMP-30、提交或发布。
