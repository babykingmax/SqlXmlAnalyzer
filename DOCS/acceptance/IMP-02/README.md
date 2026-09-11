# IMP-02 验收工具与运行索引

主文档：[审查反例验收矩阵](../../IMP-02审查反例验收矩阵.md)。机器可读定义：[acceptance-matrix.json](./acceptance-matrix.json)。

当前适配器为 **v2.5**：在 v2.4 基础上，R04 要求输入非成功状态、稳定错误代码及真实 CLI 一致；R05 要求返回全部事件、拒绝未选择集合并能解析第二条。R01 仍要求成功的有效 XML、原文不变、已支持字段无标记、未知字段阻断且无部分载荷；失败或空输出不能当作脱敏通过。42 项工具回归及其他检查保留。详见 [IMP-06 实施与验证](../../IMP-06统一最低输入识别说明.md)和[联合加固核对](../../IMP-02-04加固与实施符合性核对.md)。旧适配器快照及历史证据保持不变。

## 本次运行结果

IMP-08 已完成当前展示纠错：两配置各 1175 测试、42 项工具回归通过，完整构建 0 警告/错误。新增 R07/R15/R22 最低条件满足，总计 13 满足、9 NotMet、0 探针执行失败；R12 模型顺序依赖仍为 NotMet，UI 已撤下其收益预测。v2.5 判定器未修改，WPF 独立验证比较、持续推演说明、事件切换和沙盒编辑；[实施说明](../../IMP-08展示纠错与证据边界说明.md)。

IMP-07 已修复 R06/R11/R21 当前字段与绑定问题，并完成成本与实际行数观测链路的审查修复；v2.5 判定器及最低条件未放宽。最新两配置各 1135 测试通过，10 项最低条件满足、12 项 NotMet。真实 WPF 单独进程验证两处表格、图提示、旧视图节点详情及估算/0/部分线程/重载场景；[加固说明及截图](../../IMP-07审查修复与加固说明.md)。

IMP-06 后续审查修复增加了深层命名空间、坏字节、跨入口编码和选择后刷新的回归，并适配验收探针的 IFileHandler.OpenRead；原最低判定条件保持不变。最新全量证据见 [IMP-06 加固](../../IMP-06审查修复与加固说明.md)。

