using System.Collections.Immutable;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Application.Models;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Application.Services;

public interface ISqlSemanticRunner
{
    Task<SqlSemanticReport> ValidateAsync(string original, string candidate, SqlSemanticSuite suite, CancellationToken token = default);
}

/// <summary>Runs each side in its own newly created database on an explicitly named, dedicated LocalDB instance.</summary>
public sealed class LocalDbSqlSemanticRunner(IUnexpectedErrorReporter? unexpectedErrors = null) : ISqlSemanticRunner
{
    public const string SessionSettings = "SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_WARNINGS ON; SET ANSI_PADDING ON; " +
        "SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF; SET XACT_ABORT OFF; SET NOCOUNT OFF; " +
        "SET LANGUAGE us_english; SET DATEFORMAT ymd; SET DATEFIRST 7; SET TRANSACTION ISOLATION LEVEL READ COMMITTED; SET LOCK_TIMEOUT 5000;";
    private readonly IUnexpectedErrorReporter _unexpectedErrors = unexpectedErrors ?? UnexpectedErrorReporter.Shared;

    public async Task<SqlSemanticReport> ValidateAsync(string original, string candidate, SqlSemanticSuite suite, CancellationToken token = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        token = deadline.Token;
        var cases = ImmutableArray.CreateBuilder<SqlSemanticCaseResult>();
        var errors = ImmutableArray.CreateBuilder<string>();
        var cleanup = ImmutableArray.CreateBuilder<string>();
        var diagnostics = ImmutableArray.CreateBuilder<UnexpectedErrorReport>();
        string version = "", sourceHash = "", candidateHash = "", suiteHash = "";
        var status = SemanticValidationStatus.Failed;
        try
        {
            SqlSemanticSandboxPolicy.Validate(suite);
            SqlSemanticSandboxPolicy.ParseBatches(original);
            SqlSemanticSandboxPolicy.ParseBatches(candidate);
            sourceHash = SqlTextHash.Compute(original); candidateHash = SqlTextHash.Compute(candidate); suiteHash = suite.Fingerprint;
            token.ThrowIfCancellationRequested();
            Logger.Debug($"SemanticValidation.Start: Cases={suite.Scenarios.Length}, Compatibility={suite.CompatibilityLevel}");
            await using var admin = Connection(suite.InstanceName, "master");
            await admin.OpenAsync(token);
            using (var command = admin.CreateCommand())
            {
                command.CommandText = "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion'));";
                version = (string)(await command.ExecuteScalarAsync(token))!;
            }
            if (!int.TryParse(version.Split('.')[0], out int major) || major is not (15 or 17) || suite.CompatibilityLevel > major * 10)
                throw new InvalidDataException("仅声明支持 SQL Server 2019/2025 LocalDB 及对应可用的兼容级别 150/170。");
            var budget = new ObservationBudget();
            foreach (var scenario in suite.Scenarios)
            {
                token.ThrowIfCancellationRequested();
                string trackedSql = original + "\nGO\n" + candidate;
                var before = await ObserveAsync(admin, suite, scenario, original, trackedSql, budget, cleanup, diagnostics, token);
                var after = await ObserveAsync(admin, suite, scenario, candidate, trackedSql, budget, cleanup, diagnostics, token);
                bool completed = new[] { before.Execution, before.ObservedState, before.ObjectState,
                    after.Execution, after.ObservedState, after.ObjectState }.All(o => o.ErrorNumbers.IsEmpty);
                cases.Add(new(scenario.Name, SqlSemanticComparison.Equal(before, after, scenario.OrderedResults), completed, before, after));
                Logger.Debug($"SemanticValidation.CaseCompleted: Index={cases.Count}, Matches={cases[^1].Matches}");
            }
            status = cases.Any(c => !c.Matches) ? SemanticValidationStatus.Different :
                cases.Any(c => !c.CompletedWithoutErrors) || cleanup.Count != 0 ? SemanticValidationStatus.Inconclusive :
                SemanticValidationStatus.PassedForScenarios;
            if (status != SemanticValidationStatus.PassedForScenarios) Logger.Warning("SemanticValidation.NotEligible: 场景不匹配、执行错误或清理未完成。");
        }
        catch (Exception exception)
        {
            status = exception is OperationCanceledException ? SemanticValidationStatus.Canceled : SemanticValidationStatus.Failed;
            errors.Add(Describe(exception, "SemanticValidation.Run", diagnostics));
        }
        return new(status, sourceHash, candidateHash, suiteHash, version, suite?.CompatibilityLevel ?? 0, suite?.Collation ?? "",
            SessionSettings, cases.ToImmutable(), errors.ToImmutable(), cleanup.ToImmutable(), diagnostics.ToImmutable());
    }

