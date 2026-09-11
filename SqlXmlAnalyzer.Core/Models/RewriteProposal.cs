using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace SqlXmlAnalyzer.Core.Models;

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum RewriteEquivalence { Unproven }

/// <summary>Offsets refer to the input of this step, identified by BaseSqlHash.</summary>
public sealed record SqlRewriteDiff(int StartOffset, string OriginalText, string ReplacementText);

public sealed record RewriteValidation(
    bool SyntaxValid,
    ImmutableArray<string> CheckedProperties,
    ImmutableArray<string> UnprovenProperties,
    ImmutableArray<string> Errors)
{
    public RewriteEquivalence Equivalence => RewriteEquivalence.Unproven;
    public bool IsValid => SyntaxValid && Errors.IsEmpty;
    // IMP-19 supplies database validation and a separate reviewed apply contract.
    public bool CanApply => false;
}

public sealed record RewriteProposal(
    string Id,
    string SourceHash,
    string BaseSqlHash,
    string CandidateHash,
    string RuleId,
    string RuleVersion,
    string Description,
    SqlRewriteDiff Diff,
    ImmutableArray<string> DependsOn,
    ImmutableArray<string> Preconditions,
    ImmutableArray<string> Risks,
    ImmutableArray<string> Evidence,
    ImmutableArray<string> Warnings,
    RewriteValidation Validation,
    bool IsSelected = false);

public sealed record RewriteReview(
    string SourceHash,
    ImmutableArray<RewriteProposal> Proposals,
    string PreviewSql,
    RewriteValidation Validation,
    ImmutableArray<string> Warnings)
{
    public bool CanApply => false;
}

/// <summary>Hash of the exact decoded SQL as UTF-8 (no BOM), not of the source file bytes.</summary>
public static class SqlTextHash
{
    public static string Compute(string sql) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
}
