using System.Globalization;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Rules;

namespace SqlXmlAnalyzer;

public static partial class DeadlockPatternAnalyzer
{
    /// <summary>Diagnose the selected event's shared facts; originalDoc is retained only for API compatibility.</summary>
    public static List<DeadlockPattern> IdentifyPatterns(DeadlockGraph? graph, XDocument? originalDoc = null)
    {
        var result = new List<DeadlockPattern>();
        if (graph == null) return result;
        IEnumerable<DeadlockEvidence> ResourceEvidence(IEnumerable<LockResource> resources) => resources.Select(r =>
            new DeadlockEvidence("resource", $"{r.LockType} {r.SourceId}; object={r.ObjectName}; index={r.IndexName}", ResourceId: r.Id, Source: r.Source));
        IEnumerable<DeadlockEvidence> TextHints(Func<string, bool> predicate) => graph.Processes.Where(p => predicate(p.Inputbuf.ToLowerInvariant()))
            .Select(p => new DeadlockEvidence("sql-text-hint", "仅文本线索；原文保留在进程 RawXml/inputbuf 中", p.Id, Source: p.Source));
        void Add(string id, string name, string description, string hypothesis, string recommendation,
            DiagnosticConfidence confidence = DiagnosticConfidence.Low, IEnumerable<DeadlockEvidence>? evidence = null)
        {
            var facts = (evidence ?? EvidenceFor(graph)).Take(65).ToArray();
            bool truncated = facts.Length > 64;
            result.Add(new DeadlockPattern(name, "Medium", description, hypothesis, recommendation)
            {
                RuleId = "RULE_DEADLOCK_" + id, Confidence = confidence, SourceFingerprint = graph.SourceFingerprint,
                Evidence = Array.AsReadOnly(facts.Take(64).ToArray()), EvidenceTruncated = truncated,
                Limitations = Array.AsReadOnly(new[] { "死锁 XML 是捕获时的依赖快照，不能还原真实发生顺序或单独证明根因。" }
                    .Concat(truncated ? new[] { "诊断证据展示已截断为 64 项；完整事实保留在共享图中。" } : Array.Empty<string>()).ToArray())
            });
        }

        bool parallel = graph.Processes.Any(p => int.TryParse(p.Ecid, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ecid) && ecid > 0)
            || graph.Processes.Where(p => !string.IsNullOrWhiteSpace(p.Spid)).GroupBy(p => p.Spid).Any(g => g.Count() > 1)
            || graph.Resources.Any(r => r.LockType.Equals("exchangeEvent", StringComparison.OrdinalIgnoreCase)
                || r.LockType.Equals("SyncPoint", StringComparison.OrdinalIgnoreCase));
        if (parallel)
            Add("PARALLEL", "Parallel Intra-Query Deadlock（并行依赖线索）", "观测到并行执行上下文、共享 SPID 或 Exchange/SyncPoint 资源。",
                "可能涉及并行任务协调；不能据此断言扫描、索引或并行度是根因。", "结合执行计划、线程等待和复现验证并行依赖；仅在测量后评估并行度调整。",
                evidence: graph.Resources.Where(r => r.LockType.Equals("exchangeEvent", StringComparison.OrdinalIgnoreCase) || r.LockType.Equals("SyncPoint", StringComparison.OrdinalIgnoreCase))
                    .Select(r => new DeadlockEvidence("resource", r.LockType + " " + r.SourceId, ResourceId: r.Id, Source: r.Source))
                    .Concat(graph.Processes.Select(p => new DeadlockEvidence("execution-context", $"SPID={p.Spid}; ECID={p.Ecid}", p.Id, Source: p.Source))));
        if (HasConversionDeadlock(graph))
            Add("CONVERSION", "Lock Conversion（锁转换候选）", "观测到不同进程之间的 S/U 与 X/U 请求、持有模式组合。",
                "可能存在锁转换竞争；只有同一事务的持有与转换请求等证据才能确认转换过程。", "核对 requestType、事务访问顺序与转换请求，结合复现评估事务边界。",
                evidence: graph.Edges.Where(e => (e.HeldMode.Equals("S", StringComparison.OrdinalIgnoreCase) && e.RequestedMode.Equals("X", StringComparison.OrdinalIgnoreCase))
                    || (e.HeldMode.Equals("U", StringComparison.OrdinalIgnoreCase) && (e.RequestedMode.Equals("X", StringComparison.OrdinalIgnoreCase) || e.RequestedMode.Equals("U", StringComparison.OrdinalIgnoreCase))))
                    .Select(e => new DeadlockEvidence("lock-modes", $"{e.RequestedMode} ← {e.HeldMode}", e.FromProcessId, e.Resource?.Id, e.EdgeId, e.Resource?.Source)));
        if (HasBookmarkLookupPattern(graph))
            Add("LOOKUP", "Key Lookup（回表候选）", "观测到同一对象的多索引资源或 SQL 文本中的读写线索。",
                "可能涉及多访问路径交叉；资源名称和 SQL 关键词不能证明执行了 Key Lookup。", "检查实际执行计划和访问顺序，再评估索引覆盖及写入成本。",
                evidence: ResourceEvidence(graph.Resources.Where(r => !string.IsNullOrEmpty(r.ObjectName)))
                    .Concat(TextHints(sql => (sql.Contains("update") || sql.Contains("delete")) && (sql.Contains("select") || sql.Contains("lookup") || sql.Contains("bookmark")))));
        var rangeEvidence = RangeLockEvidence(graph);
        string[] rangeModes = rangeEvidence.Select(e => e.Value).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        if (rangeModes.Length > 0)
            Add("RANGE", "Range Lock（范围锁证据）", $"观测到实际范围锁模式：{string.Join(", ", rangeModes)}。",
                "捕获的 mode 字段包含范围锁；缺失进程引用不构成已确认的等待依赖，不能仅凭模式确定谓词、索引缺陷或根因。", "核对带证据的资源、谓词、索引与事务隔离要求，再通过复现验证改动。",
                DiagnosticConfidence.High, rangeEvidence);
        if (HasPageSplitPattern(graph))
            Add("PAGE", "Page/RID Lock Contention（页面或行资源线索）", "观测到 PAGE/RID 资源及写入文本线索。",
                "可能涉及写入竞争；页面锁不能证明发生页拆分。", "结合页拆分计数、索引布局和访问顺序验证，避免仅据死锁图调整填充因子。",
                evidence: ResourceEvidence(graph.Resources.Where(r => r.LockType.Contains("page") || r.LockType.Contains("rid")))
                    .Concat(TextHints(sql => sql.Contains("insert") || sql.Contains("update") || sql.Contains("delete"))));
        if (HasForeignKeyPattern(graph))
            Add("CASCADE", "Cascade Deadlock（约束或级联候选）", "SQL 文本中出现约束、外键或级联关键词。",
                "可能涉及约束检查或级联操作；关键词不能证明该操作参与当前环。", "核对约束元数据、触发器和实际执行计划。",
                evidence: TextHints(sql => sql.Contains("foreign key") || sql.Contains("fk_") || sql.Contains("constraint") || sql.Contains("cascade")));
        if (HasHighContentionOnSingleResource(graph))
            Add("HOTSPOT", "Hotspot Resource Contention（多等待者）", "观测到同一资源的多个 waiter。",
                "可能存在集中竞争；不能由单次快照推断持续热点或吞吐瓶颈。", "结合重复事件、等待持续时间与工作负载测量验证。",
                evidence: graph.Resources.Where(r => r.Waiters.Count >= 2).Select(r => new DeadlockEvidence("waiter-count", r.Waiters.Count.ToString(CultureInfo.InvariantCulture), ResourceId: r.Id, Source: r.Source)));
        if (graph.Processes.Any(p => !string.IsNullOrEmpty(p.DeadlockPriority)))
            Add("PRIORITY", "Deadlock Priority Analysis", "捕获数据包含死锁优先级字段；受害者以 victim-list 为准。",
                "优先级和回滚成本可能影响受害者选择；不能仅由单个字段重建选择过程。", "核对所有参与事务的有效优先级及 logused，不要把优先级调整当成消除死锁。",
                evidence: graph.Processes.Where(p => !string.IsNullOrEmpty(p.DeadlockPriority))
                    .Select(p => new DeadlockEvidence("priority/logused", $"{p.DeadlockPriority}/{p.LogUsed}", p.Id, Source: p.Source)));
        if (graph.Edges.Count > 0)
            Add("CHAIN", "Lock Chain Details", $"共享图包含 {graph.Edges.Count} 条 waiter→owner 依赖。{graph.CycleAnalysis.Summary}",
                "依赖边表达捕获的等待关系；解释路径不代表真实时间顺序。", "按资源身份和原始 owner/waiter 字段核对每条依赖。", DiagnosticConfidence.High,
                graph.Edges.Select(e => new DeadlockEvidence("wait-for", $"{e.FromProcessId}→{e.ToProcessId} 请求={e.RequestedMode} 持有={e.HeldMode}; waiter={e.WaiterLinkId}; owner={e.OwnerLinkId}",
                    e.FromProcessId, e.Resource?.Id, e.EdgeId, e.Resource?.Source)));
        if (result.Count == 0)
            Add("CYCLE", graph.CycleAnalysis.ProcessIds.Count > 0 ? "Cyclic Deadlock" : "等待关系证据不足",
                graph.CycleAnalysis.Summary, "不能从缺失的依赖推断根因或证明系统没有死锁。", "检查捕获是否完整，并结合上下文复现验证。", DiagnosticConfidence.High);
        Logger.Debug($"DeadlockPatternAnalyzer completed: diagnostics={result.Count}");
        return result;
    }

