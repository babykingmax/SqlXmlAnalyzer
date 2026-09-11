# IMP-22 统一报告模型与脱敏预览

2026-09-10 更新：[审查修复与加固](./IMP-22审查修复与加固说明.md)已将预算提前至构建/渲染阶段，新增总记录上限、全缺失指标紧凑投影及游标结构保留；下文的原始实施计数和输出证据保留为历史快照。

实施日期：2026-09-09。对应 D08、UI-07、UI-10、R01。实现、单元测试、真实 WPF 和跨格式文件核对已完成；浏览器工具禁止访问 `file://`，实际浏览器离线打开仍待人工验收。IMP-23–IMP-30 继续待实施。

## 使用方式

1. 在计划工作区选择 Batch / Statement / QueryPlan，或在死锁工作区选择事件。
2. 点击现有 HTML、PDF、Word 报告按钮，打开统一的“报告范围与脱敏预览”。原脱敏计划按钮打开同一窗口，默认选择 sqlplan。
3. 核对范围、事实数、诊断数和 Hit / NoHit / Skipped / Failed。默认使用脱敏快照；“脱敏范围和样例”展示类别、数量、未覆盖项和最多 30 个前后样例。样例原文只用于本地预览，不写入脱敏报告。
4. 可切换 HTML、PDF、Word、JSON、SVG；计划还可输出 sqlplan。SVG 仅包含最多 80 个节点的静态图，sqlplan 仅包含当前选择的 XML；窗口明确提示这两类文件没有完整诊断。
5. 保存使用新文件名。取消或失败不发布不完整报告，已存在文件不会覆盖。取消勾选脱敏会明确显示“未脱敏”，不会把原始报告标为脱敏成功。

窗口固定使用打开时的选择快照；后台继续选择其他语句不会改变已经核对的内容。重新选择后应重新打开报告。导出期间锁定格式和隐私模式，取消按钮会取消任务并关闭窗口。

## 统一模型与范围

`SqlXmlAnalyzer.Core.Reporting.DiagnosticReport` 是不持有可变 XML、控件或分析服务的不可变快照。包括协议与产品版本、生成时间、范围、来源元数据、算子事实投影、诊断、规则运行记录、关系图节点/边及当前范围 XML。诊断数和运行数分开统计，Skipped 不冒充成功；有指标时保留完整数值、状态、估算/实际类型及聚合方式，已知单位保留。完全缺失指标的算子保留身份，以 `Metrics/State=Missing` 和 `Metrics/Value=N/A` 紧凑表示。

计划工作区和工厂共享范围过滤：保留当前语句/QueryPlan 及适用的文档级诊断，排除其他语句与 `RULE_OUTSIDE_SCOPE` 运行记录。结构位置采用 `B/S/Q/O` 序号，原 NodeId、源路径和诊断 ID 另列；重复 NodeId 不会串到其他语句。选择 XML 不包含另一个可选嵌套语句。工厂校验文档身份及源版本；死锁报告校验事件与分析指纹，拒绝其他事件的诊断或已改变的源。

HTML / PDF / Word 使用同一个规范正文，JSON 序列化同一个模型，GUI 预览也读取这个快照。CLI 计划扫描 JSON 新增 `DiagnosticReport` 字段，范围为 Document；旧字段保留兼容，不将整个旧扫描 DTO 声称为脱敏输出。原 `HtmlAnalysisReport`、`PortableAnalysisReport` 及其服务保留兼容接口，仍明确属于旧原始输出；桌面报告按钮已切换到新模型入口。

## 脱敏策略与边界

`ReportRedactionService` 在原始快照副本上执行策略 `IMP22-1.0 + IMP05-1.0-SQL2022-1.571`。正文自由文本整体替换，覆盖 SQL、对象、源路径/指纹、诊断说明、配置、事实中的文本和图标签；字典键也按敏感数据处理。保留工具定义的规则 ID/版本、合并诊断的全部来源规则、严重度、置信度、状态、结构定位、数值及已知指标单位。诊断行不会静默消失，但其自由文本解释会被替换；如需查看完整建议，应在本地选择原始模式。

- 执行计划 XML 继续使用 IMP-05 字段目录，处理 SQL、参数、常量、对象名、注释/CDATA 等；未知字段、命名空间或非法结构值阻止整个脱敏快照发布。
- 死锁 XML 是结构示意副本：仅允许明确列出的元素名，删除所有属性（包括属性名）、文本、注释和处理指令。原始进程/资源属性保留在报告事实的原始模式中；脱敏后的诊断报告保留单独的结构图定位。未知元素或命名空间阻止脱敏输出。此 XML 不作为可重新分析的 xdl 导出。
- 预览同时显示处理和未覆盖数量，阻断时 `Report` 为 null、保存按钮禁用；原始输入和已分析快照不改变。
- 脱敏图从脱敏模型重新生成，不捕获未脱敏界面的截图。PDF/Word 的图来自同一静态 SVG；不使用原始 XML 图标签或嵌入未处理的源图像。

不声明任意 XML、任意第三方规则插件均获得隐私认证；当前协议信任本工具定义的结构字段和规则 ID。图形只展示前 80 个节点，完整节点/边仍进入四种完整报告。模型构建、快照及渲染共用 800 万内容字符、10 万条总记录预算；编码输出另设上限。超限立即拒绝并要求缩小范围，没有截断后冒充完整报告。CLI 超限失败结果不会再次序列化大型旧模型，详见[最新加固说明](./IMP-22审查修复与加固说明.md)。

## 安全发布、异常与日志

