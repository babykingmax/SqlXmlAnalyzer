using System.Collections.Immutable;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Refactoring;

/// <summary>Produces immutable review previews; it never writes files or executes SQL.</summary>
public sealed class RewriteProposalService
{
    private readonly IUnexpectedErrorReporter _unexpectedErrors;
    public RewriteProposalService(IUnexpectedErrorReporter? unexpectedErrors = null) =>
        _unexpectedErrors = unexpectedErrors ?? UnexpectedErrorReporter.Shared;

    public RewriteProposal Propose(string source, string before, string after, string ruleId, string ruleVersion,
        string description, IEnumerable<string> dependencies, RefactorContext context)
    {
        try
        {
            int start = 0;
            while (start < before.Length && start < after.Length && before[start] == after[start]) start++;
            int end = 0;
            while (end < before.Length - start && end < after.Length - start && before[^(end + 1)] == after[^(end + 1)]) end++;
            string sourceHash = SqlTextHash.Compute(source);
            string baseHash = SqlTextHash.Compute(before);
            string candidateHash = SqlTextHash.Compute(after);
            var prior = dependencies.ToImmutableArray();
            string id = SqlTextHash.Compute(string.Join("\n", sourceHash, baseHash, candidateHash, ruleId, ruleVersion,
                string.Join(",", prior)));
            var validation = Validate(source, after);
            var evidence = ImmutableArray.Create("规则在所示输入 AST 上识别到匹配结构；不证明结果等价。",
                $"源 SQL SHA-256 (UTF-8 text): {sourceHash}");
            // Plan diagnostics are context only: a plan for another statement is not proof about this SQL.
            if (context.Analysis.InputEnvelope is { } envelope)
                evidence = evidence.Add($"附加计划来源 hash: {envelope.SourceHash}；尚未证明与源 SQL 的语句绑定，不作为等价性依据。");
            Logger.Debug($"RewriteProposal.Created: Rule={ruleId}, Dependencies={prior.Length}");
            Logger.Warning("RewriteProposal.EquivalenceUnproven: 默认未选择，禁止自动应用。");
            return new(id, sourceHash, baseHash, candidateHash, ruleId, ruleVersion, description,
                new(start, before.Substring(start, before.Length - start - end), after.Substring(start, after.Length - start - end)),
                prior,
                ["确认列类型、NULL、排序规则、重复行与聚合边界。", "在隔离数据库验证结果、错误、事务和对象副作用。"],
                ["ScriptDom 解析成功仅代表语法有效。", "可能改变结果、溢出/错误行为、事务或性能；等价性尚未证明。"],
                evidence, context.Warnings.ToImmutableArray(), validation);
        }
        catch (Exception exception)
        {
            ExceptionPolicy.Describe(exception, "RewriteProposal.Propose", _unexpectedErrors);
            throw;
        }
    }

    public RewriteReview Review(string source, IEnumerable<RewriteProposal> proposals,
        IEnumerable<string>? selectedIds = null, IEnumerable<string>? warnings = null)
    {
        try
        {
            string hash = SqlTextHash.Compute(source);
            var selected = (selectedIds ?? []).ToHashSet(StringComparer.Ordinal);
            var items = proposals.Select(p => p with { IsSelected = selected.Contains(p.Id) }).ToImmutableArray();
            if (items.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != items.Length)
                throw new InvalidDataException("DuplicateProposal: 提案 ID 重复。");
            if (selected.Except(items.Select(p => p.Id), StringComparer.Ordinal).Any())
                throw new InvalidDataException("UnknownProposal: 选择中包含未知提案。");
            if (items.Any(p => p.SourceHash != hash))
                throw new InvalidDataException("SourceHashMismatch: 源 SQL 已变化，请重新生成提案。");

            string preview = source;
            var applied = new HashSet<string>(StringComparer.Ordinal);
            foreach (var proposal in items.Where(p => p.IsSelected))
            {
                if (!proposal.Validation.IsValid || proposal.DependsOn.Any(id => !applied.Contains(id)))
                    throw new InvalidDataException("ProposalDependency: 提案无效或缺少前序依赖；请按生成顺序选择依赖链。");
                if (SqlTextHash.Compute(preview) != proposal.BaseSqlHash)
                    throw new InvalidDataException("ProposalBaseMismatch: 组合输入与提案基线不一致。");
                var diff = proposal.Diff;
                if (diff.StartOffset < 0 || diff.StartOffset > preview.Length || diff.OriginalText.Length > preview.Length - diff.StartOffset ||
                    !preview.AsSpan(diff.StartOffset, diff.OriginalText.Length).SequenceEqual(diff.OriginalText.AsSpan()))
                    throw new InvalidDataException("ProposalDiffMismatch: 提案范围与原文不匹配。");
                preview = preview.Remove(diff.StartOffset, diff.OriginalText.Length).Insert(diff.StartOffset, diff.ReplacementText);
                if (SqlTextHash.Compute(preview) != proposal.CandidateHash)
                    throw new InvalidDataException("ProposalCandidateMismatch: 候选内容已变化。");
                applied.Add(proposal.Id);
            }
            // Validate the composition, even if every individual proposal previously parsed successfully.
            var validation = Validate(source, preview);
            if (!validation.IsValid) throw new InvalidDataException("CombinedValidationFailed: " + string.Join("; ", validation.Errors));
            Logger.Debug($"RewriteProposal.Review: Proposals={items.Length}, Selected={selected.Count}");
            return new(hash, items, preview, validation,
                (warnings ?? []).Concat(items.SelectMany(p => p.Warnings)).Distinct().ToImmutableArray());
        }
        catch (Exception exception)
        {
            ExceptionPolicy.Describe(exception, "RewriteProposal.Review", _unexpectedErrors);
            throw;
        }
    }

