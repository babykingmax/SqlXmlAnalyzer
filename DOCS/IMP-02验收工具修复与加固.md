# IMP-02 验收工具修复与加固

日期：2026-09-08。适配器版本：**2.0**。本次已修复针对 IMP-02 新增工具提出的四项 P2 问题，修改范围为 `DOCS/acceptance/IMP-02/` 工具和相关说明；产品实现及原有 xUnit 测试未修改。

## 1. 修复对应表

| 审查问题 | 修复行为 | 回归验证 |
| --- | --- | --- |
| 失败回退原 SQL 被误判为满足条件 | R02/R19/R20 先检查 `IsSuccess`、Errors、解析错误和规则失败。失败单独标记 `ProbeExecutionFailed`；成功后才检查原 SQL 保留条件，并记录变更数量与判定原因 | 失败回退、成功无变更、成功但危险改写、吞掉的规则异常、解析错误、错误集合和错误协议类型 |
| 输出行与读取行列混淆 | 通过独立指标标记、中文标题或约定的列名称分别定位两列，核对绑定路径。支持 Binding 简写、显式 Path 与属性元素形式；未知或歧义形式不能通过 | 同时存在 ActRows/ActRowsRead 时通过；原错误绑定、交换绑定、缺失/重复列、歧义绑定不能通过 |
| 归档快照与实际编译探针不同 | 在运行开始按字节冻结工具、矩阵及辅助代码；随后从快照加载模块和矩阵，并校验 SHA-256 后复制 C# 源码编译；记录快照与编译输入的对应关系 | 编辑原文件后仍复制冻结内容；快照篡改会被拒绝；目标已有文件不覆盖 |
| 漏检运行期间新增源码 | 每个检查点重新执行 Git 清单检索并计算哈希，比较新增、移除、删除、恢复、内容和 HEAD 变化；未跟踪配置也纳入保护 | 未跟踪/已暂存源码、新配置、删除、内容变化、空提交造成的 HEAD 变化均能识别；生成目录不造成误报 |

共享判定与输入保护实现在 [AcceptanceInfrastructure.psm1](./acceptance/IMP-02/AcceptanceInfrastructure.psm1)，XAML 数据检查实现在 [PlanRowBindingInspector.cs.txt](./acceptance/IMP-02/PlanRowBindingInspector.cs.txt)。运行入口为 [Run-AcceptanceMatrix.ps1](./acceptance/IMP-02/Run-AcceptanceMatrix.ps1)，回归入口为 [Test-AcceptanceInfrastructure.ps1](./acceptance/IMP-02/Test-AcceptanceInfrastructure.ps1)。

## 2. 验证结果

运行编号：`20260908T083501166Z_c5b1e0766cfc_71955017`。HEAD：`c5b1e0766cfcdf28f0d3a27d3238f5f0d6ae1a15`，保留已登记的工作树改动。

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| 完整非增量构建 | 通过，0 警告、0 错误 | [构建日志](./acceptance/IMP-02/runs/20260908T083501166Z_c5b1e0766cfc_71955017/build.stdout.txt) |
| 原有 xUnit 套件 | 807 通过，0 失败，0 未执行 | [TRX](./acceptance/IMP-02/runs/20260908T083501166Z_c5b1e0766cfc_71955017/test-output/existing-suite.trx) |
| 工具回归检查 | 32 通过，0 失败；直接调用共享实现，使用内存 XAML 和独立临时 Git 仓库 | [逐项结果](./acceptance/IMP-02/runs/20260908T083501166Z_c5b1e0766cfc_71955017/infrastructure-tests.json) |
| 探针编译与运行 | 通过，探针编译 0 警告、0 错误 | [编译日志](./acceptance/IMP-02/runs/20260908T083501166Z_c5b1e0766cfc_71955017/probe-build.stdout.txt)、[原始观察](./acceptance/IMP-02/runs/20260908T083501166Z_c5b1e0766cfc_71955017/observed.json) |
| 实际编译输入 | 两个 C# 文件与归档快照 SHA-256 一致 | [对应清单](./acceptance/IMP-02/runs/20260908T083501166Z_c5b1e0766cfc_71955017/compiled-inputs.json) |
| 输入完整性 | 构建后、测试后、探针编译后、探针/CLI 结束后均无漂移 | [最终检查点](./acceptance/IMP-02/runs/20260908T083501166Z_c5b1e0766cfc_71955017/input-checks/after-probe-and-cli.json) |
| 产品反例矩阵 | 22 项 NotMet，0 项 ProbeExecutionFailed，0 个原产品问题关闭；运行器按约定返回 1 | [运行摘要](./acceptance/IMP-02/runs/20260908T083501166Z_c5b1e0766cfc_71955017/summary.json) |

32 项工具回归与 807 项 xUnit 测试分别计数，没有将原产品反例改成通过，也没有新增 Skip。SQL Server、真实 WPF 窗口和覆盖率测量未执行。

## 3. 证据保留与使用边界

- 原审查附件、IMP-01 基线和两次 v1 验收目录均保留，不覆盖 `deliverable-validation.json` 或历史运行结果。本次证据另存带 UTC/HEAD/随机编号的目录，最终哈希复核见 [本次交付校验](./acceptance/IMP-02/hardening-validation-20260908T083501166Z_c5b1e0766cfc_71955017.json)。
- 新运行使用 `schemaVersion=2`。退出码 1 包含产品必要条件未满足或重构执行失败；退出码 2 表示工具/构建/协议/输入漂移错误。任何非 `ProbeConditionMet` 状态都不能让整体条件成为通过。
- R02/R19/R20 的字符串条件仍是最小风险缓解检查，不证明 SQL 等价或完整作用域安全。成功且没有应用变更，也不能证明风险解释、GUI 候选状态或数据库语义已经验收。
- R21 检查 XAML 数据，不执行 XAML，也不替代实际窗口、数据上下文和报告显示验收。未知标题或绑定形式需要明确适配，不能猜测为通过。
- 输入哈希检查不是文件系统锁或隔离工作树，无法保证捕捉到两检查点之间发生且已恢复的瞬时修改。运行时应避免编辑产品源码；若观察到漂移，应启动新的验收运行，不接续使用混合版本的证据。

重复运行方法与适配器边界见 [工具 README](./acceptance/IMP-02/README.md)。