    private static string? NormalizeRangeMode(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "RANGES-S" => "RangeS-S", "RANGES-U" => "RangeS-U", "RANGEI-N" => "RangeI-N",
        "RANGEX-X" => "RangeX-X", "RANGEI-S" => "RangeI-S", "RANGEI-U" => "RangeI-U",
        "RANGEI-X" => "RangeI-X", "RANGEX-S" => "RangeX-S", "RANGEX-U" => "RangeX-U",
        _ => null
    };

    private static IEnumerable<DeadlockEvidence> RangeLockEvidence(DeadlockGraph graph)
    {
        // Read typed mode fields, including raw relations rejected as graph links. Names and
        // activities are not lock modes. A raw observation deliberately has no dependency EdgeId.
        foreach (var resource in graph.Resources)
        {
            if (resource.RawFields.TryGetValue("mode", out string? value) && NormalizeRangeMode(value) is string mode)
                yield return new("mode", mode, ResourceId: resource.Id, Source: resource.Source);
            foreach (var owner in resource.Owners)
                if (NormalizeRangeMode(owner.Mode) is string ownerMode)
                    yield return new("owner.mode", ownerMode, owner.Id, resource.Id, Source: owner.Source);
            foreach (var waiter in resource.Waiters)
                if (NormalizeRangeMode(waiter.Mode) is string waiterMode)
                    yield return new("waiter.mode", waiterMode, waiter.Id, resource.Id, Source: waiter.Source);
        }
        // Legacy callers may supply only wait-for edges, without parsed resources/links.
        if (graph.ResourceLinks.Count == 0)
            foreach (var edge in graph.Edges)
            {
                if (NormalizeRangeMode(edge.RequestedMode) is string requested)
                    yield return new("requested-mode", requested, edge.FromProcessId, edge.Resource?.Id, edge.EdgeId, edge.Resource?.Source);
                if (NormalizeRangeMode(edge.HeldMode) is string held)
                    yield return new("held-mode", held, edge.ToProcessId, edge.Resource?.Id, edge.EdgeId, edge.Resource?.Source);
            }
    }

