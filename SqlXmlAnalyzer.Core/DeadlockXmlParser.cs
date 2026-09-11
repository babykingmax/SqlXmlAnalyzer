using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Threading;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer
{
    public static class DeadlockXmlParser
    {
        public static DeadlockParseResult<ParsedDeadlockGraphData> TryParseDeadlockXml(XDocument? doc,
            CancellationToken cancellationToken = default, IUnexpectedErrorReporter? unexpectedErrors = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (doc?.Root == null)
            {
                return DeadlockParseResult<ParsedDeadlockGraphData>.Failure("死锁 XML 缺少根节点。");
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Logger.Debug("IMP-13: 解析死锁进程、资源和原始关系。");
                InputRecognitionResult input = InputRecognitionService.Recognize(doc, cancellationToken);
                if (!input.IsSuccess || input.Kind != AnalysisDocumentKind.DeadlockXml)
                {
                    return DeadlockParseResult<ParsedDeadlockGraphData>.Failure(input.ErrorMessage ?? "未找到 deadlock 节点。");
                }
                if (input.Deadlocks.Count != 1)
                    return DeadlockParseResult<ParsedDeadlockGraphData>.Failure(
                        "INPUT_EVENT_SELECTION_REQUIRED: 包含多个死锁事件，请从输入识别结果中选择一个事件后再分析。");
                XElement deadlockNode = input.Deadlocks[0].Document.Root!;

                XElement? processList = deadlockNode.Element("process-list");
                if (processList == null)
                {
                    return DeadlockParseResult<ParsedDeadlockGraphData>.Failure("死锁 XML 缺少必需的 process-list 节点。");
                }

                XElement? resourceList = deadlockNode.Element("resource-list");
                if (resourceList == null)
                {
                    return DeadlockParseResult<ParsedDeadlockGraphData>.Failure("死锁 XML 缺少必需的 resource-list 节点。");
                }

                var warnings = new List<string>();
                var locator = new DocumentSource(cancellationToken);
                var processes = ParseProcesses(processList, warnings, locator, cancellationToken);
                if (processes.Count == 0)
                {
                    return DeadlockParseResult<ParsedDeadlockGraphData>.Failure("process-list 中没有包含有效 id 的 process。");
                }

                var resources = ParseResources(resourceList, warnings, locator, cancellationToken);
                var processIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var process in processes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!processIds.Add(process.Id))
                        return DeadlockParseResult<ParsedDeadlockGraphData>.Failure("DEADLOCK_PROCESS_ID_INVALID: 进程编号重复。");
                }
                if (resources.Count == 0)
                {
                    return DeadlockParseResult<ParsedDeadlockGraphData>.Failure("resource-list 中没有有效的锁资源。");
                }

                var victims = new HashSet<string>(StringComparer.Ordinal);
                string victimId = string.Empty;
                foreach (var element in deadlockNode.Element("victim-list")?.Elements("victimProcess") ?? Enumerable.Empty<XElement>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string id = (string?)element.Attribute("id") ?? string.Empty;
                    if (id.Length == 0 || !victims.Add(id)) continue;
                    if (victimId.Length == 0 || StringComparer.Ordinal.Compare(id, victimId) < 0) victimId = id;
                    if (!processIds.Contains(id)) warnings.Add($"victimProcess id '{id}' 未出现在 process-list 中。");
                }

                if (victimId.Length == 0)
                {
                    warnings.Add("死锁 XML 未提供 victimProcess id。");
                }
                foreach (LockResource resource in resources)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (string processId in resource.Owners.Select(owner => owner.Id)
                                 .Concat(resource.Waiters.Select(waiter => waiter.Id))
                                 .Where(id => id.Length > 0 && !processIds.Contains(id))
                                 .Distinct(StringComparer.Ordinal))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        warnings.Add($"资源 {resource.Id} 引用了未知进程 '{processId}'。");
                    }
                }

                return DeadlockParseResult<ParsedDeadlockGraphData>.Success(
                    new ParsedDeadlockGraphData(processes, resources, victimId)
                    { VictimIds = victims.ToFrozenSet(StringComparer.Ordinal), SourceFingerprint = DeadlockIdentity.Hash(deadlockNode.ToString(SaveOptions.DisableFormatting)) },
                    warnings);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                return DeadlockParseResult<ParsedDeadlockGraphData>.Failure(
                    $"死锁 XML 解析失败: {ExceptionPolicy.Describe(ex, "IMP13.DeadlockXmlParser", unexpectedErrors)}");
            }
        }

        [Obsolete("Use TryParseDeadlockXml and inspect IsSuccess, Errors, and Warnings.")]
        public static (List<DeadlockProcess> processes, List<LockResource> resources, string victimId)
            ParseDeadlockXml(XDocument doc)
        {
            var result = TryParseDeadlockXml(doc);
            if (!result.IsSuccess || result.Value == null)
            {
                return (new List<DeadlockProcess>(), new List<LockResource>(), string.Empty);
            }

            return (
                result.Value.Processes.ToList(),
                result.Value.Resources.ToList(),
                result.Value.VictimId);
        }

        private static List<DeadlockProcess> ParseProcesses(
            XElement processList,
            List<string> warnings, DocumentSource locator, CancellationToken cancellationToken)
        {
            var processes = new List<DeadlockProcess>();

            foreach (XElement process in processList.Elements("process"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string id = process.Attribute("id")?.Value ?? string.Empty;
                if (id.Length == 0)
                {
                    warnings.Add("已忽略缺少 id 的 process 节点。");
                    continue;
                }

                string spid = process.Attribute("spid")?.Value ?? string.Empty;
                if (spid.Length == 0)
                {
                    warnings.Add($"进程 {id} 缺少 spid。");
                }

                var frames = process.Element("executionStack")?.Elements("frame")
                    .Select(frame =>
                    {
                        string statement = (frame.Value ?? string.Empty).Trim();
                        if (statement.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                        {
                            statement = "[隐藏代码]";
                        }

                        return new ExecutionFrame(
                            frame.Attribute("procname")?.Value ?? string.Empty,
                            frame.Attribute("line")?.Value ?? string.Empty,
                            statement) { RawFields = Fields(frame), RawXml = frame.ToString(SaveOptions.DisableFormatting), Source = locator.Location(frame) };
                    })
                    .ToList() ?? new List<ExecutionFrame>();

                processes.Add(new DeadlockProcess(
                    id,
                    spid,
                    process.Attribute("loginname")?.Value ?? string.Empty,
                    process.Attribute("hostname")?.Value ?? string.Empty,
                    process.Attribute("isolationlevel")?.Value ?? string.Empty,
                    process.Attribute("status")?.Value ?? string.Empty,
                    (process.Element("inputbuf")?.Value ?? string.Empty).Trim(),
                    frames,
                    process.Attribute("transactionname")?.Value ?? string.Empty,
                    process.Attribute("currentdbname")?.Value ?? string.Empty,
                    process.Attribute("clientapp")?.Value ?? string.Empty,
                    process.Attribute("waitresource")?.Value ?? string.Empty,
                    process.Attribute("waittime")?.Value ?? string.Empty,
                    process.Attribute("ecid")?.Value ?? string.Empty,
                    ReadDeadlockPriority(process, warnings),
                    process.Attribute("logused")?.Value ?? "0") { RawFields = Fields(process), RawXml = process.ToString(SaveOptions.DisableFormatting), Source = locator.Location(process) });
            }

            return processes;
        }

        private static string ReadDeadlockPriority(XElement process, List<string> warnings)
        {
            // Standard field wins even when invalid; a conflicting alias must not hide bad evidence.
            XAttribute? attribute = process.Attribute("priority")
                ?? process.Attribute("currentdeadlockpriority")
                ?? process.Attribute("deadlockpriority");
            if (attribute == null) return string.Empty;
            if (int.TryParse(attribute.Value.Trim(' ', '\t', '\r', '\n'), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out int priority))
            {
                Logger.Debug($"IMP-07: 从 {attribute.Name.LocalName} 读取死锁优先级。");
                return priority.ToString(CultureInfo.InvariantCulture);
            }
            string warning = $"IMP-07: {attribute.Name.LocalName} 格式非法，死锁优先级保持未知。";
            warnings.Add(warning);
            Logger.Warning(warning);
            return string.Empty;
        }

        private static List<LockResource> ParseResources(
            XElement resourceList,
            List<string> warnings, DocumentSource locator, CancellationToken cancellationToken)
        {
            var resources = new List<LockResource>();
            int resourceIndex = 0;
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (XElement resource in resourceList.Elements())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string resourceId = $"res_{resourceIndex++}";
                var owners = resource.Element("owner-list")?.Elements("owner")
                    .Select(owner => new LockOwner(
                        owner.Attribute("id")?.Value ?? string.Empty,
                        owner.Attribute("mode")?.Value ?? string.Empty) { RawFields = Fields(owner), Source = locator.Location(owner) })
                    .ToList() ?? new List<LockOwner>();
                var waiters = resource.Element("waiter-list")?.Elements("waiter")
                    .Select(waiter => new LockWaiter(
                        waiter.Attribute("id")?.Value ?? string.Empty,
                        waiter.Attribute("mode")?.Value ?? string.Empty,
                        waiter.Attribute("requestType")?.Value ?? string.Empty) { RawFields = Fields(waiter), Source = locator.Location(waiter) })
                    .ToList() ?? new List<LockWaiter>();

                if (owners.Count == 0)
                {
                    warnings.Add($"资源 {resourceId} 缺少 owner。");
                }
                if (waiters.Count == 0)
                {
                    warnings.Add($"资源 {resourceId} 缺少 waiter。");
                }

                string identity = DeadlockIdentity.Hash(new { Type = resource.Name.ToString(), Fields = Fields(resource),
                    Owners = owners.Select(o => System.Text.Json.JsonSerializer.Serialize(o.RawFields)).Order(StringComparer.Ordinal).ToArray(),
                    Waiters = waiters.Select(w => System.Text.Json.JsonSerializer.Serialize(w.RawFields)).Order(StringComparer.Ordinal).ToArray(),
                    Other = resource.Elements().Where(e => e.Name.LocalName is not "owner-list" and not "waiter-list")
                        .Select(e => e.ToString(SaveOptions.DisableFormatting)).Order(StringComparer.Ordinal).ToArray() });
                int occurrence = occurrences.GetValueOrDefault(identity);
                occurrences[identity] = occurrence + 1;
                resources.Add(new LockResource(
                    resource.Name.LocalName,
                    resource.Attribute("objectname")?.Value ?? "(未知)",
                    resource.Attribute("indexname")?.Value ?? string.Empty,
                    resource.Attribute("hobtid")?.Value ?? string.Empty,
                    resource.Attribute("dbid")?.Value ?? string.Empty,
                    owners,
                    waiters,
                    resourceId)
                {
                    SourceId = (string?)resource.Attribute("id") ?? "", ResourceKey = identity + ":" + occurrence,
                    RawFields = Fields(resource), RawXml = resource.ToString(SaveOptions.DisableFormatting), Source = locator.Location(resource)
                });
            }

            return resources;
        }

        private static IReadOnlyDictionary<string, string> Fields(XElement element) =>
            new ReadOnlyDictionary<string, string>(element.Attributes().Where(a => !a.IsNamespaceDeclaration)
                .OrderBy(a => a.Name.ToString(), StringComparer.Ordinal).ToDictionary(a => a.Name.ToString(), a => a.Value, StringComparer.Ordinal));
    }
}