| 运行 | 结果 | 证据 |
| --- | --- | --- |
| v2.5 Debug / Release（IMP-08） | 各 1175 测试、42 工具回归通过，构建 0 警告/错误；13 最低条件满足、9 NotMet；WPF 3 个比较场景、9 个推演状态及沙盒编辑通过 | [Debug](./runs/20260908T132838998Z_c5b1e0766cfc_a92698f8/summary.json)、[Release](./runs/20260908T132939519Z_c5b1e0766cfc_d40aa08b/summary.json)、[WPF 与汇总](../../implementation/IMP-08/verification.json) |
| v2.5 Debug / Release（IMP-07 审查修复后） | 两配置完整构建 0 警告/错误，各 1135 测试、42 工具回归通过；10 最低条件满足、12 NotMet、0 探针执行失败；WPF 两视图各 5 个运行时场景通过 | [Debug](./runs/20260908T130755683Z_c5b1e0766cfc_a4518124/summary.json)、[Release](./runs/20260908T130847895Z_c5b1e0766cfc_e528e6ee/summary.json)、[加固与汇总](../../implementation/IMP-07/hardening/verification.json) |
| v2.5 Debug / Release（IMP-07） | 两配置完整构建 0 警告/错误，各 1106 测试、42 工具回归通过；R06/R11/R21 最低条件满足；总计 10 满足、12 NotMet、0 探针执行失败 | [Debug](./runs/20260908T125119934Z_c5b1e0766cfc_2d768643/summary.json)、[Release](./runs/20260908T125213729Z_c5b1e0766cfc_0243fdb7/summary.json)、[WPF 与汇总](../../implementation/IMP-07/verification.json) |
| v2.5 Debug / Release（IMP-06 审查修复后） | 两配置完整构建 0 警告/错误，各 1044 测试、42 工具回归通过；7 最低条件满足、15 NotMet；另有 9 组真实 CLI 与主窗口选择/刷新流程复验 | [Debug](./runs/20260908T122224064Z_c5b1e0766cfc_b8ced8e6/summary.json)、[Release](./runs/20260908T122332967Z_c5b1e0766cfc_04004332/summary.json)、[加固说明](../../IMP-06审查修复与加固说明.md) |
| v2.5 Debug / Release（IMP-06 最终） | 两配置完整构建 0 警告/错误，各 1011 测试、42 工具回归通过；7 最小条件通过、15 NotMet；R04/R05 已满足最低条件 | [Debug](./runs/20260908T115707647Z_c5b1e0766cfc_9da94257/summary.json)、[Release](./runs/20260908T115743976Z_c5b1e0766cfc_2ce6d6ae/summary.json)、[独立构建布局说明](../../IMP-06统一最低输入识别说明.md) |
| v2.4 Debug / Release | 两配置全量构建 0 警告/错误，各 944 测试、42 工具回归通过；5 最小条件通过、17 NotMet；IMP-05 进程外层 17/17 | [Debug](./runs/20260908T101641783Z_c5b1e0766cfc_6f336732/summary.json)、[Release](./runs/20260908T101821071Z_c5b1e0766cfc_fc081b13/summary.json)、[IMP-05](../../IMP-05脱敏覆盖与输出入口验证.md) |
| v2.3 Debug / Release | 两配置完整构建 0 警告/错误，各 905 测试、38 工具回归通过；4 最小条件通过、18 NotMet | [Debug](./runs/20260908T095232442Z_c5b1e0766cfc_3a4bacdf/summary.json)、[Release](./runs/20260908T095314857Z_c5b1e0766cfc_db4a0d4e/summary.json) |
| v2.2 `20260908T093220366Z_c5b1e0766cfc_0420c7c1` | IMP-04 最终验证：完整 Debug 构建 0 警告/错误，886 测试通过，32 工具回归通过；R02/R03/R19/R20 最小条件通过，18 项 NotMet；Release 及进程验证另见实施说明 | [摘要](./runs/20260908T093220366Z_c5b1e0766cfc_0420c7c1/summary.json)、[逐项观察](./runs/20260908T093220366Z_c5b1e0766cfc_0420c7c1/acceptance-results.json)、[实施说明](../../IMP-04高风险改写限制与诊断说明.md) |
| v2.1 `20260908T090642185Z_c5b1e0766cfc_c45e1ec8` | IMP-03 最终验证：完整构建 0 警告/错误；852 测试通过；32 工具回归通过；R03 为 ProbeConditionMet，21 项 NotMet，0 个问题自动关闭 | [摘要](./runs/20260908T090642185Z_c5b1e0766cfc_c45e1ec8/summary.json)、[逐项观察](./runs/20260908T090642185Z_c5b1e0766cfc_c45e1ec8/acceptance-results.json)、[实施说明](../../IMP-03安全写回与恢复说明.md) |
| v2 `20260908T083501166Z_c5b1e0766cfc_71955017` | 工具回归 32 通过；完整构建 0 警告/错误；原有测试 807 通过；产品矩阵仍为 22 NotMet，0 个原产品问题关闭 | [摘要](./runs/20260908T083501166Z_c5b1e0766cfc_71955017/summary.json)、[工具回归](./runs/20260908T083501166Z_c5b1e0766cfc_71955017/infrastructure-tests.json)、[交付校验](./hardening-validation-20260908T083501166Z_c5b1e0766cfc_71955017.json) |
| `20260908T080801184Z_c5b1e0766cfc_6a19a7b0` | 探针完成；22 个必要条件全部 NotMet；已有测试 807 通过；0 个评审问题关闭 | [摘要](./runs/20260908T080801184Z_c5b1e0766cfc_6a19a7b0/summary.json)、[逐项预期与观察](./runs/20260908T080801184Z_c5b1e0766cfc_6a19a7b0/acceptance-results.json) |
| `20260908T080412884Z_c5b1e0766cfc_be2f8496` | 首轮工具启动失败，未产生产品断言结果；误将 dotnet run 的 --nologo 传入探针参数，后已修正 | [基础设施错误](./runs/20260908T080412884Z_c5b1e0766cfc_be2f8496/infrastructure-error.txt)、[探针 stderr](./runs/20260908T080412884Z_c5b1e0766cfc_be2f8496/probe.stderr.txt) |
| SQL 准备脚本 | 6 个脚本通过 TSql160Parser 语法检查，未执行 SQL Server | [语法检查记录](./sql-preparation-20260908T080833545Z-01767bf0.json) |