    private static IEnumerable<DeadlockEvidence> EvidenceFor(DeadlockGraph graph)
    {
        foreach (var link in graph.ResourceLinks)
            yield return new(link.IsWaiter ? "waiter" : "owner", $"{link.Mode} {link.RequestType}".Trim(), link.ProcessId, link.ResourceId, link.LinkId, link.Source);
        // Compatibility for callers constructing a graph directly without parsed resource links.
        if (graph.ResourceLinks.Count == 0)
            foreach (var edge in graph.Edges)
                yield return new("wait-for", $"{edge.RequestedMode} {edge.HeldMode}", edge.FromProcessId, edge.Resource?.Id, edge.EdgeId, edge.Resource?.Source);
        foreach (var resource in graph.Resources)
        {
            yield return new("resource", resource.LockType + " " + resource.SourceId, ResourceId: resource.Id, Source: resource.Source);
            foreach (var field in resource.RawFields)
                yield return new(field.Key, field.Value, ResourceId: resource.Id, Source: resource.Source);
            if (graph.ResourceLinks.Count == 0)
            {
                foreach (var owner in resource.Owners) yield return new("owner", owner.Mode, owner.Id, resource.Id, Source: owner.Source);
                foreach (var waiter in resource.Waiters) yield return new("waiter", waiter.Mode, waiter.Id, resource.Id, Source: waiter.Source);
            }
        }
        foreach (var process in graph.Processes)
            yield return new("process", $"SPID={process.Spid}; ECID={process.Ecid}; priority={process.DeadlockPriority}", process.Id, Source: process.Source);
    }
}

public static class DeadlockDiagnosticFormatter
{
    public static string FormatEvidence(DeadlockPattern pattern) => string.Join(Environment.NewLine,
        new[] { $"规则：{pattern.RuleId}@{pattern.RuleVersion}；置信度：{pattern.Confidence}；来源指纹：{pattern.SourceFingerprint}" }
            .Concat(pattern.Evidence.Select(e => $"{e.Name}={e.Value} [process={e.ProcessId}; resource={e.ResourceId}; edge={e.EdgeId}; source={e.Source?.XmlPath}]"))
            .Concat(pattern.Limitations));
}
