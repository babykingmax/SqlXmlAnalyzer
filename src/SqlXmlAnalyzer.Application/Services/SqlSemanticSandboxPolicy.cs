using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Application.Models;

namespace SqlXmlAnalyzer.Application.Services;

public static class SqlSemanticSandboxPolicy
{
    public const int MaxSqlCharacters = 256 * 1024;
    public const int MaxRows = 10000;
    public const int MaxResultBytes = 4 * 1024 * 1024;

    public static SqlSemanticSuite Load(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 1024 * 1024) throw new InvalidDataException("语义场景文件不得超过 1 MiB。");
        try
        {
            var suite = JsonSerializer.Deserialize<SqlSemanticSuite>(stream, new JsonSerializerOptions
            { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("语义场景文件为空。");
            Validate(suite);
            return suite;
        }
        catch (JsonException exception) { throw new InvalidDataException("语义场景 JSON 无效。", exception); }
    }

    public static void Validate(SqlSemanticSuite suite)
    {
        if (suite == null || !Regex.IsMatch(suite.InstanceName ?? "", "\\ASqlXmlAnalyzer_[A-Za-z0-9_]{1,45}\\z") ||
            suite.CompatibilityLevel is not (150 or 170) ||
            suite.Collation is not ("Latin1_General_100_CI_AS_SC" or "Latin1_General_100_CS_AS_SC") ||
            suite.Scenarios.IsDefaultOrEmpty || suite.Scenarios.Length > 32)
            throw new InvalidDataException("仅支持专用 SqlXmlAnalyzer_* LocalDB、兼容级别 150/170、声明的排序规则及 1–32 个场景。");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scenario in suite.Scenarios)
        {
            if (scenario == null || string.IsNullOrWhiteSpace(scenario.Name) || scenario.Name.Length > 100 || !names.Add(scenario.Name))
                throw new InvalidDataException("场景名称必须非空且唯一，最长 100 字符。");
            ParseBatches(scenario.SetupSql);
            ParseObserverBatches(scenario.ObserveSql);
        }
    }

    public static ImmutableArray<string> ParseBatches(string sql) => ParseBatches(sql, observer: false);

    public static ImmutableArray<string> ParseObserverBatches(string sql) => ParseBatches(sql, observer: true);

    private static ImmutableArray<string> ParseBatches(string sql, bool observer)
    {
        if (sql == null || sql.Length > MaxSqlCharacters) throw new InvalidDataException("SQL 超出语义验证输入预算。");
        var script = new TSql160Parser(true).Parse(new StringReader(sql), out var errors) as TSqlScript;
        if (errors.Count > 0 || script == null || script.Batches.Count > 16)
            throw new InvalidDataException("SQL 语法无效或批次数量超过 16。");
        var policy = new SupportedStatements();
        script.Accept(policy);
        if (policy.Unsupported) throw new InvalidDataException("此语句不在隔离验证支持范围内；禁止 EXEC、USE、权限/服务器操作和跨数据库引用。");
        if (observer)
        {
            var readOnly = new ReadOnlyObserver();
            script.Accept(readOnly);
            if (readOnly.Unsupported)
                throw new InvalidDataException("ObserverReadOnly: ObserveSql 只支持只读 SELECT；禁止写入、SELECT INTO、变量赋值和事务/控制语句。清理由验证器负责。");
        }
        return script.Batches.Select(batch => sql.Substring(batch.StartOffset, batch.FragmentLength)).ToImmutableArray();
    }

    private sealed class ReadOnlyObserver : TSqlFragmentVisitor
    {
        public bool Unsupported { get; private set; }
        public override void Visit(TSqlStatement node)
        {
            if (node is not SelectStatement) Unsupported = true;
            base.Visit(node);
        }
        public override void ExplicitVisit(SelectStatement node)
        {
            if (node.Into != null) Unsupported = true;
            base.ExplicitVisit(node);
        }
        public override void Visit(TSqlFragment node)
        {
            // A SELECT wrapper must not disguise embedded DML or an assignment.
            if (node is DataModificationTableReference or SelectSetVariable) Unsupported = true;
            base.Visit(node);
        }
    }

    private sealed class SupportedStatements : TSqlFragmentVisitor
    {
        public bool Unsupported { get; private set; }
        public override void Visit(TSqlFragment node)
        {
            if (node is AdHocDataSource or AdHocTableReference or OpenRowsetTableReference or InternalOpenRowset or
                OpenRowsetCosmos or BulkOpenRowset or OpenQueryTableReference or NextValueForExpression)
                Unsupported = true;
            base.Visit(node);
        }
        public override void Visit(TSqlStatement node)
        {
            if (node is not (SelectStatement or DeclareVariableStatement or DeclareTableVariableStatement or SetVariableStatement or
                CreateTableStatement or DropTableStatement or InsertStatement or UpdateStatement or DeleteStatement or
                BeginTransactionStatement or CommitTransactionStatement or RollbackTransactionStatement or SaveTransactionStatement or
                IfStatement or WhileStatement or BeginEndBlockStatement or TryCatchStatement or ThrowStatement or RaiseErrorStatement or
                PrintStatement)) Unsupported = true;
            base.Visit(node);
        }
        public override void ExplicitVisit(SchemaObjectName node)
        {
            if (node.Identifiers.Count > 2 || node.BaseIdentifier?.Value.StartsWith("##", StringComparison.Ordinal) == true)
                Unsupported = true;
            base.ExplicitVisit(node);
        }
    }
}