**历史 IMP-02 初始交付：** 22 项 NotMet，807 测试通过。**IMP-03 后：** 852 测试通过，R03 最小条件通过。**IMP-04 初交付后：** 886 测试通过，R02/R03/R19/R20 最小条件通过，18 项 NotMet。各阶段没有增加 Skip 或隐藏失败，不把最小条件通过直接等同于整个问题关闭。

历史 v1 有效运行的关键证据（当前结果见上表首行）：

- [完整 build 日志](./runs/20260908T080801184Z_c5b1e0766cfc_6a19a7b0/build.stdout.txt)：0 警告、0 错误。
- [test 日志](./runs/20260908T080801184Z_c5b1e0766cfc_6a19a7b0/test.stdout.txt)、[原始 TRX](./runs/20260908T080801184Z_c5b1e0766cfc_6a19a7b0/test-output/existing-suite.trx)：完整既有测试。
- [原始观察](./runs/20260908T080801184Z_c5b1e0766cfc_6a19a7b0/observed.json)：R01–R20 的真实服务调用，以及 R21 XAML / R22 ViewModel 观察。
- [CLI 输出](./runs/20260908T080801184Z_c5b1e0766cfc_6a19a7b0/cli-invalid-input.stdout.txt)：无关 XML 当前仍 Passed，进程退出码 0，R04 尚未修复。
- [输入与候选来源清单](./runs/20260908T080801184Z_c5b1e0766cfc_6a19a7b0/fixture-manifest.json)：19 个合成输入、3 个实际引擎生成的候选 SQL、6 个既有仓库夹具，共 28 项；服务器执行均为 false。
- [源码与历史附件保护检查](./runs/20260908T080801184Z_c5b1e0766cfc_6a19a7b0/protected-input-drift.json)：空数组。
- [命令与时间](./runs/20260908T080801184Z_c5b1e0766cfc_6a19a7b0/commands.json)、[证据指纹](./runs/20260908T080801184Z_c5b1e0766cfc_6a19a7b0/artifact-manifest.json)。

## 重新运行

在仓库根使用 PowerShell 7+：

```powershell
pwsh -NoProfile -File DOCS/acceptance/IMP-02/Run-AcceptanceMatrix.ps1 -RepositoryPath E:/SqlXmlAnalyzer -Configuration Debug
pwsh -NoProfile -File DOCS/acceptance/IMP-02/Run-AcceptanceMatrix.ps1 -RepositoryPath E:/SqlXmlAnalyzer -Configuration Release
pwsh -NoProfile -File DOCS/acceptance/IMP-02/Test-SqlPreparation.ps1 -RepositoryPath E:/SqlXmlAnalyzer
pwsh -NoProfile -File DOCS/acceptance/IMP-02/Test-AcceptanceInfrastructure.ps1
```

第一个脚本每次创建新的 UTC/HEAD/随机编号目录，运行完整还原、非增量构建、测试及临时编译的独立探针；不连接数据库、不执行候选 SQL、不改生产源码。探针代码以 `.cs.txt` 保存，临时工程建在系统临时目录，不参与默认测试发现。第二个脚本只对六个准备脚本做语法检查，定点读取已知的 Debug ScriptDom 程序集，不枚举 bin/obj。

验收运行器约定自身退出码：0 表示本适配器的最小条件全部满足；1 表示存在 `NotMet` 或 `ProbeExecutionFailed`；2 表示基础设施/输入结构/构建/输入漂移等错误。`ProbeExecutionFailed` 表示被测重构返回失败、记录了解析/规则错误，或支持范围内的脱敏夹具未生成 Ready 有效 XML，单独计数；失败回退原 SQL 不能变成通过条件。应同时检查 `summary.json` 或 `infrastructure-error.txt`，避免宿主 shell 对退出码的转换造成误解。**即使返回 0，也不自动关闭任何评审问题。**

当前版本正常完成观察后应返回 1。不得将其接入强制 CI 再长期豁免；对应修复中的正式回归测试应与实现一起变绿。接口改动导致无法编译或观察字段变化时，先升级适配器并另存新版本证据，不能把异常当成“修复成功”。

