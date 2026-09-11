using System.Collections.ObjectModel;
using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.Comparison;

public enum ComparisonConfidence { Unmatched, Candidate, High, Manual }
public sealed record EvidencePair<T>(T? A, T? B, ComparisonConfidence Confidence, string Reason) where T : class;
public sealed record ComparisonParameter(string Name, string? Type, string? CompiledValue, string? RuntimeValue);
public sealed record ComparisonCapture(string? SourceHash, string? EngineBuild, string? SchemaVersion,
    string? CompatibilityLevel, string? CardinalityEstimator, string? DegreeOfParallelism,
    IReadOnlyDictionary<string, string> SetOptions, bool ParameterListPresent, IReadOnlyList<ComparisonParameter> Parameters,
    bool HasRuntime)
{
    public string SourceHashKind { get; init; } = "原始文件字节";
    public string? XmlContentHash { get; init; }
    public string? RecordedOriginalSourceHash { get; init; }
    public string Summary => $"Build {EngineBuild ?? "N/A"}；CE {CardinalityEstimator ?? "N/A"}；兼容级别 {CompatibilityLevel ?? "N/A"}；DOP {DegreeOfParallelism ?? "N/A"}；"
        + $"{(HasRuntime ? "含运行记录" : "无运行记录")}；参数 {(ParameterListPresent ? Parameters.Count.ToString() : "N/A")}；{SourceHashKind} SHA-256 {SourceHash ?? "N/A"}"
        + (XmlContentHash != null && XmlContentHash != SourceHash ? $"；当前 XML SHA-256 {XmlContentHash}" : "")
        + (RecordedOriginalSourceHash != null && RecordedOriginalSourceHash != SourceHash
            ? $"；历史原始文件 SHA-256（与当前 XML 的关联未核验）{RecordedOriginalSourceHash}" : "");
    public string ParameterSummary => !ParameterListPresent ? "参数证据 N/A" : Parameters.Count == 0 ? "明确空参数列表"
        : string.Join("；", Parameters.Select(p => $"{p.Name} ({p.Type ?? "N/A"})：编译 {p.CompiledValue ?? "N/A"}，运行 {p.RuntimeValue ?? "N/A"}"));
}
public sealed record ComparisonQuery(PlanQueryPlan Plan, XElement Source, ComparisonCapture Capture,
    string Objects, string Structure, IReadOnlyDictionary<PlanOperatorKey, string> OperatorSignatures)
{
    public IReadOnlySet<PlanOperatorKey> IncompletePredicateOperators { get; init; } = FrozenSet<PlanOperatorKey>.Empty;
    public IReadOnlySet<PlanOperatorKey> IncompleteStructureOperators { get; init; } = FrozenSet<PlanOperatorKey>.Empty;
    public bool HasCompleteObjectIdentity => Plan.Operators.All(PlanComparisonObjectEvidence.IsComplete);
}
public sealed record ComparisonStatement(PlanStatement Statement, XElement Source, string? SqlSignature,
    string Objects, string Structure, IReadOnlyList<ComparisonQuery> Queries)
{
    public string Label => $"B{Statement.Key.Batch.BatchOrdinal}/S{Statement.Key.StatementOrdinal} — {Statement.Text ?? Statement.Kind}";
}
public sealed record ComparisonConditions(bool EstimatesComparable, bool RuntimeComparable, IReadOnlyList<string> Reasons)
{
    public string Summary => $"估算采集条件{(EstimatesComparable ? "满足" : "不足")}；运行采集条件{(RuntimeComparable ? "满足" : "不足")}；逐指标仍需核对匹配和执行口径。"
        + string.Join("；", Reasons);
}

/// <summary>Offline correspondence, never a proof of SQL equivalence or performance improvement.</summary>
public static class PlanComparisonEvidence
{
    public const string Version = "plan-comparison/1.2.0";
    private static readonly string[] RequiredOptions = ["ANSI_NULLS", "ANSI_PADDING", "ANSI_WARNINGS", "ARITHABORT",
        "CONCAT_NULL_YIELDS_NULL", "NUMERIC_ROUNDABORT", "QUOTED_IDENTIFIER"];

