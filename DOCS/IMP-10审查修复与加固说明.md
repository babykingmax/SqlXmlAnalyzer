# IMP-10 审查修复与加固

日期：2026-09-08。针对 IMP-10 审查确认的四项问题完成修复，新增 22 项回归测试。Debug / Release 完整构建均为 0 警告、0 错误；全量测试各 1312 项通过，无失败或跳过。

## 1. 规则结果的实际归属

R035 的调用范围仍是 Operator，但根节点分支生成的是整个文档的索引/非 SARGable 汇总。此前 RuleEngine 将这个汇总附到当前算子，可能把第二条语句的 `audit.Users` 建议定位到第一条语句的 `sales.T`。

- `AnalysisResult.ResultScope` 表示该次结果的证据范围；未指定时使用规则元数据的调用范围。
- R035 全局汇总明确使用 Plan 范围，NodeId 为空，Location 只定位文档，Objects 为空。
- `AnalyzePlan` 对同一规则的文档汇总仅保留一次；算子级建议仍逐个保留，并携带自身对象与来源。
- `AnalyzeNode` 同样按照结果范围附加身份。R035 两个异常捕获点使用既有 ExceptionPolicy，未知错误不再只写 Warning 后被吞掉。

这是明确的输出变化：全局汇总的旧 NodeId 从 `0` 改为空，多语句中重复的全局汇总合并。RuleId、默认严重度和配置格式没有改变。8 份既有夹具的[对比记录](implementation/IMP-10-review/rule-differences.json)中，仅 `plan_missing_index.sqlplan` 的 R035 汇总 NodeId 出现预期变化，其余旧字段一致；新跨语句测试另行验证去重和保留局部建议。

## 2. 比较失败不打断 A/B 状态更新

控制器继续拒绝未指定范围或已失效的 QueryPlan，保留 `PLAN_SELECTION_REQUIRED` / `PLAN_SELECTION_NOT_FOUND`。比较 UI 在刷新边界处理异常，不让它穿透同步 PropertyChanged 回调。

两侧树先完成构建再发布；失败时两侧显示错误原因，清除先前结果。这样 A/B 交换、会话恢复、清空结果和清空历史都可以完成连续属性更新。有效选择恢复后重新显示比较树。完整 QueryPlan 选择界面仍属于后续 IMP-20/21。

预期选择错误记录 ERROR，不生成 DUMP。未知错误通过共享诊断策略生成并校验 minidump/异常侧车，同一异常跨控制器和 UI 只捕获一次；诊断文件写入失败时显示 DUMP 失败原因，仍保持选择状态可恢复。

## 3. CLI read 取消契约

`RunReadCommand` 将相同 CancellationToken 传给文档读取和身份构建，并在模型构建后、序列化后且发布输出前再次检查。构建过程中或构建刚结束时收到取消，退出码为 **130**，不输出 Success JSON。

序列化使用现有同步 System.Text.Json；取消在返回序列化结果后、写入标准输出前检查。此处没有承诺中断正在执行的同步序列化或回滚已经写出的标准输出。未知异常仍由 CLI.Main 的既有 DUMP 边界处理。

内部读取命令接受 IPlanDocumentBuilder，使回归测试可以确定性地在构建期间和结束时取消，无需依赖 Debug 日志、线程时序或大文件碰运气。常规 read / scan 的 JSON 层级和原始信息提示保留。

## 4. 限制 NodeId 在身份键中的大小

Microsoft Showplan SQL Server 2022 XSD 的 `RelOpType.NodeId` 是可选的 **xsd:int**。身份构建按有符号 Int32 范围读取，使用 InvariantCulture 生成规范十进制文本，最长 11 个字符。

| 输入 | 身份键与诊断 |
| --- | --- |
| `0`、`2147483647`、`-2147483648` | 有效，保留数值 |
| `+0001`、周围 XML 空白、8192 个前导零 | 规范化为短整数文本，源 XML 不修改 |
| 超出 Int32 范围、非整数文本 | NodeId 为 null，增加 `PLAN_NODE_ID_INVALID` |
| 缺失或空白 | NodeId 为 null，增加 `PLAN_NODE_ID_MISSING` |
| `01` 与 `+1` 等相同数值 | 增加 `PLAN_NODE_ID_DUPLICATE`，OperatorOrdinal 仍区分两个算子 |

所有原值仍通过源 XML 定位保留；无效或重复值不会丢失算子、覆盖源码映射或破坏父子关系。日志只记录消歧数量，不写入原始标识符。