交付校验时收紧了源输入排除表达式，使 `publish.ps1` / `publish.bat` 保留在后续保护清单中，仅排除生成的 publish 目录。已运行的历史适配器快照不修改；本次额外对两文件与 IMP-01 指纹做了复核，结果包含在 `deliverable-validation.json`。该采集范围修正未改变 22 项断言或产品代码。

## 适配器边界

- R01–R20 从原审查探针派生，保留原反例语义并导出实际使用的输入；没有修改 `DOCS/review-evidence/`。
- R14 的旧输入只有表名；R15 的旧指标模型以数值表示缺失；R16 的旧接口只返回首个比较树。三项需随领域模型升级适配器，当前输出只证明已有缺口。
- R02/R19 的探针检查默认保留表达式/表变量，是风险缓解条件；SQL 结果等价必须另行验证。
- v2.1 的 R03 使用新写回服务和备份创建失败注入；合成改写只负责稳定进入写回分支，不承担 SQL 语义验证。扩展文件故障由正式 xUnit 测试覆盖，真实 GUI 应用入口仍按 IMP-19/21 验收。
- R02/R19/R20 在成功且无解析/规则失败时才判定 SQL 条件；观察保留错误、规则失败及变更数量。v2.3 还要求与固定输入完全相同、零变更，并验证 SafetySkips 的 RuleId/原因/前提及可见警告；不能由此推断 SQL Server 等价性或完整 GUI 流程已验收。
- R21 分别定位输出行与读取行，要求两视图各有一列且分别绑定 `ActualRows` / `ActualRowsRead`。识别独立英文指标标记、中文行数标题或约定的 `x:Name`，支持简写、显式 `Path` 和属性元素绑定；未知/歧义绑定不能通过。R22 为真实 ViewModel 初始文字读取。可见控件、切换后说明及 DPI/主题需人工或 UI 自动化验收。
- 源码不变与旧测试通过不说明业务正确性；本轮没有新增覆盖率统计或实际界面/数据库运行记录。

## v2 的可追溯输入与回归检查

- `adapter/` 按原始字节保存运行器、矩阵、探针、绑定检查器、共享模块与回归脚本。随后从快照加载模块/矩阵并复制 C# 源码，禁止重新从工作区编译探针；`adapter-snapshot-manifest.json` 和 `compiled-inputs.json` 记录 SHA-256 对应关系。
- 源输入通过 `git ls-files` 获取，包括非生成、非 DOCS 的已跟踪和未跟踪文件，以及四份原审查附件。构建后、测试后、探针编译后、探针/CLI 结束后重新获取清单及 HEAD，识别新增、移除、删除、恢复和内容变化；各检查点另存 `input-checks/`。`publish.ps1` 属于源码，生成目录仍排除。
- 输入漂移返回退出码 2，不生成正常完成摘要。Git/哈希读取失败同样停止。检查点用于发现持续到检查时的改动，不是文件系统锁或隔离工作树；运行期间仍应避免编辑产品源码。
- 独立回归脚本直接调用共享实现，在内存构造 XAML，并在系统临时目录的新 Git 仓库模拟源码/配置/HEAD 变化。测试仓库由脚本安全清理，不改本仓库 Git 状态。每次完整验收自动运行这些检查，结果另存 `infrastructure-tests.json`；单独运行则另存 `regressions/`。
- 回归脚本需要 PowerShell 7+、Git 和 .NET 的内存 C# 编译能力，无需额外 Pester 包，不连接数据库、不启动 WPF 窗口。它与原有 xUnit 套件分别计数。

SQL 与 UI 后续按[SQL 说明](./SQL与真实夹具验证说明.md)和[WPF 脚本](./WPF验收操作脚本.md)执行。来源模板：[fixture-record-template.json](./fixture-record-template.json)。

命令默认超时 900 秒，可用 `-CommandTimeoutSeconds` 设为 1–3600；子进程超时后终止进程树并保留 stdout/stderr 和 `.command.json`。只有流已完整收集且进程正常退出后才进入相应业务检查；失败路径在 finally 完成证据清单。启动前置失败不产生虚假的产品状态。
