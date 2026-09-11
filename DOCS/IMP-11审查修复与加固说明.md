# IMP-11 审查修复与加固说明

日期：2026-09-09（Asia/Taipei）。范围：修复本轮 review 的 P2 线程编号解析问题，并加固同一解析、归一化和聚合边界。

## 修复结果

此前 `NumberStyles.None` 拒绝合法的 `Thread="+0"`，将线程身份标为未知，连带使 `RowsPerExecution` 变成 Ambiguous。估算 1 行、实际 20,000 行、执行 1 次的计划因此漏掉基数告警。

`ReadThread` 现在按 `xsd:int` 接受十进制前导正负号，使用 `InvariantCulture` 和 `Int32` 范围检查，只移除 XML 允许的首尾空白（空格、Tab、CR、LF）。原始 `Thread` 字符串仍保存在只读 `SourceAttributes` 中。

- `0`、`+0`、`-0`、`0000`、`+0000` 归一为同一编号；重复记录继续保留，但聚合标为 Ambiguous，防止等价拼写绕过去重。
- `+01` 等正编号参与 worker 统计，零编号作为协调线程；线程重排不会改变重复执行分母或倾斜统计。
- 负整数属于可解析的 `xsd:int`，保留其数值。本工具尚无负编号的线程角色契约，因此不推导逻辑次数或 worker 分布；完整原始计数仍可汇总。
- 小数、指数、千分位、内部空白、非 XML 空白、非 ASCII 符号/数字及 Int32 溢出均保持未知，不借用当前区域的数字格式。
- 非法或角色未知的编号记录不含输入值的 Warning，不抛出异常或生成 DUMP。未知提取异常仍由既有 `ExceptionPolicy` 生成经验证的原生 DUMP，且保留原始异常。

未调整规则 ID 或严重度阈值；变化是合法有符号编号不再导致基数、零行及线程倾斜诊断被错误跳过。

## 检索依据

按照 Microsoft 官方资料优先的顺序核对，2026-09-09 以下页面均通过 HTTPS 成功读取：

| 来源 | 用途 |
| --- | --- |
| [Microsoft Showplan SQL Server 2019 XSD](https://schemas.microsoft.com/sqlserver/2004/07/showplan/sql2019/showplanxml.xsd) | `RunTimeCountersPerThread.Thread` 为必需 `xsd:int`；测试继续使用仓库固定 schema，离线验证完整合成计划 |
| [Microsoft NumberStyles 文档](https://learn.microsoft.com/en-us/dotnet/api/system.globalization.numberstyles?view=net-8.0) | 确认前导符号需由 AllowLeadingSign 明确启用，符号解释受 NumberFormatInfo 控制 |
| [Microsoft XmlConvert.ToInt32 文档](https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlconvert.toint32?view=net-8.0) | 核对 XML 字符串到 Int32 的转换与失败行为 |
| [.NET 8 XmlConvert 源码，dotnet/runtime v8.0.0](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Private.Xml/src/System/Xml/XmlConvert.cs) | 对照成熟实现的前导符号和固定区域设置；生产代码使用 TryParse，避免把常见坏字段作为异常处理 |

固定 XSD 的 SHA-256：`845B3FAA55748D442DFC71071315FF1EBB611E4E298E8D06A21EBF946B8C93A8`。负编号的角色弃权是本工具的保守约定，不声称 Microsoft 定义了负编号的执行角色。

本轮网页搜索工具连接失败，改用 PowerShell 直接读取官方页面及 GitHub 原始源码。GitHub 插件技能已读取，但本会话未提供可调用的 GitHub 连接器检索工具。官方契约和 .NET 源码已足以确认此解析缺陷，未用社区猜测替代依据，也未把 CSS/社区文章列作已核验来源。

## 验证

- 新增 33 项回归：30 项解析/聚合/跨出口测试，3 项非法或负线程编号的日志与不生成 DUMP 测试。
- 修复前新增的 30 项测试中 15 项失败、15 项通过；修复后 30 项全部通过。结果见 [red.trx](implementation/IMP-11/hardening/red.trx) / [green.trx](implementation/IMP-11/hardening/green.trx)。
- 完整 Debug / Release 构建各 0 警告、0 错误；完整测试各 **1389 通过、0 失败、0 跳过**。包含原生 DUMP 校验、DUMP 写入失败、原异常保留、取消与日志分级。最新计数和指纹见 [verification.json](implementation/IMP-11/verification.json)，逐项结果见 [Debug](implementation/IMP-11/debug.trx) / [Release](implementation/IMP-11/release.trx)。
- 完整合成计划经 Microsoft XSD 验证后，检查规则、图节点、连线提示、属性面板与 Plan JSON：输出 20,000、逻辑执行 1、单次行数 20,000，原始 `+0` 保留，归一化 ThreadId 为 0。
- 真实 Release CLI `read` 和 `scan` 对 [signed-thread.sqlplan](implementation/IMP-11/hardening/signed-thread.sqlplan) 导出相同事实；此文件亦独立通过固定 XSD 验证。`read` 退出 0；`scan` 正确命中 RULE004 / RULE030 两项 Critical，按现有门禁策略退出 1，输入读取状态仍为 Success。见 [CLI 观察值](implementation/IMP-11/hardening/cli-observed.json)及 [read](implementation/IMP-11/hardening/cli-read.json) / [scan](implementation/IMP-11/hardening/cli-scan.json) 原始结果。
- Debug 记录 DEBUG/WARN/ERROR/CRITICAL；Release 仅 ERROR/CRITICAL。新增的坏编号恢复路径在 Release 下不输出诊断日志。

复现完整构建和测试：

```powershell
.\DOCS\implementation\IMP-11\Verify-IMP11.ps1
.\DOCS\implementation\IMP-11\hardening\Verify-ThreadCli.ps1
```

本轮未连接 SQL Server 采集真实计划，未补做完整 WPF 窗口交互或截图；合成计划的 XSD 和绑定检查不替代这些验收。原 IMP-11 的真实并行/循环计划验证及界面验收边界保持见 [实施说明](IMP-11集中算子事实与指标口径.md)。