    public static RewriteValidation Validate(string original, string candidate)
    {
        var parser = new TSql160Parser(true);
        using var sourceReader = new StringReader(original);
        var source = parser.Parse(sourceReader, out var sourceErrors);
        using var candidateReader = new StringReader(candidate);
        var fragment = parser.Parse(candidateReader, out var parseErrors);
        var errors = new List<string>();
        if (sourceErrors.Count > 0) errors.Add("源 SQL 语法无效。");
        if (parseErrors.Count > 0) errors.Add($"候选 SQL 语法无效：{parseErrors.Count} 个解析错误。");
        if (sourceErrors.Count == 0 && parseErrors.Count == 0)
        {
            var before = new ObjectEffects(); source.Accept(before);
            var after = new ObjectEffects(); fragment.Accept(after);
            if (!before.Effects.SequenceEqual(after.Effects))
                errors.Add("TemporaryObjectOwnership: SELECT INTO 目标、临时对象创建/删除、表变量声明或所在批次/控制分支发生变化，所有权及作用域未经验证，禁止组合。");
        }
        return new(parseErrors.Count == 0 && sourceErrors.Count == 0,
            ["ScriptDom TSql160 语法解析", "SELECT INTO 目标、显式临时表 CREATE/DROP、表变量声明名称及批次/IF/WHILE/TRY-CATCH 分支一致性检查"],
            ["结果集及重复行等价性", "类型、NULL、排序规则及错误行为", "对象完整定义、模块/动态 SQL 作用域及完整生命周期",
                "事务、会话设置、调用方对象与运行性能"], errors.ToImmutableArray());
    }

    private sealed class ObjectEffects : TSqlFragmentVisitor
    {
        public List<string> Effects { get; } = new();
        private string _scope = "";

        // Preserve identifier boundaries and case: no database collation is known offline.
        // Quoting differences do not change the parsed identifier values.
        private static string ObjectName(SchemaObjectName name) => string.Concat(name.Identifiers.Select(id =>
        {
            string value = id.Value ?? "";
            return $"{value.Length}:{value};";
        }));

        private void Record(string effect) => Effects.Add(_scope + effect);

        private void VisitScoped(string scope, TSqlFragment? fragment)
        {
            if (fragment == null) return;
            string previous = _scope;
            _scope += scope + "/";
            try { fragment.Accept(this); }
            finally { _scope = previous; }
        }

        private static string PredicateHash(BooleanExpression predicate)
        {
            new Sql160ScriptGenerator().GenerateScript(predicate, out string sql);
            return SqlTextHash.Compute(sql);
        }

        public override void ExplicitVisit(IfStatement node)
        {
            string condition = PredicateHash(node.Predicate);
            VisitScoped("IF:" + condition + ":THEN", node.ThenStatement);
            VisitScoped("IF:" + condition + ":ELSE", node.ElseStatement);
        }

        public override void ExplicitVisit(WhileStatement node) =>
            VisitScoped("WHILE:" + PredicateHash(node.Predicate), node.Statement);

        public override void ExplicitVisit(TryCatchStatement node)
        {
            VisitScoped("TRY", node.TryStatements);
            VisitScoped("CATCH", node.CatchStatements);
        }

        public override void ExplicitVisit(TSqlBatch node)
        {
            Effects.Add("BATCH");
            base.ExplicitVisit(node);
            Effects.Add("END BATCH");
        }

        public override void ExplicitVisit(SelectStatement node)
        {
            // SELECT INTO creates a table; it is not represented by CreateTableStatement.
            // Include permanent targets too, so switching away from a temp table cannot hide creation.
            if (node.Into != null) Record("SELECT INTO:" + ObjectName(node.Into));
            base.ExplicitVisit(node);
        }

        public override void ExplicitVisit(CreateTableStatement node)
        {
            if (node.SchemaObjectName.BaseIdentifier.Value.StartsWith('#')) Record("CREATE:" + ObjectName(node.SchemaObjectName));
            base.ExplicitVisit(node);
        }
        public override void ExplicitVisit(DropTableStatement node)
        {
            foreach (var name in node.Objects.Where(n => n.BaseIdentifier.Value.StartsWith('#')))
                Record($"DROP:{node.IsIfExists}:" + ObjectName(name));
            base.ExplicitVisit(node);
        }
        public override void ExplicitVisit(DeclareTableVariableStatement node)
        {
            Record("DECLARE:" + node.Body.VariableName.Value);
            base.ExplicitVisit(node);
        }
    }
}
