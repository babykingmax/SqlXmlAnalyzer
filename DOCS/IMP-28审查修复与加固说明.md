# IMP-28 审查修复与加固

日期：2026-09-10。修复一项 P2：旧配置只增加版本字段时，重新格式化整个 JSON 导致合法输入超出 1 MiB 限制。修复范围为 `RuleConfigurationDocument.WithCurrentSchema()`，版本、规则语义、CLI 参数和会话保存行为保持原契约。

## 缺陷与修复

审查复现的旧配置包含 200,000 个数值的未知扩展数组，UTF-8 大小为 **400,026 字节**。旧实现通过 `JsonNode.ToJsonString(WriteIndented = true)` 重写整个文档，缩进使结果超过 1 MiB，迁移失败。关闭缩进后，中文属性名/值、emoji 和 `<` 仍会因默认字符转义而膨胀；另外四项失败测试复现了这一点。

修复利用 `RuleConfigurationDocument` 的已有不变量：`Json` 是由 `Parse` 校验过的单个根对象，并且不可变。在根对象开始处插入新版本成员；空对象不添加逗号，已有成员的对象添加逗号。只序列化可信的版本字段名称和值，其他文本不重新编码或格式化，随后仍由 `Parse` 完整验证新文档。已有版本的文档直接返回原实例。

这保留了原字段名转义、空白、大小写、规则别名、未知规则、超大数字表示、字符串转义和原始 Unicode 文本。没有改动全局 JSON 编码器、报告输出或配置字段编辑逻辑。复现样例修复后为 **400,059 字节**，有效配置指纹不变。

生成副本前按严格 UTF-8 计算原文与新增成员的合计字节数。超过 1 MiB 时抛出预期的 `InvalidDataException`，错误码为 `CONFIG_MIGRATION_BUDGET_EXCEEDED`；不创建新文件、不修改原文或活动配置。预算没有放宽，已有原始空白仍计入预算。旧无版本配置仍可直接读取，迁移仍通过 `SaveAsAsync` 保存新副本。

## 回归与验证

新增 **15 项**公共接口测试：

- 大数组，以及中文值、中文属性名、emoji、HTML 敏感字符的迁移大小与内容保留。
- 三种空对象/空白形式，转义属性名、未知数字和原始值、规则别名及未知规则。
- 两种已声明当前版本的拼写形式，验证幂等性及文本不变。
- 含大量中文的精确 UTF-8 边界：旧文档 1,048,543 字节可迁移为 1,048,576 字节；旧文档 1,048,544 字节因新增字段超出一字节而拒绝。
- 大配置实际另存/重新载入、拒绝覆盖、取消与原件保留；超限迁移失败时保留源文件和活动配置。

Debug/Release 完整构建各 **0 警告、0 错误**；全量各 **2754 通过、0 失败、0 跳过**。既有会话与配置诊断测试实际生成并验证 Windows DUMP，覆盖捕获失败、主错误保留及日志门禁：Debug 为 Debug/Warning/Error/Critical，Release 仅 Error/Critical。迁移超限属于预期输入错误，不触发未知异常 DUMP。

证据、失败阶段 TRX 摘要、最终测试名称、程序集及源码哈希见[验证目录](verification/IMP-28-hardening/README.md)。原 IMP-28 的 2739 项结果和实际 CLI/会话转换样例保留为修复前记录。本次未新增 WPF/SQL Server/发布回退验收，也未生成覆盖率报告；IMP-29 和 IMP-30 仍独立进行。

## 检索依据

按用户要求优先查阅 Microsoft 官方资料，并核对公开上游源码（2026-09-10）：

- [System.Text.Json 字符编码](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/character-encoding)：默认会转义非 ASCII 字符，解释了仅关闭缩进仍然失败的原因。
- [JsonWriterOptions.Indented](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonwriteroptions.indented?view=net-8.0)：格式化会增加缩进、换行和空白。
- [JsonElement.GetRawText](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonelement.getrawtext?view=net-8.0)：原始输入表示可以与再次序列化的文本区分；回归测试用原始值核对未知内容保留。
- [.NET 8 JsonDocument 上游实现](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Text.Json/src/System/Text/Json/Document/JsonDocument.cs)：`WriteElementTo` 会经 writer 写出属性名和字符串，不能把 DOM 重新序列化当作原文保持。

本轮 GitHub 插件未暴露可调用的连接器工具，使用 HTTPS 读取 `dotnet/runtime` 公开源码。检索工具连接失败后，官方页面和上游源码均通过 HTTPS 获取并核对。本问题属于本地 .NET JSON 表示与字节预算，无须采用 SQL Server CSS 或社区替代方案。