    private static SqlConnection Connection(string instance, string database) => new(new SqlConnectionStringBuilder
    {
        DataSource = @"(localdb)\" + instance, InitialCatalog = database, IntegratedSecurity = true,
        Pooling = false, Encrypt = SqlConnectionEncryptOption.Optional, ConnectTimeout = 10,
        ApplicationName = "SqlXmlAnalyzer semantic sandbox"
    }.ConnectionString);

    private async Task<SqlSemanticObservation> ObserveAsync(SqlConnection admin, SqlSemanticSuite suite, SqlSemanticScenario scenario,
        string sql, string trackedSql, ObservationBudget budget, ImmutableArray<string>.Builder cleanup,
        ImmutableArray<UnexpectedErrorReport>.Builder diagnostics, CancellationToken token)
    {
        string database = "SxaSemantic_" + Guid.NewGuid().ToString("N");
        return await SqlSemanticDatabaseLifetime.RunAsync(
            () => ExecuteAdminAsync(admin, $"CREATE DATABASE [{database}] COLLATE {suite.Collation};", token),
            async () =>
            {
                await ExecuteAdminAsync(admin, $"ALTER DATABASE [{database}] SET COMPATIBILITY_LEVEL = {suite.CompatibilityLevel}; " +
                    $"ALTER DATABASE [{database}] SET TRUSTWORTHY OFF; ALTER DATABASE [{database}] SET DB_CHAINING OFF;", token);
                await using (var setup = Connection(suite.InstanceName, database))
                {
                    await setup.OpenAsync(token);
                    await ExecuteAdminAsync(setup, "CREATE USER [sxa_probe] WITHOUT LOGIN; ALTER ROLE db_owner ADD MEMBER [sxa_probe];", token);
                }
                // No pooling: the irreversible impersonation token is never reused by an administrator.
                await using var connection = Connection(suite.InstanceName, database);
                await connection.OpenAsync(token);
                await ExecuteAdminAsync(connection, "EXECUTE AS USER = 'sxa_probe' WITH NO REVERT; " + SessionSettings, token);
                var fixture = await ExecuteAsync(connection, scenario.SetupSql, budget, token);
                if (!fixture.ErrorNumbers.IsEmpty) throw new InvalidDataException("场景准备失败，未进行可应用验证；SQL 错误编号：" + string.Join(",", fixture.ErrorNumbers));
                var execution = await ExecuteAsync(connection, sql, budget, token);
                // No auxiliary SQL may run before the observer: even SELECT overwrites @@ROWCOUNT/@@ERROR.
                // The separately checked observer is read-only; any observer error blocks eligibility.
                var observed = await ExecuteAsync(connection, scenario.ObserveSql, budget, token, observer: true);
                int count, state, options;
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT CONVERT(int, @@TRANCOUNT), CONVERT(int, XACT_STATE()), CONVERT(int, @@OPTIONS);";
                    await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, token);
                    await reader.ReadAsync(token);
                    count = reader.GetInt32(0); state = reader.GetInt32(1); options = reader.GetInt32(2);
                }
                var objects = await CaptureObjectsAsync(connection, scenario.SetupSql + "\nGO\n" + trackedSql + "\nGO\n" + scenario.ObserveSql, budget, token);
                return new SqlSemanticObservation(execution, observed, objects, count, state, options);
            },
            cleanupToken => CleanupDatabaseAsync(suite.InstanceName, database, cleanupToken),
            exception => cleanup.Add($"隔离库清理失败：{database}；" + Describe(exception, "SemanticValidation.Cleanup", diagnostics)),
            token);
    }

    internal static async Task CleanupDatabaseAsync(string instance, string database, CancellationToken token)
    {
        const string prefix = "SxaSemantic_";
        if (!database.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(database.AsSpan(prefix.Length), "N", out _))
            throw new InvalidDataException("拒绝清理非本轮格式的隔离数据库名称。");
        // This connection is independent of the possibly canceled/broken CREATE connection.
        await using var cleaner = Connection(instance, "master");
        await cleaner.OpenAsync(token);
        using var command = cleaner.CreateCommand();
        command.CommandTimeout = 15;
        command.CommandText = $"IF DB_ID(@database) IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]; END;";
        command.Parameters.Add("@database", System.Data.SqlDbType.NVarChar, 128).Value = database;
        await command.ExecuteNonQueryAsync(token);
        Logger.Debug("SemanticValidation.CleanupCompleted: 本轮隔离库已确认不存在。");
    }

    private static async Task ExecuteAdminAsync(SqlConnection connection, string sql, CancellationToken token)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql; command.CommandTimeout = 15;
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<SqlExecutionObservation> ExecuteAsync(SqlConnection connection, string sql, ObservationBudget budget,
        CancellationToken token, bool observer = false)
    {
        var results = ImmutableArray.CreateBuilder<SqlResultSet>();
        var errors = ImmutableArray.CreateBuilder<int>();
        int affected = -1;
        var batches = observer ? SqlSemanticSandboxPolicy.ParseObserverBatches(sql) : SqlSemanticSandboxPolicy.ParseBatches(sql);
        foreach (string batch in batches)
        {
            using var command = connection.CreateCommand();
            command.CommandText = batch; command.CommandTimeout = 15;
            try
            {
                await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, token);
                do
                {
                    if (reader.FieldCount == 0) continue;
                    if (reader.FieldCount > 128) throw new InvalidDataException("列数超过验证预算。");
                    if (results.Count >= 16) throw new InvalidDataException("结果集数量超过验证预算，不接受截断结果。");
                    for (int i = 0; i < reader.FieldCount; i++)
                        SqlSemanticValueReader.EnsureComparableType(reader.GetDataTypeName(i));
                    var columns = reader.GetColumnSchema().Select(c => JsonSerializer.Serialize(new
                    { c.ColumnName, c.DataTypeName, c.ColumnSize, c.NumericPrecision, c.NumericScale, c.AllowDBNull })).ToImmutableArray();
                    foreach (string column in columns) budget.AddMetadata(column);
                    var rows = ImmutableArray.CreateBuilder<string>();
                    while (await reader.ReadAsync(token))
                    {
                        var values = new object[reader.FieldCount];
                        for (int i = 0; i < values.Length; i++)
                        {
                            if (await reader.IsDBNullAsync(i, token)) { values[i] = DBNull.Value; continue; }
                            Type type = reader.GetFieldType(i);
                            if (type == typeof(string))
                            {
                                using var text = reader.GetTextReader(i);
                                values[i] = await SqlSemanticValueReader.ReadTextAsync(text, token);
                            }
                            else if (type == typeof(byte[]))
                            {
                                using var stream = reader.GetStream(i);
                                values[i] = await SqlSemanticValueReader.ReadBytesAsync(stream, token);
                            }
                            else values[i] = type == typeof(decimal) ? reader.GetSqlDecimal(i) : reader.GetValue(i);
                        }
                        string row = SqlSemanticComparison.EncodeRow(values);
                        budget.Add(row);
                        rows.Add(row);
                    }
                    results.Add(new(columns, rows.ToImmutable()));
                } while (await reader.NextResultAsync(token));
                affected = SqlSemanticAffectedRows.Combine(affected, reader.RecordsAffected);
            }
            catch (SqlException exception) when (exception.Number != -2 && exception.Class < 20)
            {
                errors.AddRange(exception.Errors.Cast<SqlError>().Select(e => e.Number));
                Logger.Debug("SemanticValidation.SqlErrorObserved: Number=" + exception.Number);
                break;
            }
        }
        return new(results.ToImmutable(), errors.ToImmutable(), affected);
    }

    private static async Task<SqlExecutionObservation> CaptureObjectsAsync(SqlConnection connection, string input, ObservationBudget budget, CancellationToken token)
    {
        const string schemaSql = "SELECT SCHEMA_NAME(t.schema_id) AS SchemaName, t.name AS TableName, c.column_id, c.name AS ColumnName, " +
            "TYPE_NAME(c.user_type_id) AS TypeName, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity, c.is_computed, c.collation_name " +
            "FROM sys.tables t JOIN sys.columns c ON t.object_id=c.object_id WHERE t.is_ms_shipped=0 ORDER BY SchemaName, TableName, c.column_id;";
        var schema = await ExecuteAsync(connection, schemaSql, budget, token);
        var sets = schema.Results.ToBuilder();
        var errors = schema.ErrorNumbers.ToBuilder();
        var names = new List<(string Label, string SqlName)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT SCHEMA_NAME(schema_id), name FROM sys.tables WHERE is_ms_shipped=0 ORDER BY 1,2;";
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                if (names.Count >= 64) throw new InvalidDataException("对象数量超过验证预算。");
                names.Add((reader.GetString(0) + "." + reader.GetString(1), Quote(reader.GetString(0)) + "." + Quote(reader.GetString(1))));
            }
        }
        var tempNames = new TempNames();
        new TSql160Parser(true).Parse(new StringReader(input), out _).Accept(tempNames);
        foreach (string name in tempNames.Names.Order(StringComparer.Ordinal))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT CASE WHEN OBJECT_ID(@name) IS NULL THEN 0 ELSE 1 END;";
            command.Parameters.AddWithValue("@name", "tempdb.." + Quote(name));
            bool exists = (int)(await command.ExecuteScalarAsync(token))! == 1;
            sets.Add(new(["TemporaryObject:" + name], [exists ? "present" : "absent"]));
            if (exists) names.Add((name, Quote(name)));
        }
        foreach (var name in names)
        {
            var data = await ExecuteAsync(connection, "SELECT * FROM " + name.SqlName + ";", budget, token);
            sets.AddRange(data.Results.Select(set => set with { Columns = set.Columns.Insert(0, "Object:" + name.Label) }));
            errors.AddRange(data.ErrorNumbers);
        }
        return new(sets.ToImmutable(), errors.ToImmutable(), 0);
    }

    private string Describe(Exception exception, string operation, ImmutableArray<UnexpectedErrorReport>.Builder diagnostics)
    {
        if (exception is SqlException sql)
        {
            Logger.Error($"{operation}: SQL Server error {sql.Number}。");
            return $"数据库连接、执行或超时失败（SQL 错误 {sql.Number}）。";
        }
        if (ExceptionPolicy.IsExpected(exception)) return ExceptionPolicy.Describe(exception, operation, _unexpectedErrors);
        var report = ExceptionPolicy.Capture(exception, operation, _unexpectedErrors);
        diagnostics.Add(report);
        return "未知语义验证错误；" + report.Summary;
    }

    private static string Quote(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
    private sealed class ObservationBudget
    {
        private int _rows, _bytes;
        public void AddMetadata(string value)
        {
            _bytes = checked(_bytes + Encoding.UTF8.GetByteCount(value));
            if (_bytes > SqlSemanticSandboxPolicy.MaxResultBytes) throw new InvalidDataException("结果超过验证预算。");
        }
        public void Add(string row)
        {
            _rows++; AddMetadata(row);
            if (_rows > SqlSemanticSandboxPolicy.MaxRows || _bytes > SqlSemanticSandboxPolicy.MaxResultBytes)
                throw new InvalidDataException("结果超过验证预算，不接受截断或采样结果作为应用依据。");
        }
    }
    private sealed class TempNames : TSqlFragmentVisitor
    {
        public HashSet<string> Names { get; } = new(StringComparer.Ordinal);
        public override void ExplicitVisit(SchemaObjectName node)
        {
            if (node.BaseIdentifier?.Value is string name && name.StartsWith('#')) Names.Add(name);
            base.ExplicitVisit(node);
        }
    }
}