`DiagnosticReportExportService` 先验证格式与扩展名，创建同目录唯一暂存文件，完成渲染并刷盘，最后以禁止覆盖的移动发布。写入失败、空结果或取消会清理本轮自有暂存文件；文件名冲突保留已有内容。存储设备和操作系统崩溃的持久性仍受文件系统保证约束。

输入、I/O、路径与取消走可恢复错误提示。未知预览/渲染/导出错误复用 `ExceptionPolicy` 和 `UnexpectedErrorReporter`，请求并校验 Windows minidump；同一异常去重，DUMP 或诊断组件失败时明确显示失败原因。正常动作日志不写报告正文或对象身份。Debug 记录 DEBUG、WARN、ERROR、CRITICAL；Release 仅 ERROR、CRITICAL。

HTML 对动态文本编码，使用内嵌静态 SVG，无 CDN 或 JavaScript；CSP 为默认拒绝，禁止脚本、连接、对象、表单与外部资源，仅允许所需内嵌样式和 data 图像。SVG 仅由固定标签/坐标和编码后的标签文本生成。

## 验证结果

新增 **31 项**回归。Debug、Release 全量各 **2342 通过、0 失败、0 跳过**；完整构建均 **0 警告、0 错误**。测试覆盖选择与嵌套范围、读取/输出行、N/A 与单位、不可变快照、源版本错误、死锁事件隔离、CLI 新模型、敏感标记、未覆盖阻断、六种实际格式、禁止覆盖、中断/取消清理、导出期间状态锁定、真实 DUMP/失败/去重及分模式日志。

两配置均通过真实 `App` / `MainWindow` / 报告窗口操作，各保留 5 张截图、12 个原始/脱敏格式文件，WPF 绑定错误为 0。样例选择 `B1/S2/Q1`：259 个事实字段、1 项诊断；输出行 100、读取行 1000。HTML、JSON、PDF、Word 的规范正文、事实数、RuleId 和定位一致；PDF 和 Word 渲染后的文本也逐行核对，不只检查文件存在。脱敏 XML、JSON、HTML、SVG、Word 内部 XML 和 PDF 文本均未检出测试标记。

PDF 使用现有 QuestPDF，Word 使用现有 DocX；没有新增 NuGet 依赖。本机 Word 只读渲染两种模式，验证输入 DOCX 字节不变。标准 `render_docx.py` 因缺少 LibreOffice 失败，已保留日志并使用本机 Word 完成渲染。原始/脱敏 PDF 分别 10/8 页，Word 渲染分别 14/11 页；全部页面生成 PNG 并核对版面。页数可以因格式和分页不同而不同，事实和正文不能不同。没有生成覆盖率报告。

| 验收状态 | Debug | Release |
| --- | --- | --- |
| 范围与报告预览 | [截图](./verification/IMP-22/wpf-Debug-eb05b1934a5e48228405522390e22fa2/plan-redacted-preview.png) | [截图](./verification/IMP-22/wpf-Release-bb8e2622c8054405b0e1d13152c4a638/plan-redacted-preview.png) |
| 处理类别、数量及前后样例 | [截图](./verification/IMP-22/wpf-Debug-eb05b1934a5e48228405522390e22fa2/redaction-categories-samples.png) | [截图](./verification/IMP-22/wpf-Release-bb8e2622c8054405b0e1d13152c4a638/redaction-categories-samples.png) |
| 未覆盖字段阻止保存 | [截图](./verification/IMP-22/wpf-Debug-eb05b1934a5e48228405522390e22fa2/unsupported-blocked.png) | [截图](./verification/IMP-22/wpf-Release-bb8e2622c8054405b0e1d13152c4a638/unsupported-blocked.png) |

源文件、构建日志、TRX、截图、各格式文件及核对结果的 SHA-256 见[验证摘要](./verification/IMP-22/summary.json)。日志/TRX 位于 `.tmp.imp22-*`；DUMP 由测试验证后清理。历史 IMP-20/21 证据保持不变。

**未完成的环境验收：** 浏览器工具的安全策略明确禁止 `file://` 本地报告地址，未尝试通过本地服务器、其他浏览器或调试协议绕过。因此“浏览器实际离线打开”没有通过声明；已完成静态 HTML/CSP/SVG 和全部内容核对。没有实测全部浏览器、DPI/可访问性或大型报告性能，未改变 SQL 应用权限。

## 复现与依据

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug
dotnet build SqlXmlAnalyzer.sln -c Release
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release
& DOCS/verification/IMP-22/Run-WpfVerification.ps1 -Configuration Debug
& DOCS/verification/IMP-22/Run-WpfVerification.ps1 -Configuration Release
& DOCS/verification/IMP-22/Render-WordReports.ps1 -Directory <WPF输出目录>
python DOCS/verification/IMP-22/Verify-ReportFiles.py <WPF输出目录>
```

Word 渲染脚本需要安装 Microsoft Word；Python 校验脚本使用 pypdf、pypdfium2、Pillow。应用输出本身不依赖 Word 或 Python。`Write-VerificationSummary.ps1` 接受 Debug/Release 证据目录并核对日志、计数及文件哈希。

设计依据包括 [Microsoft File.Move 的禁止覆盖重载](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move?view=net-8.0)、[Microsoft WPF 属性通知契约](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/data/how-to-implement-property-change-notification)，以及既有 [IMP-05 脱敏字段与输出契约](./IMP-05脱敏覆盖与输出入口验证.md)、IMP-12 诊断协议及 IMP-20/21 选择与审核约束。QuestPDF SVG/图像方法依据项目已安装 2026.6.0 包的官方 XML 文档核对。
