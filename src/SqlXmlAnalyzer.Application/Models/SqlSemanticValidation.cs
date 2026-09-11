using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Application.Models;

public sealed record SqlSemanticScenario(string Name, string SetupSql, string ObserveSql = "", bool OrderedResults = false);

public sealed record SqlSemanticSuite(
    ImmutableArray<SqlSemanticScenario> Scenarios,
    string InstanceName = "SqlXmlAnalyzer_IMP19_2025",
    int CompatibilityLevel = 170,
    string Collation = "Latin1_General_100_CI_AS_SC")
{
    [JsonIgnore]
    public string Fingerprint => SqlTextHash.Compute(JsonSerializer.Serialize(this));
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SemanticValidationStatus { PassedForScenarios, Different, Inconclusive, Failed, Canceled }

public sealed record SqlResultSet(ImmutableArray<string> Columns, ImmutableArray<string> Rows);
public sealed record SqlExecutionObservation(
    ImmutableArray<SqlResultSet> Results, ImmutableArray<int> ErrorNumbers, int RecordsAffected);
public sealed record SqlSemanticObservation(
    SqlExecutionObservation Execution, SqlExecutionObservation ObservedState,
    SqlExecutionObservation ObjectState, int TransactionCount, int TransactionState, int SessionOptions);
public sealed record SqlSemanticCaseResult(string Name, bool Matches, bool CompletedWithoutErrors,
    SqlSemanticObservation Original, SqlSemanticObservation Candidate);

public sealed record SqlSemanticReport(
    SemanticValidationStatus Status, string SourceHash, string CandidateHash, string SuiteHash,
    string ServerVersion, int CompatibilityLevel, string Collation, string SessionSettings,
    ImmutableArray<SqlSemanticCaseResult> Cases, ImmutableArray<string> Errors,
    ImmutableArray<string> CleanupFailures, ImmutableArray<UnexpectedErrorReport> Diagnostics)
{
    public bool CanPrepareApply => Status == SemanticValidationStatus.PassedForScenarios &&
        !Cases.IsEmpty && Cases.All(c => c.Matches && c.CompletedWithoutErrors) &&
        Errors.IsEmpty && CleanupFailures.IsEmpty;
    public string Limitation => "仅在记录的数据库版本、兼容级别、排序规则、会话设置和场景数据中匹配；未证明任意输入等价，也未证明性能改善。";
    public string Isolation => "专用 LocalDB；每个场景/每侧独立新库；TRUSTWORTHY/DB_CHAINING OFF；无登录数据库用户 WITH NO REVERT；连接不复用。";
}

/// <summary>Issued in memory after live validation. Serialized reports are never apply credentials.</summary>
public sealed class PreparedSqlRewrite
{
    internal PreparedSqlRewrite(SqlFileSnapshot source, string reviewHash, string candidate, SqlSemanticReport report)
    { Source = source; ReviewHash = reviewHash; Candidate = candidate; Report = report; }
    internal SqlFileSnapshot Source { get; }
    internal string ReviewHash { get; }
    internal string Candidate { get; }
    internal int Consumed;
    public SqlSemanticReport Report { get; }
    public string SourceHash => SqlTextHash.Compute(Source.Text);
    public string CandidateHash => SqlTextHash.Compute(Candidate);
    public bool CanApply => Report.CanPrepareApply && Volatile.Read(ref Consumed) == 0;
}

public sealed record PrepareSqlRewriteResult(PreparedSqlRewrite? Prepared, SqlSemanticReport? Validation,
    string? Error, UnexpectedErrorReport? Diagnostic = null)
{
    public bool CanApply => Prepared?.CanApply == true;
}
public sealed record ApplySqlRewriteResult(bool IsSuccess, SqlWritebackResult? Writeback, string? Error,
    UnexpectedErrorReport? Diagnostic = null)
{
    public bool SourceWritten => Writeback?.SourceWritten == true;
    public bool CommitOutcomeUnknown => Writeback?.CommitOutcomeUnknown == true;
}
