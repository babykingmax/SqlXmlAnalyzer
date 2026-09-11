# IMP-29 验收证据

本轮独立目录；不覆盖 IMP-01～IMP-28 的历史结果。实施说明见 [IMP-29 完整构建测试与用户场景验收](../../IMP-29完整构建测试与用户场景验收.md)。

审查后的变更与回归见[修复说明](../../IMP-29审查修复与加固说明.md)。当前证据必须绑定到[执行时构建证明](evidence/build-evidence.json)，各场景另有 `*-attestation.json`。旧摘要仅在 [prior-review](prior-review/summary.json) 作为历史记录保留。

- [summary.json](summary.json)：双配置构建、原 12 项新增及本轮 25 项脚本回归、数据库/CLI/WPF/XEL、真实 DUMP 与顺序性能测量。
- [acceptance-matrix.md](acceptance-matrix.md)：22 个 R、9 个 D、16 个 UI 逐项判定。38 关闭、5 缓解、4 部分验收；不是发布许可。
- [sources.json](sources.json)：本轮源码、样例、验收工具及文档指纹；[artifacts.json](artifacts.json)：原日志/TRX/SQL 结果及复制证据的字节指纹。
- [accepted-runs.json](accepted-runs.json)：修复后最终结果的明确选择；每个阶段必须匹配同一份构建证明。旧探针失败记录仍在原本机目录，不能被误计为当前通过。
- [desktop/verification.json](desktop/verification.json)：可见桌面操作记录；[report-fixed.png](desktop/report-fixed.png)、[report-saved.png](desktop/report-saved.png) 和 [statement-2-redacted.html](desktop/statement-2-redacted.html) 是实际修复/保存证据。
- [screenshots](screenshots/plan-statement-2.png)：另有计划证据、多事件死锁、A/B、改写勾选、诊断包预览截图。离屏 WPF 宿主证据与可见桌面记录分开标注。

从 `E:/SqlXmlAnalyzer` 运行：

```powershell
pwsh -NoProfile -File ./DOCS/verification/IMP-29/Run-Acceptance.ps1
# 当前交付证据的只读校验（本机原始产物也须仍在）：
pwsh -NoProfile -File ./DOCS/verification/IMP-29/Write-Validation.ps1 -VerifySources
```

完整入口顺序执行 restore、Debug/Release solution build 与全量测试、两引擎语义/真实应用/DDL、CLI 和文件迁移、8 组 WPF、双配置未知异常 DUMP/日志、真实 XEL 捕获/检索、最后独立性能测量。需要 PowerShell 7。构建启用 `-warnaserror`，以原生退出码记录结果，不解析本地化警告摘要。各场景脚本单独复跑时必须指定 `-BuildEvidence <本次构建目录>/build-evidence.json`；源码或程序集变化时必须重新构建并执行。当前 `Write-Validation.ps1` 针对本交付的 `accepted-runs.json` 选择结果；新整体运行的阶段状态在它自己的 `stages.json`，须审核并映射新目录后再形成新的版本化报告。

记录中的 SQL、计划和死锁均为合成测试输入/隔离库采集。原生 DUMP、原始 XEL 与完整日志留在 `accepted-runs.json` 指向的本机目录，不随本目录发布；`evidence/debug` 与 `evidence/release` 有精确 Git 例外，可正常提交其中的审核摘要。SHA-256 用于识别字节，不构成真实性签名。冷/热各单次测量不证明普遍性能；未生成覆盖率报告。M7 的真实 DPI/多屏、辅助技术、外部阅读器和性能目标仍按矩阵保留，IMP-30 未执行。