    public static IReadOnlyList<ComparisonStatement> Read(XDocument document, PlanDocument model,
        PlanQueryPlanKey? selection = null, CancellationToken cancellationToken = default, string? originalSourceHash = null,
        string? recordedOriginalSourceHash = null)
    {
        if (selection != null && !model.QueryPlans.Any(q => q.Key == selection))
            throw new InvalidDataException("PLAN_SELECTION_NOT_FOUND: 比较选择不属于当前源快照，请重新选择。");
        var result = new List<ComparisonStatement>();
        string xmlContentHash = Digest(document.Root?.ToString(SaveOptions.DisableFormatting) ?? "");
        string sourceHash = model.Envelope.SourceHash ?? originalSourceHash ?? xmlContentHash;
        string sourceHashKind = model.Envelope.SourceHash != null ? "原始文件字节" : originalSourceHash != null
            ? "原始文件字节（会话记录，未重新读取原文件验证）" : "XML 表示";
        foreach (var statement in model.Statements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (selection != null && selection.Statement != statement.Key) continue;
            var source = model.GetStatementSource(statement.Key)!;
            var queries = new List<ComparisonQuery>();
            foreach (var query in statement.QueryPlans.Where(q => selection == null || q.Key == selection))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var querySource = model.GetQueryPlanSource(query.Key)!;
                var (signatures, incompletePredicates, incompleteStructure) = Signatures(query, model, cancellationToken);
                var objects = ObjectSet(query.Operators);
                var structure = Join(query.Operators.Where(o => o.Parent == null).Select(o => signatures[o.Key]));
                queries.Add(new(query, querySource, Capture(model, source, querySource, query, sourceHash) with
                    { SourceHashKind = sourceHashKind, XmlContentHash = xmlContentHash, RecordedOriginalSourceHash = recordedOriginalSourceHash }, objects, structure, signatures)
                    { IncompletePredicateOperators = incompletePredicates, IncompleteStructureOperators = incompleteStructure });
            }
            result.Add(new(statement, source, SqlSignature(statement.Text), ObjectSet(queries.SelectMany(q => q.Plan.Operators)),
                Join(queries.Select(q => q.Structure)), queries.AsReadOnly()));
        }
        return result.AsReadOnly();
    }

    public static IReadOnlyList<EvidencePair<ComparisonStatement>> MatchStatements(IReadOnlyList<ComparisonStatement> a,
        IReadOnlyList<ComparisonStatement> b, bool manual, CancellationToken token = default)
    {
        if (manual && a.Count == 1 && b.Count == 1) return [new(a[0], b[0], ComparisonConfidence.Manual, "用户选择语句/QueryPlan；不证明语义等价")];
        return Match(a, b, token,
            (s => s.SqlSignature == null || s.Queries.Any(q => !q.HasCompleteObjectIdentity) ? null
                : s.Statement.Kind + ":" + s.SqlSignature + ":" + s.Objects, ComparisonConfidence.High, "唯一 SQL 词法与捕获对象一致"),
            (s => s.SqlSignature == null ? null : s.Statement.Kind + ":" + s.SqlSignature, ComparisonConfidence.Candidate, "SQL 词法一致，但对象证据不同或不完整"),
            (s => ValidHash(s.Statement.QueryHash) is { } hash ? s.Statement.Kind + ":" + hash + ":" + s.Objects : null,
                ComparisonConfidence.Candidate, "唯一 QueryHash 候选；字面量、参数与语义尚未确认"),
            (s => s.Objects.Length == 0 ? null : s.Statement.Kind + ":" + s.Objects + ":" + s.Structure,
                ComparisonConfidence.Candidate, "唯一对象/结构候选；SQL 等价性尚未确认"));
    }

    public static IReadOnlyList<EvidencePair<ComparisonQuery>> MatchQueries(EvidencePair<ComparisonStatement> statement, CancellationToken token = default)
    {
        var a = statement.A?.Queries ?? []; var b = statement.B?.Queries ?? [];
        if (a.Count == 1 && b.Count == 1) return [new(a[0], b[0], statement.Confidence, "已匹配语句内唯一 QueryPlan")];
        return Match(a, b, token, (q => q.Structure.Length == 0 || q.IncompletePredicateOperators.Count > 0 || q.IncompleteStructureOperators.Count > 0
            || !q.HasCompleteObjectIdentity ? null : q.Objects + ":" + q.Structure,
            ComparisonConfidence.High, "已匹配语句内唯一对象/结构 QueryPlan"),
            (q => q.Structure.Length == 0 ? null : q.Objects + ":" + q.Structure,
            ComparisonConfidence.Candidate, "QueryPlan 对象或算子结构证据不完整"));
    }

    public static IReadOnlyList<EvidencePair<PlanOperator>> MatchOperators(ComparisonQuery? a, ComparisonQuery? b, CancellationToken token = default)
    {
        string Key(PlanOperator op) => a?.OperatorSignatures.GetValueOrDefault(op.Key)
            ?? b!.OperatorSignatures[op.Key];
        bool Complete(PlanOperator op) => a?.IncompletePredicateOperators.Contains(op.Key) != true
            && b?.IncompletePredicateOperators.Contains(op.Key) != true
            && a?.IncompleteStructureOperators.Contains(op.Key) != true && b?.IncompleteStructureOperators.Contains(op.Key) != true;
        return Match(a?.Plan.Operators ?? [], b?.Plan.Operators ?? [], token,
            (op => string.IsNullOrEmpty(op.PhysicalOp) || !Complete(op) ? null : op.PhysicalOp + ":" + Key(op), ComparisonConfidence.High, "唯一类型/对象/谓词/排序/子树结构"),
            (op => string.IsNullOrEmpty(op.LogicalOp) ? null : Key(op), ComparisonConfidence.Candidate, "唯一逻辑结构；物理算子可能改变或对象/结构证据不完整"));
    }

    private static (IReadOnlyDictionary<PlanOperatorKey, string>, IReadOnlySet<PlanOperatorKey>, IReadOnlySet<PlanOperatorKey>)
        Signatures(PlanQueryPlan query, PlanDocument model, CancellationToken token)
    {
        var children = query.Operators.Where(o => o.Parent != null).ToLookup(o => o.Parent!);
        var result = new Dictionary<PlanOperatorKey, string>();
        var incomplete = new HashSet<PlanOperatorKey>();
        var incompleteStructure = new HashSet<PlanOperatorKey>();
        foreach (var op in query.Operators.Reverse())
        {
            token.ThrowIfCancellationRequested();
            var source = model.GetOperatorSource(op.Key)!;
            var predicates = PlanComparisonPredicateEvidence.Read(source, token);
            var sort = PlanComparisonSortEvidence.Read(source, token);
            if (!predicates.Complete || children[op.Key].Any(c => incomplete.Contains(c.Key))) incomplete.Add(op.Key);
            if (incomplete.Contains(op.Key) || !sort.Complete || !PlanComparisonObjectEvidence.IsComplete(op)
                || children[op.Key].Any(c => incompleteStructure.Contains(c.Key))) incompleteStructure.Add(op.Key);
            result.Add(op.Key, Digest(Join(new[] { Role(op), ObjectSet([op]), predicates.Signature, sort.Signature }) + Join(children[op.Key].Select(c => result[c.Key]))));
        }
        return (new ReadOnlyDictionary<PlanOperatorKey, string>(result), incomplete.ToFrozenSet(), incompleteStructure.ToFrozenSet());
    }

    private static string Role(PlanOperator op) => op.LogicalOp is "Table Scan" or "Index Scan" or "Index Seek" or "Clustered Index Scan" or "Clustered Index Seek"
        ? "Access" : op.LogicalOp ?? op.PhysicalOp ?? "Unknown";
    private static string ObjectSet(IEnumerable<PlanOperator> operators) => Join(operators.SelectMany(o => o.Objects)
        .Select(o => string.Join("|", new[] { o.Identity.Server, o.Identity.Database, o.Identity.Schema, o.Identity.Object, o.Alias }
            .Select(s => s == null ? "?" : s.Length + ":" + s))).Distinct(StringComparer.Ordinal));
    private static string Join(IEnumerable<string> parts) => string.Join("", parts.OrderBy(s => s, StringComparer.Ordinal).Select(s => s.Length + ":" + s));
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string? ValidHash(string? value) => value is { Length: 18 } && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        && value[2..].All(Uri.IsHexDigit) && value[2..].Any(c => c != '0') ? value.ToUpperInvariant() : null;

    private static string? SqlSignature(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return null;
        using var reader = new StringReader(sql);
        var tokens = new TSql160Parser(true).GetTokenStream(reader, out var errors);
        if (errors.Count > 0) return null;
        var values = tokens.Where(t => t.TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment
            or TSqlTokenType.MultilineComment or TSqlTokenType.EndOfFile)).Select(t => $"{(int)t.TokenType}:{t.Text.Length}:{t.Text}").ToArray();
        return values.Length == 0 ? null : Digest(string.Join("|", values));
    }

    private static ComparisonCapture Capture(PlanDocument model, XElement statement, XElement query, PlanQueryPlan plan, string sourceHash)
    {
        var ns = query.Name.Namespace;
        var options = statement.Elements(ns + "StatementSetOptions").ToArray();
        var settings = options.Length == 1 ? options[0].Attributes().Where(a => a.Name.Namespace == XNamespace.None)
            .ToDictionary(a => a.Name.LocalName, a => a.Value, StringComparer.Ordinal) : new Dictionary<string, string>();
        var lists = query.Elements(ns + "ParameterList").ToArray();
        bool validList = lists.Length == 1 && lists[0].Elements().All(e => e.Name == ns + "ColumnReference" && !e.HasElements);
        var parameters = validList ? lists[0].Elements(ns + "ColumnReference").Select(p => new ComparisonParameter(
            (string?)p.Attribute("Column") ?? "", (string?)p.Attribute("ParameterDataType"), (string?)p.Attribute("ParameterCompiledValue"),
            (string?)p.Attribute("ParameterRuntimeValue"))).OrderBy(p => p.Name, StringComparer.Ordinal).ToArray() : [];
        return new ComparisonCapture(sourceHash, model.Envelope.EngineBuild, model.Envelope.SchemaVersion,
            (string?)statement.Attribute("DatabaseCompatibilityLevel"), (string?)statement.Attribute("CardinalityEstimationModelVersion"),
            (string?)query.Attribute("DegreeOfParallelism"), new ReadOnlyDictionary<string, string>(settings), validList,
            Array.AsReadOnly(parameters), plan.Operators.Any(o => o.Facts?.Threads.Count > 0))
        { SourceHashKind = model.Envelope.SourceHash == null ? "XML 表示" : "原始文件字节" };
    }

    public static ComparisonConditions Assess(ComparisonQuery? a, ComparisonQuery? b, ComparisonConfidence confidence)
    {
        var reasons = new List<string>();
        if (a == null || b == null) return new(false, false, ["未匹配 QueryPlan"]);
        bool estimates = confidence is ComparisonConfidence.High or ComparisonConfidence.Manual;
        if (!estimates) reasons.Add("语句匹配尚待人工确认");
        if (!a.HasCompleteObjectIdentity || !b.HasCompleteObjectIdentity) { estimates = false; reasons.Add("捕获对象身份不完整"); }
        if (a.Objects != b.Objects) { estimates = false; reasons.Add("捕获对象不同"); }
        var x = a.Capture; var y = b.Capture;
        void Required(string name, string? first, string? second)
        {
            if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second) || first != second)
            { estimates = false; reasons.Add(name + (first == null || second == null ? "缺失" : "不同")); }
        }
        Required("引擎版本", VersionText(x.EngineBuild), VersionText(y.EngineBuild));
        Required("CE 版本", PositiveInteger(x.CardinalityEstimator), PositiveInteger(y.CardinalityEstimator));
        if (x.CompatibilityLevel != null || y.CompatibilityLevel != null) Required("兼容级别", PositiveInteger(x.CompatibilityLevel), PositiveInteger(y.CompatibilityLevel));
        else reasons.Add("兼容级别未记录，不由 CE 版本推断");
        foreach (string option in RequiredOptions) Required(option, BooleanText(x.SetOptions.GetValueOrDefault(option)), BooleanText(y.SetOptions.GetValueOrDefault(option)));
        bool parameters = x.ParameterListPresent && y.ParameterListPresent && x.Parameters.Count == y.Parameters.Count
            && x.Parameters.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == x.Parameters.Count
            && y.Parameters.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == y.Parameters.Count;
        bool runtimeParameters = parameters;
        if (parameters)
            for (int i = 0; i < x.Parameters.Count; i++)
            {
                var p = x.Parameters[i]; var q = y.Parameters[i];
                parameters &= p.Name.Length > 0 && p.Name == q.Name && p.Type != null && p.Type == q.Type
                    && p.CompiledValue != null && p.CompiledValue == q.CompiledValue;
                runtimeParameters &= p.Name == q.Name && p.RuntimeValue != null && p.RuntimeValue == q.RuntimeValue;
            }
        if (!parameters) { estimates = false; reasons.Add("编译参数证据缺失或不同"); }
        bool runtime = estimates && x.HasRuntime && y.HasRuntime && runtimeParameters;
        if (!x.HasRuntime || !y.HasRuntime) reasons.Add("Actual/Estimated 或缺少运行记录");
        if (!runtimeParameters) reasons.Add("运行参数证据缺失或不同");
        if (PositiveInteger(x.DegreeOfParallelism) is not { } dop || dop != PositiveInteger(y.DegreeOfParallelism))
        { runtime = false; reasons.Add("运行 DOP 缺失或不同"); }
        reasons.Add("仅比较捕获条件；硬件、缓存、统计信息时点及负载未验证，非性能改善证明");
        return new(estimates, runtime, reasons.AsReadOnly());
    }

    private static string? BooleanText(string? text) => text?.Trim() switch { "true" or "1" => "true", "false" or "0" => "false", _ => null };
    private static string? PositiveInteger(string? text) => int.TryParse(text, System.Globalization.NumberStyles.None,
        System.Globalization.CultureInfo.InvariantCulture, out int value) && value > 0 ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
    private static string? VersionText(string? text) => System.Version.TryParse(text, out var version) ? version.ToString() : null;

    private static IReadOnlyList<EvidencePair<T>> Match<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, CancellationToken token,
        params (Func<T, string?> Key, ComparisonConfidence Confidence, string Reason)[] stages) where T : class
    {
        var left = Enumerable.Range(0, a.Count).ToHashSet(); var right = Enumerable.Range(0, b.Count).ToHashSet();
        var pairs = new Dictionary<int, EvidencePair<T>>();
        foreach (var stage in stages)
        {
            token.ThrowIfCancellationRequested();
            Dictionary<string, int[]> Groups(HashSet<int> remaining, IReadOnlyList<T> values) => remaining
                .Select(i => (Index: i, Key: stage.Key(values[i]))).Where(v => !string.IsNullOrEmpty(v.Key))
                .GroupBy(v => v.Key!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(v => v.Index).ToArray(), StringComparer.Ordinal);
            var aa = Groups(left, a); var bb = Groups(right, b);
            foreach (var group in aa)
            {
                token.ThrowIfCancellationRequested();
                if (group.Value.Length != 1 || !bb.TryGetValue(group.Key, out var matches) || matches.Length != 1) continue;
                int ai = group.Value[0], bi = matches[0];
                pairs.Add(ai, new(a[ai], b[bi], stage.Confidence, stage.Reason)); left.Remove(ai); right.Remove(bi);
            }
        }
        return Enumerable.Range(0, a.Count).Select(i => pairs.GetValueOrDefault(i) ?? new(a[i], null, ComparisonConfidence.Unmatched,
            "A 未匹配：缺少唯一证据或存在重复候选；可人工选择"))
            .Concat(right.Order().Select(i => new EvidencePair<T>(null, b[i], ComparisonConfidence.Unmatched,
                "B 未匹配：缺少唯一证据或存在重复候选；可人工选择"))).ToArray();
    }
}
