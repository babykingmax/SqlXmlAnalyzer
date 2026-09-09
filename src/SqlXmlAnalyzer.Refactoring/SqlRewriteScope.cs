using System.Collections.Immutable;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Refactoring;

public sealed record SqlTableSymbol(int BatchOrdinal, string Name, int Offset, bool IsTopLevel);
public sealed record TemporaryObjectReservation(int BatchOrdinal, string VariableName, string TemporaryName, string OwnerSourceHash);

/// <summary>Conservative offline scope inventory. Reservations do not authorize CREATE or DROP.</summary>
public sealed record SqlRewriteScope(
    ImmutableArray<SqlTableSymbol> TableVariables,
    ImmutableArray<string> TemporaryNames,
    ImmutableArray<TemporaryObjectReservation> Reservations,
    ImmutableArray<string> Limitations)
{
    public static SqlRewriteScope Analyze(string sourceSql)
    {
        var variables = new List<SqlTableSymbol>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var limitations = new List<string>();
        // Reparse the exact source: engine clones may have regenerated formatting and different offsets.
        using var reader = new StringReader(sourceSql);
        var script = new TSql160Parser(true).Parse(reader, out var errors) as TSqlScript;
        if (script == null || errors.Count != 0)
            return new([], [], [], ["UnsupportedScope: 仅支持语法有效且具有明确批次边界的源脚本。"]);
        int batch = 0;
        foreach (var item in script.Batches)
        {
            var visitor = new Collector(batch++, item.Statements.OfType<DeclareTableVariableStatement>().ToHashSet());
            item.Accept(visitor);
            variables.AddRange(visitor.Symbols);
            names.UnionWith(visitor.Names);
            if (visitor.HasExecute) limitations.Add("DynamicOrExternalScope: EXEC 的作用域及调用方临时对象无法离线确定。");
        }
        if (variables.Any(v => !v.IsTopLevel))
            limitations.Add("UnsupportedScope: 嵌套声明、过程、函数和控制流中的对象生命周期无法可靠证明，跳过转换。");
        if (variables.GroupBy(v => (v.BatchOrdinal, v.Name.ToUpperInvariant())).Any(g => g.Count() > 1))
            limitations.Add("DuplicateDeclaration: 同一批次存在重复表变量声明，跳过转换。");

        string hash = SqlTextHash.Compute(sourceSql);
        var reserved = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var allocations = new List<TemporaryObjectReservation>();
        if (limitations.Count == 0)
        {
            int index = 0;
            foreach (var symbol in variables)
            {
                string name;
                do { name = $"#sxa_{hash[..12]}_{++index}"; } while (!reserved.Add(name));
                allocations.Add(new(symbol.BatchOrdinal, symbol.Name, name, hash));
            }
        }
        return new(variables.ToImmutableArray(), names.Order(StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
            allocations.ToImmutableArray(), limitations.Distinct().ToImmutableArray());
    }

    private sealed class Collector(int batch, HashSet<DeclareTableVariableStatement> topLevel) : TSqlFragmentVisitor
    {
        public List<SqlTableSymbol> Symbols { get; } = new();
        public HashSet<string> Names { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool HasExecute { get; private set; }
        public override void ExplicitVisit(DeclareTableVariableStatement node)
        {
            Symbols.Add(new(batch, node.Body.VariableName.Value, node.StartOffset, topLevel.Contains(node)));
            base.ExplicitVisit(node);
        }
        public override void ExplicitVisit(SchemaObjectName node)
        {
            if (node.BaseIdentifier?.Value is string name && name.StartsWith('#')) Names.Add(name);
            base.ExplicitVisit(node);
        }
        public override void ExplicitVisit(ExecuteStatement node)
        {
            HasExecute = true;
            base.ExplicitVisit(node);
        }
    }
}