独立 Release CLI 实测 1000 个子算子，见[测量结果](implementation/IMP-10-review/cli-measurement.json)和[复现脚本](implementation/IMP-10-review/Measure-Hardening.ps1)：

| 根 NodeId | 输入字符数 | 紧凑 Plan JSON 字符数 | 缩进 CLI JSON 字符数 |
| --- | ---: | ---: | ---: |
| 正常 `0` | 21,146 | 1,814,249 | 4,202,242 |
| 8192 位无效整数 | 29,337 | 1,816,496 | 4,204,642 |
| 8192 个零 | 29,337 | 1,814,683 | 4,202,692 |

超长值不再通过每个子算子的父键重复展开，输出大小接近正常输入。原审查探针的类似输入约生成 10 MB 紧凑身份 JSON；本次脚本记录的是当前实现和正常输入的对照，不冒充旧二进制重测。三个输入的算子数均为 1001，read 均返回 0。这是合成输入的资源放大回归，不代表生产容量测试或完整 XSD 校验。

## 5. 测试与证据

- [PlanIdentityHardeningTests](../SqlXmlAnalyzer.Tests/PlanIdentityHardeningTests.cs)：16 项，覆盖全局结果归属、去重与局部建议、Int32 边界、规范化、重复值、超长值、父引用、真实 WPF 服务的交换/清空/恢复及预期错误不生成 DUMP。
- [PlanIdentityErrorContractTests](../SqlXmlAnalyzer.Tests/PlanIdentityErrorContractTests.cs)：6 项，覆盖取消令牌传递与输出、CLI JSON、会话恢复/清空历史、未知异常的真实 Windows DUMP 和诊断写入失败。
- 现有 R035 单元测试同步调整全局结果的 NodeId 与 ResultScope 断言。
- 两种模式均验证 ERROR / CRITICAL；Debug 还包含 DEBUG / WARN，Release 不包含 DEBUG / WARN / INFO。DUMP 与侧车在隔离临时目录中生成、校验并清理，不提交内存文件。

首批测试在修复前复现了 12 个失败、2 个通过，见[记录](implementation/IMP-10-review/regression-before.log)。完成修复后，69 项相关测试全部通过；完整验证如下：

| 配置 | 完整构建 | 全量测试 |
| --- | --- | --- |
| Debug | [0 警告 / 0 错误](implementation/IMP-10-review/build-debug.log) | [1312 通过](implementation/IMP-10-review/debug-verified.trx) |
| Release | [0 警告 / 0 错误](implementation/IMP-10-review/build-release.log) | [1312 通过](implementation/IMP-10-review/release-verified.trx) |

构建使用 `-p:IntermediateOutputPath=obj/imp10-review-final/<Configuration>/ -nr:false` 避开本机已有 WPF 中间资源锁。完整构建后，测试使用 `--no-build --no-restore` 运行对应配置。未删除占用中的资源或修改用户其他改动。

独立进程 [read JSON](implementation/IMP-10-review/cli-read.json) / [scan JSON](implementation/IMP-10-review/cli-scan.json) 均验证退出 0。WPF 测试连接实际 PropertyChanged、比较服务和控件，在 STA 中运行；不是完整窗口自动化或 SQL Server/SSMS 验证。汇总与源文件哈希见[verification.json](implementation/IMP-10-review/verification.json)。

## 6. 检索依据

按 Microsoft 优先策略读取官方资料，再以官方 GitHub 实现补充。网页检索工具连接失败后，直接读取以下官方页面；当前 GitHub 插件没有可调用的连接器，公开源码通过官方仓库读取，无需第三方替代建议。

1. [Microsoft Showplan SQL Server 2022 XSD](https://schemas.microsoft.com/sqlserver/2004/07/showplan/sql2022/showplanxml.xsd)：核对 RelOpType 的 NodeId 为可选 xsd:int，未将其他类型的 NodeId 混作算子定义。
2. [Cancellation in managed threads](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads)：取消令牌传递、协作检查以及 OperationCanceledException。
3. [Best practices for exceptions](https://learn.microsoft.com/en-us/dotnet/standard/exceptions/best-practices-for-exceptions)：处理可恢复条件，并在操作未完成时保持或恢复状态一致性。
4. [dotnet/runtime CancellationToken.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Threading/CancellationToken.cs)：官方 ThrowIfCancellationRequested 实现与携带原令牌的取消异常。

前三级所需事实已由官方 schema、文档和本地复现直接支持，未为凑齐层级引用无关 CSS 或社区文章。
