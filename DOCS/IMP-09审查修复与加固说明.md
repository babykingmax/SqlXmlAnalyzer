# IMP-09 审查修复与加固

修复日期：2026-09-08。本次关闭统一读取契约审查发现的四项缺陷，新增 20 项回归测试。未改变分析规则 ID 或严重级别；未知异常继续走现有 DUMP、异常侧车和日志机制。

## 1. 修复结果

| 缺陷 | 触发与原行为 | 修复后行为 |
| --- | --- | --- |
| 同级节点定位的二次增长 | 每条记录都调用 `ElementsBeforeSelf(...).Count()`，反复遍历同一批兄弟；8 万条记录约需 12.1 秒 | 每次识别使用独立位置缓存，同一父元素的兄弟只计数一次；按展开名称分别计数，保留命名空间、源行列和序号 |
| 遍历中的取消未生效 | 全部为不支持记录的集合在取消后仍计算位置，最后返回 Invalid | 集合遍历、兄弟索引、祖先路径、规范化副本及结果提交前检查令牌；异步读取返回 Cancelled，丢弃未完成结果 |
| 空 XE 死锁载荷被遗漏 | 前一个事件为 `<deadlock-list/>`、后一个有效时返回 Success，后者被标成事件 1 | 空载荷占用一个源序号并生成 `INPUT_DEADLOCK_STRUCTURE`；混合集合为 Partial，有效事件仍为 2；全部为空时 Invalid，保留各条诊断 |
| CLI 全局取消未传递 | Ctrl+C 被全局处理器拦截，但 redact 未收到令牌，refactor 又建立独立令牌 | Main 统一拥有取消源；read、scan、redact、refactor 接收同一令牌；取消返回 130，重构取消后不另写失败报告 |
| 编码声明被 1024 字节截断 | 合法 Latin-1 声明中的 encoding 出现在截断位置之后，流入口误按 UTF-8 解码并返回 ReadError | 在现有字节/字符预算内扫描到声明实际结束，定期检查取消；声明匹配使用非回溯正则；保留严格解码、BOM/签名优先、DTD 禁止和 XmlResolver=null |

表中前两行共同对应位置遍历审查项。缓存仅在一次识别内存在；同一 XDocument 后续修改后再次识别会重新计算位置，不使用静态缓存。脱敏文件读取也把取消令牌传入 SafeXmlHelper 的读取和解析检查点。

## 2. 检索依据与适用范围

按要求优先查阅 Microsoft 官方资料。取消、编码和 LINQ XML 遍历的直接依据已由官方资料及 .NET 8 源码覆盖；未使用社区建议替换现有 SQL Server 领域规则。

- Microsoft 说明协作式取消需要将令牌传递给监听操作，并由操作检查取消状态。本次统一 CLI 令牌及增加循环检查据此实施。[Cancellation in managed threads](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads)
- Microsoft 区分 XmlReader 的字节流编码识别和 TextReader 已解码字符输入；这支持文件/流入口正确处理编码声明后再进入统一 XML 解析。本次兼容既有 BOM 与声明不一致的行为，同时取消任意 1024 字节限制。[XmlReader.Create](https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlreader.create?view=net-8.0)
- 官方死锁指南给出 `xml_deadlock_report` 和 `RingBufferTarget/event` 的提取结构。空包装无法提供死锁图；把它标为未处理记录是本项目 Partial 契约的处理决定，不声称官方规定了本项目状态码。[SQL Server deadlocks guide](https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-deadlocks-guide?view=sql-server-ver16)
- 通过 GitHub CLI 核对 .NET 8 稳定版本源码：`GetElementsBeforeSelf` 从父节点内容逐一向前遍历，反复 Count 会重复工作；`ParseXmlDeclaration` 持续读取并解析声明。本次没有复制第三方实现或添加依赖。[XNode.cs](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Private.Xml.Linq/src/System/Xml/Linq/XNode.cs)、[XmlTextReaderImpl.cs](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Private.Xml/src/System/Xml/Core/XmlTextReaderImpl.cs)

检索环境：搜索工具连接失败后改为直接读取 Microsoft 官方页面；GitHub 插件本次未提供可调用的检索接口，按技能的备用流程使用 `gh api` 核对公共仓库源码。

## 3. 测试与性能证据

Debug / Release 完整解决方案构建均为 **0 警告、0 错误**；全量测试各 **1247 通过、0 失败、0 跳过**，相较修复前 1227 项新增 20 项。

新增回归覆盖：空载荷位于集合首部/中部/尾部、全部为空、命名空间与同名兄弟位置、再次识别时重建位置、缓存命中后的取消、8 万条记录性能上界、XML 已读完后的取消、跨 1024/8192 字节的编码声明、GUI/read/scan/refactor 编码一致、破损声明/未知编码/字符预算，以及四类 CLI 预取消和重构子命令取消后的文件不变性。

原有全量测试同时重新验证了真实 Windows Minidump 的格式校验、异常侧车、DUMP 提供者失败处理、同一异常去重，以及 Debug 的 DEBUG/WARN/ERROR/CRITICAL 和 Release 的 ERROR/CRITICAL 日志策略。正常取消、格式错误和预算超限测试断言不调用未知异常转储器。

| 同级记录数 | 修复前 Debug 探针（ms，单次） | 修复后 Debug（ms，3 次中位数） | 修复后 Release（ms，3 次中位数） |
| --- | ---: | ---: | ---: |
| 20000 | 932 | 32.959 | 25.984 |
| 40000 | 2985 | 88.121 | 48.197 |
| 80000 | 12057 | 132.978 | 109.397 |

上述测量只包含内存文档的识别与位置构造，XML 建树在计时外。修复前数值来自本次审查探针；修复后原始数据和复现脚本随文档保存。数据受 JIT、GC 和主机负载影响，不作为生产吞吐承诺。1 ms 后请求取消的探针，Debug/Release 分别在约 31/22 ms 内观察到取消，其中包含 PowerShell 调用及异常传播开销。

- [验证清单和源码哈希](implementation/IMP-09-hardening/verification.json)
- [Debug 构建](implementation/IMP-09-hardening/build-debug.log)、[Release 构建](implementation/IMP-09-hardening/build-release.log)
- [Debug TRX](implementation/IMP-09-hardening/debug.trx)、[Release TRX](implementation/IMP-09-hardening/release.trx)
- [测量脚本](implementation/IMP-09-hardening/Measure-ReadLocations.ps1)、[Debug 测量](implementation/IMP-09-hardening/locations-debug.json)、[Release 测量](implementation/IMP-09-hardening/locations-release.json)
- [真实 CLI Partial 输出](implementation/IMP-09-hardening/cli-partial.json)、[退出码与源序号断言](implementation/IMP-09-hardening/cli-partial-exit.json)

## 4. 复现与边界

原默认中间目录的 WPF `.g.resources` 遇到文件占用，删除操作被自动审批策略拒绝。本次改用独立中间目录，未修改项目构建配置、降低测试要求或结束用户进程。

```powershell
dotnet build SqlXmlAnalyzer.sln -c Debug --no-restore -p:IntermediateOutputPath=obj/imp09-hardening-final/Debug/
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Debug --no-build --no-restore
dotnet build SqlXmlAnalyzer.sln -c Release --no-restore -p:IntermediateOutputPath=obj/imp09-hardening-final/Release/
dotnet test SqlXmlAnalyzer.Tests/SqlXmlAnalyzer.Tests.csproj -c Release --no-build --no-restore
pwsh -File DOCS/implementation/IMP-09-hardening/Measure-ReadLocations.ps1 -Configuration Release
```

CLI 取消回归通过令牌注入覆盖命令入口及重构子命令，未模拟真实终端按键。取消仍在协作检查点生效，既有规则内部执行和操作系统正在执行的同步 I/O 不保证立即中断。XEL 正向测试仍使用 IXelEventSource 替身，生产二进制 XEL 正向兼容性验证沿用 IMP-09 已披露的缺口。本次未修改 WPF 视觉布局。
