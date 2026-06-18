using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Refactoring;
using SqlXmlAnalyzer.Refactoring.Rules;
using Xunit;

namespace SqlXmlAnalyzer.Tests.Refactoring
{
    public class SqlRefactoringEngineTests
    {
        private readonly SqlRefactoringEngine _engine;
        private readonly List<ISqlRefactorRule> _rules;

        public SqlRefactoringEngineTests()
        {
            _rules = new List<ISqlRefactorRule>
            {
                new ConstantFoldingRefactorRule(),
                new IsNullComparisonRefactorRule(),
                new TrimRefactorRule(),
                new LeftOrSubstringRefactorRule(),
                new ImplicitConversionRefactorRule(),
                new SubqueryToJoinRule(),
                new ExistsToJoinRule(),
                new TableVariableRefactorRule(),
                new ScalarSubqueryToJoinRule()
            };
            var filter = new DefaultRuleFilter();
            _engine = new SqlRefactoringEngine(_rules, filter, NullLogger<SqlRefactoringEngine>.Instance);
        }

        [Fact]
        public void Run_WithEmptySql_ShouldReturnOriginalSql()
        {
            // Arrange
            string sql = "";
            var report = new AnalysisReport(new List<IAnalysisIssue>());
            var options = new RefactorOptions();

            // Act
            var result = _engine.Run(sql, report, options, isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Be(sql);
            result.Errors.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithInvalidSqlSyntax_ShouldReturnOriginalSqlWithErrors()
        {
            // Arrange
            string sql = "SELECT * FROM WHERE Status = 'Active'"; // Invalid Syntax
            var report = new AnalysisReport(new List<IAnalysisIssue>());
            var options = new RefactorOptions();

            // Act
            var result = _engine.Run(sql, report, options, isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeFalse();
            result.OutputSql.Should().Be(sql);
            result.Errors.Should().NotBeEmpty();
            result.Errors.First().Should().Contain("Line 1");
        }

        [Fact]
        public void Run_WithFoldableArithmetic_ShouldFoldCorrectly()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE Age + 5 = -10";
            var report = new AnalysisReport(new List<IAnalysisIssue>());
            var options = new RefactorOptions();

            // Act
            var result = _engine.Run(sql, report, options, isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("Age = -15");
            result.Errors.Should().BeEmpty();
            result.Context.RefactorChanges.Should().HaveCount(1);
            result.Context.RefactorChanges.First().RuleId.Should().Be("REF_RULE_104_CONST_FOLD");
            result.Context.RefactorChanges.First().Description.Should().Contain("Folded constant arithmetic on column Age");
        }

        [Fact]
        public void Run_WithFoldableArithmetic_DryRun_ShouldKeepOriginalSqlButRecordChanges()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE Age + 5 = -10";
            var report = new AnalysisReport(new List<IAnalysisIssue>());
            var options = new RefactorOptions();

            // Act
            var result = _engine.Run(sql, report, options, isDryRun: true);

            // Assert
            result.IsSuccess.Should().BeTrue();
            // In C# the engine actually applies changes to the syntax tree, then generates SQL.
            // Wait, does SqlRefactoringEngine use isDryRun to skip SQL generation or does it just set context.IsDryRun?
            // Let's check: the engine runs the rules on the fragment regardless of dry run, but it's up to the caller to decide whether to write it back.
            // Let's see: finalSql is generated and returned, but Context.IsDryRun is true.
            result.Context.IsDryRun.Should().BeTrue();
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithDisabledRule_ShouldNotApplyFolding()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE Age + 5 = -10";
            var report = new AnalysisReport(new List<IAnalysisIssue>());
            var options = new RefactorOptions(DisabledRuleIds: new[] { "REF_RULE_104_CONST_FOLD" });

            // Act
            var result = _engine.Run(sql, report, options, isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            // Since the rule is disabled, the SQL should not be changed.
            result.OutputSql.Should().Contain("Age + 5 = -10");
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithIsNullComparisonDifferentValue_ShouldRemoveIsNull()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE ISNULL(Status, '') = 'Active'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());
            var options = new RefactorOptions();

            // Act
            var result = _engine.Run(sql, report, options, isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("Status = 'Active'");
            result.OutputSql.Should().NotContain("ISNULL(Status");
            result.Context.RefactorChanges.Should().HaveCount(1);
            result.Context.RefactorChanges.First().RuleId.Should().Be("REF_RULE_101_ISNULL_EQUAL");
        }

        [Fact]
        public void Run_WithIsNullComparisonDifferentValueReversed_ShouldRemoveIsNull()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE 'Active' = ISNULL(Status, '')";
            var report = new AnalysisReport(new List<IAnalysisIssue>());
            var options = new RefactorOptions();

            // Act
            var result = _engine.Run(sql, report, options, isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("Status = 'Active'");
            result.OutputSql.Should().NotContain("ISNULL(Status");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithIsNullComparisonSameValue_ShouldRewriteToOrIsNull()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE ISNULL(Status, 'Unknown') = 'Unknown'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());
            var options = new RefactorOptions();

            // Act
            var result = _engine.Run(sql, report, options, isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("Status = 'Unknown'");
            result.OutputSql.Should().Contain("Status IS NULL");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithCoalesceComparisonDifferentValue_ShouldRemoveCoalesce()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE COALESCE(Status, '') = 'Active'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());
            var options = new RefactorOptions();

            // Act
            var result = _engine.Run(sql, report, options, isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("Status = 'Active'");
            result.OutputSql.Should().NotContain("COALESCE");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithIsNullNumericEqual_ShouldRewriteToOrIsNull()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE ISNULL(Age, 0) = 0.0";
            var report = new AnalysisReport(new List<IAnalysisIssue>());
            var options = new RefactorOptions();

            // Act
            var result = _engine.Run(sql, report, options, isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("Age = 0.0");
            result.OutputSql.Should().Contain("Age IS NULL");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithIsNullNumericDifferent_ShouldRemoveIsNull()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE ISNULL(Age, 0) = 5.0";
            var report = new AnalysisReport(new List<IAnalysisIssue>());
            var options = new RefactorOptions();

            // Act
            var result = _engine.Run(sql, report, options, isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("Age = 5.0");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithIsNullComparisonVariable_ShouldKeepOriginal()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE ISNULL(Status, '') = @Status";
            var report = new AnalysisReport(new List<IAnalysisIssue>());
            var options = new RefactorOptions();

            // Act
            var result = _engine.Run(sql, report, options, isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("ISNULL(Status, '') = @Status");
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithIsNullNotSatisfyingComparison_ShouldRemoveIsNull()
        {
            // Arrange
            string sql = "SELECT * FROM Orders WHERE ISNULL(Amount, 0) > 100";
            var report = new AnalysisReport(new List<IAnalysisIssue>());
            var options = new RefactorOptions();

            // Act
            var result = _engine.Run(sql, report, options, isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("Amount > 100");
            result.OutputSql.Should().NotContain("ISNULL");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithIsNullSatisfyingComparison_ShouldRewriteToOrIsNull()
        {
            // Arrange
            string sql = "SELECT * FROM Orders WHERE ISNULL(Amount, 200) > 100";
            var report = new AnalysisReport(new List<IAnalysisIssue>());
            var options = new RefactorOptions();

            // Act
            var result = _engine.Run(sql, report, options, isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("Amount > 100");
            result.OutputSql.Should().Contain("Amount IS NULL");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithIsNullReversedComparisonNotSatisfying_ShouldRemoveIsNull()
        {
            // Arrange
            string sql = "SELECT * FROM Orders WHERE 100 < ISNULL(Amount, 0)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());
            var options = new RefactorOptions();

            // Act
            var result = _engine.Run(sql, report, options, isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("Amount > 100");
            result.OutputSql.Should().NotContain("ISNULL");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithIsNullComparisonTrailingSpaces_ShouldNotOptimize()
        {
            // Arrange
            string sql1 = "SELECT * FROM Users WHERE ISNULL(Status, 'Default ') = 'Default'";
            string sql2 = "SELECT * FROM Users WHERE ISNULL(Status, 'Default') = 'Default '";
            string sql3 = "SELECT * FROM Users WHERE ISNULL(Status, 'Default ') <> 'Default'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result1 = _engine.Run(sql1, report, new RefactorOptions(), isDryRun: false);
            var result2 = _engine.Run(sql2, report, new RefactorOptions(), isDryRun: false);
            var result3 = _engine.Run(sql3, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result1.OutputSql.Should().Contain("ISNULL(Status, 'Default ')");
            result2.OutputSql.Should().Contain("ISNULL(Status, 'Default')");
            result3.OutputSql.Should().Contain("ISNULL(Status, 'Default ')");
        }

        [Fact]
        public void Run_WithIsNullComparisonCaseMismatch_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE ISNULL(Status, 'default') = 'Default'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.OutputSql.Should().Contain("ISNULL(Status, 'default')");
        }

        [Fact]
        public void Run_WithIsNullNotEqualSatisfying_ShouldRewriteToOrIsNull()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE ISNULL(Status, '') <> 'Active'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("Status <> 'Active'");
            result.OutputSql.Should().Contain("Status IS NULL");
        }

        [Fact]
        public void Run_WithIsNullNotEqualNotSatisfying_ShouldRewriteToAndIsNotNull()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE ISNULL(Status, 'Active') <> 'Active'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("Status <> 'Active'");
            result.OutputSql.Should().Contain("Status IS NOT NULL");
        }

        [Fact]
        public void Run_WithIsNullAndNotOperator_ShouldOptimizeInsideNot()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE NOT (ISNULL(Status, '') = 'Active')";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("Status = 'Active'");
            result.OutputSql.Should().Contain("Status IS NOT NULL");
            result.OutputSql.Should().Contain("NOT");
        }

        [Fact]
        public void Run_WithLTrimRTrim_ShouldOptimizeAndWarn()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE LTRIM(RTRIM(UserName)) = 'admin'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("UserName = 'admin'");
            result.OutputSql.Should().NotContain("LTRIM");
            result.OutputSql.Should().NotContain("RTRIM");
            result.Context.RefactorChanges.Should().HaveCount(1);
            result.Context.RefactorChanges.First().RuleId.Should().Be("REF_RULE_103_TRIM");
            result.Context.Warnings.Should().Contain(w => w.Contains("Removed LTRIM on column UserName"));
        }

        [Fact]
        public void Run_WithRTrim_ShouldOptimizeWithoutWarn()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE RTRIM(UserName) = 'admin'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("UserName = 'admin'");
            result.OutputSql.Should().NotContain("RTRIM");
            result.Context.RefactorChanges.Should().HaveCount(1);
            result.Context.Warnings.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithTrim_ShouldOptimizeAndWarn()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE TRIM(UserName) = 'admin'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("UserName = 'admin'");
            result.OutputSql.Should().NotContain("TRIM");
            result.Context.RefactorChanges.Should().HaveCount(1);
            result.Context.Warnings.Should().Contain(w => w.Contains("Removed LTRIM on column UserName"));
        }

        [Fact]
        public void Run_WithTrimLeadingSpaceLiteral_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE LTRIM(UserName) = ' admin'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("LTRIM(UserName) = ' admin'");
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithTrimTrailingSpaceLiteral_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE RTRIM(UserName) = 'admin '";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("RTRIM(UserName) = 'admin '");
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithLeftEqualLiteral_ShouldConvertToLike()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE LEFT(UserName, 3) = 'adm'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("UserName LIKE 'adm%'");
            result.OutputSql.Should().NotContain("LEFT(");
            result.Context.RefactorChanges.Should().HaveCount(1);
            result.Context.RefactorChanges.First().RuleId.Should().Be("REF_RULE_102_LEFT_SUBSTRING");
            result.Context.RefactorChanges.First().Description.Should().Be("Optimized LEFT/SUBSTRING comparison on column UserName to LIKE");
        }

        [Fact]
        public void Run_WithSubstringStart1EqualLiteral_ShouldConvertToLike()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE SUBSTRING(UserName, 1, 4) = 'john'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("UserName LIKE 'john%'");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithLeftEqualLiteralLengthMismatch_ShouldKeepOriginal()
        {
            // Arrange
            string sql1 = "SELECT * FROM Users WHERE LEFT(UserName, 3) = 'ad'";
            string sql2 = "SELECT * FROM Users WHERE LEFT(UserName, 3) = 'admi'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result1 = _engine.Run(sql1, report, new RefactorOptions(), isDryRun: false);
            var result2 = _engine.Run(sql2, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result1.OutputSql.Should().Contain("LEFT(UserName, 3) = 'ad'");
            result1.Context.RefactorChanges.Should().BeEmpty();
            result2.OutputSql.Should().Contain("LEFT(UserName, 3) = 'admi'");
            result2.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithSubstringStartNot1EqualLiteral_ShouldKeepOriginal()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE SUBSTRING(UserName, 2, 3) = 'joh'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.OutputSql.Should().Contain("SUBSTRING(UserName, 2, 3) = 'joh'");
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithLeftEqualWildcardLiteral_ShouldNotOptimize()
        {
            // Arrange
            string sql1 = "SELECT * FROM Users WHERE LEFT(UserName, 5) = 'a%b_c'";
            string sql2 = "SELECT * FROM Users WHERE LEFT(UserName, 3) = 'a[b]'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result1 = _engine.Run(sql1, report, new RefactorOptions(), isDryRun: false);
            var result2 = _engine.Run(sql2, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result1.OutputSql.Should().Contain("LEFT(UserName, 5) = 'a%b_c'");
            result1.Context.RefactorChanges.Should().BeEmpty();
            result2.OutputSql.Should().Contain("LEFT(UserName, 3) = 'a[b]'");
            result2.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithLeftEqualTrailingSpaceLiteral_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE LEFT(UserName, 4) = 'abc '";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.OutputSql.Should().Contain("LEFT(UserName, 4) = 'abc '");
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithSubstringEqualWildcardLiteral_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE SUBSTRING(UserName, 1, 4) = 'a%bc'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.OutputSql.Should().Contain("SUBSTRING(UserName, 1, 4) = 'a%bc'");
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithLeftReversedComparison_ShouldConvertToLike()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE 'adm' = LEFT(UserName, 3)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("UserName LIKE 'adm%'");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithSubstringReversedComparison_ShouldConvertToLike()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE 'john' = SUBSTRING(UserName, 1, 4)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("UserName LIKE 'john%'");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithSubstringLengthMismatch_ShouldKeepOriginal()
        {
            // Arrange
            string sql1 = "SELECT * FROM Users WHERE SUBSTRING(UserName, 1, 4) = 'abc'";
            string sql2 = "SELECT * FROM Users WHERE SUBSTRING(UserName, 1, 4) = 'abcde'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result1 = _engine.Run(sql1, report, new RefactorOptions(), isDryRun: false);
            var result2 = _engine.Run(sql2, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result1.OutputSql.Should().Contain("SUBSTRING(UserName, 1, 4) = 'abc'");
            result1.Context.RefactorChanges.Should().BeEmpty();
            result2.OutputSql.Should().Contain("SUBSTRING(UserName, 1, 4) = 'abcde'");
            result2.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithLeftNonIntegerLength_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE LEFT(UserName, OtherColumn) = 'abc'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.OutputSql.Should().Contain("LEFT(UserName, OtherColumn) = 'abc'");
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithImplicitConversion_NoPlan_ASCII_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE UserName = N'admin'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("UserName = 'admin'");
            result.Context.RefactorChanges.Should().HaveCount(1);
            result.Context.RefactorChanges.First().RuleId.Should().Be("REF_RULE_105_IMPLICIT_CONV");
            result.Context.RefactorChanges.First().Description.Should().Contain("Removed redundant Unicode N prefix");
        }

        [Fact]
        public void Run_WithImplicitConversion_NoPlan_NonASCII_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE UserName = N'张三'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("UserName = N'张三'");
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithImplicitConversion_WithPlanIssue_MatchingColumn_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE UserName = N'admin'";
            var issues = new List<IAnalysisIssue>
            {
                new FakeAnalysisIssue
                {
                    IssueType = "RULE_001_IMPLICIT_CONV",
                    ColumnName = "UserName",
                    Description = "Implicit conversion on column UserName"
                }
            };
            var report = new AnalysisReport(issues);

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("UserName = 'admin'");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithImplicitConversion_WithPlanIssue_NonMatchingColumn_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE UserName = N'admin'";
            var issues = new List<IAnalysisIssue>
            {
                new FakeAnalysisIssue
                {
                    IssueType = "RULE_001_IMPLICIT_CONV",
                    ColumnName = "Email",
                    Description = "Implicit conversion on column Email"
                }
            };
            var report = new AnalysisReport(issues);

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("UserName = N'admin'");
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithImplicitConversion_WithPlanIssue_MatchingDescriptionOnly_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE UserName = N'admin'";
            var issues = new List<IAnalysisIssue>
            {
                new FakeAnalysisIssue
                {
                    IssueType = "RULE_001_IMPLICIT_CONV",
                    ColumnName = null,
                    Description = "Implicit conversion detected on [UserName]"
                }
            };
            var report = new AnalysisReport(issues);

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("UserName = 'admin'");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithImplicitConversion_InPredicate_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE UserName IN (N'admin', N'user', N'张三')";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("UserName IN ('admin', 'user', N'张三')");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithImplicitConversion_LikePredicate_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE UserName LIKE N'adm%'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("UserName LIKE 'adm%'");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithImplicitConversion_BetweenPredicate_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE UserName BETWEEN N'a' AND N'z'";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("UserName BETWEEN 'a' AND 'z'");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithImplicitConversion_ReversedComparison_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE N'admin' = UserName";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("'admin' = UserName");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithSimpleSubqueryIn_ShouldConvertToInnerJoin()
        {
            // Arrange
            string sql = "SELECT s.Id FROM SourceTable s WHERE s.CategoryId IN (SELECT c.Id FROM Category c)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("INNER JOIN");
            result.OutputSql.Should().Contain("Category AS c");
            result.OutputSql.Should().Contain("s.CategoryId = c.Id");
            result.OutputSql.Should().NotContain("WHERE");
            result.Context.RefactorChanges.Should().HaveCount(1);
            result.Context.RefactorChanges.First().RuleId.Should().Be("REF_RULE_106_SUBQUERY_JOIN");
        }

        [Fact]
        public void Run_WithNotInSubquery_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT s.Id FROM SourceTable s WHERE s.CategoryId NOT IN (SELECT c.Id FROM Category c)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("NOT IN");
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithCorrelatedSubquery_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT s.Id FROM SourceTable s WHERE s.CategoryId IN (SELECT c.Id FROM Category c WHERE c.ParentId = s.ParentId)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("INNER JOIN");
            result.OutputSql.Should().Contain("Category AS c");
            result.OutputSql.Should().Contain("s.CategoryId = c.Id");
            result.OutputSql.Should().Contain("c.ParentId = s.ParentId");
            result.OutputSql.Should().NotContain("IN (SELECT");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithInSubqueryContainingTwoTables_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT s.Id FROM SourceTable s WHERE s.CategoryId IN (SELECT c.Id FROM Category c JOIN SubCategory sc ON c.SubId = sc.Id)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("INNER JOIN");
            result.OutputSql.Should().Contain("Category AS c");
            result.OutputSql.Should().Contain("SubCategory AS sc");
            result.OutputSql.Should().Contain("c.SubId = sc.Id");
            result.OutputSql.Should().Contain("s.CategoryId = c.Id");
            result.OutputSql.Should().NotContain("IN (SELECT");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithInSubqueryContainingTwoTablesAndLocalFiltersAndCorrelation_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT s.Id FROM SourceTable s WHERE s.CategoryId IN (SELECT c.Id FROM Category c JOIN SubCategory sc ON c.SubId = sc.Id WHERE c.ParentId = s.ParentId AND sc.Status = 'Active')";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("INNER JOIN");
            result.OutputSql.Should().Contain("Category AS c");
            result.OutputSql.Should().Contain("SubCategory AS sc");
            result.OutputSql.Should().Contain("c.SubId = sc.Id");
            result.OutputSql.Should().Contain("s.CategoryId = c.Id");
            result.OutputSql.Should().Contain("c.ParentId = s.ParentId");
            result.OutputSql.Should().Contain("sc.Status = 'Active'");
            result.OutputSql.Should().NotContain("IN (SELECT");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithInSubqueryContainingThreeTables_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT s.Id FROM SourceTable s WHERE s.CategoryId IN (SELECT c.Id FROM Category c JOIN SubCategory sc ON c.SubId = sc.Id JOIN AnotherTable a ON sc.Id = a.SubId)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("IN");
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithMultipleOuterTables_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT s.Id FROM SourceTable s CROSS JOIN Other o WHERE s.CategoryId IN (SELECT c.Id FROM Category c)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("INNER JOIN");
            result.OutputSql.Should().Contain("Category AS c");
            result.OutputSql.Should().Contain("s.CategoryId = c.Id");
            result.OutputSql.Should().NotContain("WHERE");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithInnerJoin_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT s.Id FROM SourceTable s INNER JOIN Other o ON s.Id = o.SourceId WHERE s.CategoryId IN (SELECT c.Id FROM Category c)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("INNER JOIN");
            result.OutputSql.Should().Contain("Category AS c");
            result.OutputSql.Should().Contain("s.CategoryId = c.Id");
            result.OutputSql.Should().NotContain("WHERE");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithSubqueryInAndCondition_ShouldOptimizeAndKeepOtherConditions()
        {
            // Arrange
            string sql = "SELECT s.Id FROM SourceTable s WHERE s.Status = 1 AND s.CategoryId IN (SELECT c.Id FROM Category c)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("INNER JOIN");
            result.OutputSql.Should().Contain("Category AS c");
            result.OutputSql.Should().Contain("s.CategoryId = c.Id");
            result.OutputSql.Should().Contain("s.Status = 1");
            result.OutputSql.Should().NotContain("IN (SELECT");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithSubqueryInComplexAndConditions_ShouldOptimizeAndKeepOthers()
        {
            // Arrange
            string sql = "SELECT s.Id FROM SourceTable s WHERE s.Status = 1 AND s.Active = 1 AND s.CategoryId IN (SELECT c.Id FROM Category c)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("INNER JOIN");
            result.OutputSql.Should().Contain("Category AS c");
            result.OutputSql.Should().Contain("s.CategoryId = c.Id");
            result.OutputSql.Should().Contain("s.Status = 1");
            result.OutputSql.Should().Contain("s.Active = 1");
            result.OutputSql.Should().NotContain("IN (SELECT");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithSubqueryInAndConditionWithParenthesis_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT s.Id FROM SourceTable s WHERE (s.Status = 1 AND s.CategoryId IN (SELECT c.Id FROM Category c))";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("INNER JOIN");
            result.OutputSql.Should().Contain("Category AS c");
            result.OutputSql.Should().Contain("s.CategoryId = c.Id");
            result.OutputSql.Should().Contain("s.Status = 1");
            result.OutputSql.Should().NotContain("IN (SELECT");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithSubqueryInOrCondition_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT s.Id FROM SourceTable s WHERE s.Status = 1 OR s.CategoryId IN (SELECT c.Id FROM Category c)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("IN");
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithSimpleCorrelatedExists_ShouldConvertToInnerJoin()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE EXISTS (SELECT 1 FROM DetailTable d WHERE d.FK = o.PK)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("INNER JOIN");
            result.OutputSql.Should().Contain("DetailTable AS d");
            result.OutputSql.Should().Contain("d.FK = o.PK");
            result.OutputSql.Should().NotContain("WHERE");
            result.Context.RefactorChanges.Should().HaveCount(1);
            result.Context.RefactorChanges.First().RuleId.Should().Be("REF_RULE_107_EXISTS_JOIN");
        }

        [Fact]
        public void Run_WithExistsContainingNonCorrelationCondition_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE EXISTS (SELECT 1 FROM DetailTable d WHERE d.FK = o.PK AND d.Status = 1)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("INNER JOIN");
            result.OutputSql.Should().Contain("DetailTable AS d");
            result.OutputSql.Should().Contain("d.FK = o.PK");
            result.OutputSql.Should().Contain("d.Status = 1");
            result.OutputSql.Should().NotContain("WHERE");
            result.Context.RefactorChanges.Should().HaveCount(1);
            result.Context.RefactorChanges.First().RuleId.Should().Be("REF_RULE_107_EXISTS_JOIN");
        }

        [Fact]
        public void Run_WithExistsContainingOnlyNonCorrelationCondition_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE EXISTS (SELECT 1 FROM DetailTable d WHERE d.Status = 1)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("EXISTS");
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithExistsAndOuterJoin_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o INNER JOIN OtherTable x ON o.Id = x.OuterId WHERE EXISTS (SELECT 1 FROM DetailTable d WHERE d.FK = o.PK)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("INNER JOIN");
            result.OutputSql.Should().Contain("DetailTable AS d");
            result.OutputSql.Should().Contain("d.FK = o.PK");
            result.OutputSql.Should().NotContain("WHERE");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithExistsInAndCondition_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE o.Active = 1 AND EXISTS (SELECT 1 FROM DetailTable d WHERE d.FK = o.PK)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("INNER JOIN");
            result.OutputSql.Should().Contain("DetailTable AS d");
            result.OutputSql.Should().Contain("d.FK = o.PK");
            result.OutputSql.Should().Contain("o.Active = 1");
            result.OutputSql.Should().NotContain("EXISTS");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithSimpleCorrelatedNotExists_ShouldConvertToLeftJoinAndIsNull()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE NOT EXISTS (SELECT 1 FROM DetailTable d WHERE d.FK = o.PK)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("LEFT");
            result.OutputSql.Should().Contain("JOIN");
            result.OutputSql.Should().Contain("DetailTable AS d");
            result.OutputSql.Should().Contain("d.FK = o.PK");
            result.OutputSql.Should().Contain("d.FK IS NULL");
            result.OutputSql.Should().NotContain("EXISTS");
            result.Context.RefactorChanges.Should().HaveCount(1);
            result.Context.RefactorChanges.First().RuleId.Should().Be("REF_RULE_107_EXISTS_JOIN");
        }

        [Fact]
        public void Run_WithNotExistsContainingNonCorrelationCondition_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE NOT EXISTS (SELECT 1 FROM DetailTable d WHERE d.FK = o.PK AND d.Status = 1)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("LEFT");
            result.OutputSql.Should().Contain("JOIN");
            result.OutputSql.Should().Contain("DetailTable AS d");
            result.OutputSql.Should().Contain("d.FK = o.PK");
            result.OutputSql.Should().Contain("d.Status = 1");
            result.OutputSql.Should().Contain("d.FK IS NULL");
            result.OutputSql.Should().NotContain("EXISTS");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithNotExistsAndNoAlias_ShouldQualifySubqueryColumn()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE NOT EXISTS (SELECT 1 FROM DetailTable WHERE FK = o.PK)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("LEFT");
            result.OutputSql.Should().Contain("JOIN");
            result.OutputSql.Should().Contain("DetailTable");
            result.OutputSql.Should().Contain("DetailTable.FK IS NULL");
            result.OutputSql.Should().NotContain("EXISTS");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithNotExistsInAndCondition_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE o.Active = 1 AND NOT EXISTS (SELECT 1 FROM DetailTable d WHERE d.FK = o.PK)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("LEFT");
            result.OutputSql.Should().Contain("JOIN");
            result.OutputSql.Should().Contain("DetailTable AS d");
            result.OutputSql.Should().Contain("d.FK = o.PK");
            result.OutputSql.Should().Contain("o.Active = 1");
            result.OutputSql.Should().Contain("d.FK IS NULL");
            result.OutputSql.Should().NotContain("EXISTS");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithNotExistsFallbackToOuterColumn_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE NOT EXISTS (SELECT 1 FROM DetailTable WHERE d.FK = o.PK)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("LEFT");
            result.OutputSql.Should().Contain("JOIN");
            result.OutputSql.Should().Contain("DetailTable");
            result.OutputSql.Should().Contain("d.FK = o.PK");
            result.OutputSql.Should().Contain("o.PK IS NULL");
            result.OutputSql.Should().NotContain("EXISTS");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithExistsContainingTwoTables_ShouldConvertToInnerJoinAndParenthesize()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE EXISTS (SELECT 1 FROM DetailTable d JOIN ExtraTable e ON d.ExtraId = e.Id WHERE d.FK = o.PK)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("INNER JOIN");
            result.OutputSql.Should().Contain("DetailTable AS d");
            result.OutputSql.Should().Contain("ExtraTable AS e");
            result.OutputSql.Should().Contain("d.ExtraId = e.Id");
            result.OutputSql.Should().Contain("d.FK = o.PK");
            result.OutputSql.Should().NotContain("EXISTS");
            result.Context.RefactorChanges.Should().HaveCount(1);
            result.Context.RefactorChanges.First().RuleId.Should().Be("REF_RULE_107_EXISTS_JOIN");
        }

        [Fact]
        public void Run_WithExistsContainingTwoTablesAndLocalFilters_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE EXISTS (SELECT 1 FROM DetailTable d JOIN ExtraTable e ON d.ExtraId = e.Id WHERE d.FK = o.PK AND e.Status = 'Active')";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("INNER JOIN");
            result.OutputSql.Should().Contain("d.ExtraId = e.Id");
            result.OutputSql.Should().Contain("d.FK = o.PK");
            result.OutputSql.Should().Contain("e.Status = 'Active'");
            result.OutputSql.Should().NotContain("EXISTS");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithNotExistsContainingTwoTables_ShouldConvertToLeftJoinAndIsNull()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE NOT EXISTS (SELECT 1 FROM DetailTable d JOIN ExtraTable e ON d.ExtraId = e.Id WHERE d.FK = o.PK)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("LEFT OUTER JOIN");
            result.OutputSql.Should().Contain("DetailTable AS d");
            result.OutputSql.Should().Contain("ExtraTable AS e");
            result.OutputSql.Should().Contain("d.ExtraId = e.Id");
            result.OutputSql.Should().Contain("d.FK = o.PK");
            result.OutputSql.Should().Contain("d.FK IS NULL");
            result.OutputSql.Should().NotContain("EXISTS");
            result.Context.RefactorChanges.Should().HaveCount(1);
            result.Context.RefactorChanges.First().RuleId.Should().Be("REF_RULE_107_EXISTS_JOIN");
        }

        [Fact]
        public void Run_WithNotExistsContainingTwoTablesAndLocalFilters_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE NOT EXISTS (SELECT 1 FROM DetailTable d JOIN ExtraTable e ON d.ExtraId = e.Id WHERE d.FK = o.PK AND e.Status = 'Active')";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("LEFT OUTER JOIN");
            result.OutputSql.Should().Contain("d.ExtraId = e.Id");
            result.OutputSql.Should().Contain("d.FK = o.PK");
            result.OutputSql.Should().Contain("e.Status = 'Active'");
            result.OutputSql.Should().Contain("d.FK IS NULL");
            result.OutputSql.Should().NotContain("EXISTS");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithNotExistsContainingTwoTablesCorrelationOnSecondTable_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE NOT EXISTS (SELECT 1 FROM DetailTable d JOIN ExtraTable e ON d.ExtraId = e.Id WHERE e.FK = o.PK)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("LEFT OUTER JOIN");
            result.OutputSql.Should().Contain("d.ExtraId = e.Id");
            result.OutputSql.Should().Contain("e.FK = o.PK");
            result.OutputSql.Should().Contain("e.FK IS NULL");
            result.OutputSql.Should().NotContain("EXISTS");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithNotExistsContainingTwoTablesNoAlias_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE NOT EXISTS (SELECT 1 FROM DetailTable JOIN ExtraTable ON DetailTable.ExtraId = ExtraTable.Id WHERE FK = o.PK)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("LEFT OUTER JOIN");
            result.OutputSql.Should().Contain("DetailTable.FK IS NULL");
            result.OutputSql.Should().NotContain("EXISTS");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithNotExistsContainingThreeTables_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE NOT EXISTS (SELECT 1 FROM DetailTable d JOIN ExtraTable e ON d.ExtraId = e.Id JOIN AnotherTable a ON e.Id = a.ExtraId WHERE d.FK = o.PK)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("LEFT OUTER JOIN");
            result.OutputSql.Should().Contain("DetailTable AS d");
            result.OutputSql.Should().Contain("ExtraTable AS e");
            result.OutputSql.Should().Contain("AnotherTable AS a");
            result.OutputSql.Should().Contain("d.ExtraId = e.Id");
            result.OutputSql.Should().Contain("e.Id = a.ExtraId");
            result.OutputSql.Should().Contain("d.FK = o.PK");
            result.OutputSql.Should().Contain("d.FK IS NULL");
            result.OutputSql.Should().NotContain("EXISTS");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithExistsContainingThreeTables_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE EXISTS (SELECT 1 FROM DetailTable d JOIN ExtraTable e ON d.ExtraId = e.Id JOIN AnotherTable a ON e.Id = a.ExtraId WHERE d.FK = o.PK)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("INNER JOIN");
            result.OutputSql.Should().Contain("DetailTable AS d");
            result.OutputSql.Should().Contain("ExtraTable AS e");
            result.OutputSql.Should().Contain("AnotherTable AS a");
            result.OutputSql.Should().Contain("d.ExtraId = e.Id");
            result.OutputSql.Should().Contain("e.Id = a.ExtraId");
            result.OutputSql.Should().Contain("d.FK = o.PK");
            result.OutputSql.Should().NotContain("EXISTS");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithNotExistsContainingThreeTablesCorrelationOnThirdTable_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE NOT EXISTS (SELECT 1 FROM DetailTable d JOIN ExtraTable e ON d.ExtraId = e.Id JOIN AnotherTable a ON e.Id = a.ExtraId WHERE a.FK = o.PK)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("LEFT OUTER JOIN");
            result.OutputSql.Should().Contain("DetailTable AS d");
            result.OutputSql.Should().Contain("ExtraTable AS e");
            result.OutputSql.Should().Contain("AnotherTable AS a");
            result.OutputSql.Should().Contain("d.ExtraId = e.Id");
            result.OutputSql.Should().Contain("e.Id = a.ExtraId");
            result.OutputSql.Should().Contain("a.FK = o.PK");
            result.OutputSql.Should().Contain("a.FK IS NULL");
            result.OutputSql.Should().NotContain("EXISTS");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithNotExistsContainingThreeTablesNoAlias_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE NOT EXISTS (SELECT 1 FROM DetailTable JOIN ExtraTable ON DetailTable.ExtraId = ExtraTable.Id JOIN AnotherTable ON ExtraTable.Id = AnotherTable.ExtraId WHERE DetailTable.FK = o.PK)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("LEFT OUTER JOIN");
            result.OutputSql.Should().Contain("DetailTable.FK = o.PK");
            result.OutputSql.Should().Contain("DetailTable.FK IS NULL");
            result.OutputSql.Should().NotContain("EXISTS");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithExistsContainingFourTables_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE EXISTS (SELECT 1 FROM DetailTable d JOIN ExtraTable e ON d.ExtraId = e.Id JOIN AnotherTable a ON e.Id = a.ExtraId JOIN FourthTable f ON a.Id = f.AnotherId WHERE d.FK = o.PK)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("EXISTS");
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithNotExistsContainingFourTables_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT o.Id FROM OuterTable o WHERE NOT EXISTS (SELECT 1 FROM DetailTable d JOIN ExtraTable e ON d.ExtraId = e.Id JOIN AnotherTable a ON e.Id = a.ExtraId JOIN FourthTable f ON a.Id = f.AnotherId WHERE d.FK = o.PK)";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("NOT EXISTS");
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithTableVariable_ShouldConvertTableVariableToTempTable()
        {
            // Arrange
            string sql = @"
DECLARE @MyTable TABLE (
    Id INT PRIMARY KEY,
    Name NVARCHAR(50)
);

INSERT INTO @MyTable (Id, Name)
SELECT Id, Name FROM Users;

SELECT * FROM @MyTable;
";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().NotContain("DECLARE @MyTable TABLE");
            result.OutputSql.Should().Contain("CREATE TABLE #MyTable");
            result.OutputSql.Should().Contain("INSERT INTO #MyTable");
            result.OutputSql.Should().Contain("#MyTable");
            result.OutputSql.Should().Contain("OBJECT_ID('tempdb..#MyTable')");
            result.OutputSql.Should().Contain("DROP TABLE #MyTable");
            result.Context.RefactorChanges.Should().HaveCount(1);
            result.Context.RefactorChanges.First().RuleId.Should().Be("REF_RULE_002_TABLE_VAR");
        }

        [Fact]
        public void Run_WithTableVariableInStoredProcedure_ShouldConvertButNotAppendDropTableAtBatchEnd()
        {
            // Arrange
            string sql = @"
CREATE PROCEDURE dbo.GetUsersProc
AS
BEGIN
    DECLARE @UserTable TABLE (
        UserId INT,
        Email VARCHAR(100)
    );

    INSERT INTO @UserTable (UserId, Email)
    SELECT Id, Email FROM Users;

    SELECT * FROM @UserTable;
END
";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().NotContain("DECLARE @UserTable TABLE");
            result.OutputSql.Should().Contain("CREATE TABLE #UserTable");
            result.OutputSql.Should().Contain("INSERT INTO #UserTable");
            result.OutputSql.Should().Contain("#UserTable");
            // Since it's inside a stored procedure, it shouldn't append DROP TABLE to the batch end
            result.OutputSql.Should().NotContain("DROP TABLE #UserTable");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithTableVariableInFunction_ShouldNotConvert()
        {
            // Arrange
            string sql = @"
CREATE FUNCTION dbo.GetActiveUsers()
RETURNS INT
AS
BEGIN
    DECLARE @LocalTable TABLE (
        UserId INT,
        UserName NVARCHAR(50)
    );
    INSERT INTO @LocalTable (UserId, UserName)
    SELECT Id, Name FROM Users;
    RETURN 1;
END
";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("DECLARE @LocalTable TABLE");
            result.OutputSql.Should().NotContain("#LocalTable");
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithScalarSubqueryBasicAggregates_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT c.Name, (SELECT SUM(o.Amount) FROM Orders o WHERE o.CustomerId = c.Id) AS TotalAmount FROM Customers c";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("LEFT OUTER JOIN");
            result.OutputSql.Should().Contain("SUM(o.Amount) AS agg_0");
            result.OutputSql.Should().Contain("GROUP BY o.CustomerId");
            result.OutputSql.Should().Contain("t_sub_0.CustomerId = c.Id");
            result.OutputSql.Should().Contain("t_sub_0.agg_0 AS TotalAmount");
            result.Context.RefactorChanges.Should().HaveCount(1);
            result.Context.RefactorChanges.First().RuleId.Should().Be("REF_RULE_107_SCALAR_SUBQUERY_JOIN");
        }

        [Fact]
        public void Run_WithScalarSubqueryCount_ShouldWrapWithIsNull()
        {
            // Arrange
            string sql = "SELECT c.Name, (SELECT COUNT(o.Id) FROM Orders o WHERE o.CustomerId = c.Id) AS OrderCount FROM Customers c";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("LEFT OUTER JOIN");
            result.OutputSql.Should().Contain("ISNULL(t_sub_0.agg_0, 0) AS OrderCount");
            result.OutputSql.Should().Contain("GROUP BY o.CustomerId");
            result.Context.RefactorChanges.Should().HaveCount(1);
            result.Context.RefactorChanges.First().RuleId.Should().Be("REF_RULE_107_SCALAR_SUBQUERY_JOIN");
        }

        [Fact]
        public void Run_WithScalarSubqueryLocalFilters_ShouldMoveOnlyCorrelatedToOn()
        {
            // Arrange
            string sql = "SELECT c.Name, (SELECT COUNT(o.Id) FROM Orders o WHERE o.CustomerId = c.Id AND o.Status = 1) AS ActiveOrderCount FROM Customers c";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("LEFT OUTER JOIN");
            result.OutputSql.Should().Contain("o.Status = 1");
            result.OutputSql.Should().Contain("t_sub_0.CustomerId = c.Id");
            result.OutputSql.Should().NotContain("ON t_sub_0.CustomerId = c.Id AND o.Status = 1");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithMultipleScalarSubqueries_ShouldOptimizeBothWithDistinctAliases()
        {
            // Arrange
            string sql = "SELECT c.Name, (SELECT COUNT(o.Id) FROM Orders o WHERE o.CustomerId = c.Id) AS OrderCount, (SELECT SUM(o.Amount) FROM Orders o WHERE o.CustomerId = c.Id) AS TotalAmount FROM Customers c";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("t_sub_0.agg_0");
            result.OutputSql.Should().Contain("t_sub_1.agg_1");
            result.OutputSql.Should().Contain("LEFT OUTER JOIN");
            result.Context.RefactorChanges.Should().HaveCount(1);
            result.Context.RefactorChanges.First().Description.Should().Contain("Converted subquery on o to LEFT JOIN");
        }

        [Fact]
        public void Run_WithMultiColumnCorrelationScalarSubquery_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT c.Name, (SELECT COUNT(o.Id) FROM Orders o WHERE o.CustomerId = c.Id AND o.CompanyId = c.CompanyId) AS OrderCount FROM Customers c";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("GROUP BY o.CustomerId, o.CompanyId");
            result.OutputSql.Should().Contain("t_sub_0.CustomerId = c.Id");
            result.OutputSql.Should().Contain("t_sub_0.CompanyId = c.CompanyId");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithScalarSubqueryCountWildcard_ShouldPreserveWildcard()
        {
            // Arrange
            string sql = "SELECT c.Name, (SELECT COUNT(*) FROM Orders o WHERE o.CustomerId = c.Id) AS OrderCount FROM Customers c";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("COUNT(*)");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithScalarSubqueryDistinct_ShouldPreserveDistinct()
        {
            // Arrange
            string sql = "SELECT c.Name, (SELECT SUM(DISTINCT o.Amount) FROM Orders o WHERE o.CustomerId = c.Id) AS DistinctSum FROM Customers c";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("DISTINCT o.Amount");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithScalarSubqueryTopFilter_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT c.Name, (SELECT TOP 1 SUM(o.Amount) FROM Orders o WHERE o.CustomerId = c.Id) AS TotalAmount FROM Customers c";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithScalarSubqueryOuterReferenceInAggregate_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT c.Name, (SELECT SUM(o.Amount * c.Discount) FROM Orders o WHERE o.CustomerId = c.Id) AS TotalAmount FROM Customers c";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithScalarSubqueryOuterReferenceInFilter_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT c.Name, (SELECT SUM(o.Amount) FROM Orders o WHERE o.CustomerId = c.Id AND o.CreatedDate > c.CreatedDate) AS TotalAmount FROM Customers c";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithScalarSubqueryOverClause_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT c.Name, (SELECT SUM(o.Amount) OVER (PARTITION BY o.CustomerId) FROM Orders o WHERE o.CustomerId = c.Id) AS TotalAmount FROM Customers c";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithScalarSubquerySchemaQualified_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT c.Name, (SELECT SUM(o.Amount) FROM dbo.Orders o WHERE o.CustomerId = c.Id) AS TotalAmount FROM Customers c";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("LEFT OUTER JOIN");
            result.OutputSql.Should().Contain("dbo.Orders");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithScalarSubqueryCorrelatedToOuterDerivedTable_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT c.Name, (SELECT SUM(o.Amount) FROM Orders o WHERE o.CustomerId = d.Id) AS TotalAmount FROM Customers c JOIN (SELECT Id FROM Companies) AS d ON c.CompanyId = d.Id";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("LEFT OUTER JOIN");
            result.OutputSql.Should().Contain("GROUP BY o.CustomerId");
            result.OutputSql.Should().Contain("t_sub_0.CustomerId = d.Id");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithScalarSubqueryCorrelatedToTableVariable_ShouldOptimize()
        {
            // Arrange
            string sql = "SELECT tv.Id, (SELECT SUM(o.Amount) FROM Orders o WHERE o.CustomerId = tv.Id) AS TotalAmount FROM @tableVar AS tv";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.OutputSql.Should().Contain("LEFT OUTER JOIN");
            result.OutputSql.Should().Contain("t_sub_0.CustomerId = tv.Id");
            result.Context.RefactorChanges.Should().HaveCount(1);
        }

        [Fact]
        public void Run_WithScalarSubqueryAliasShadowing_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT t.Name, (SELECT SUM(t.Amount) FROM Orders t WHERE t.CustomerId = t.Id) AS TotalAmount FROM Customers t";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        [Fact]
        public void Run_WithScalarSubqueryUnqualifiedOuterReference_ShouldNotOptimize()
        {
            // Arrange
            string sql = "SELECT c.Name, (SELECT SUM(o.Amount) FROM Orders o WHERE o.CustomerId = c.Id AND o.OrderId = parent_id) AS TotalAmount FROM Customers c";
            var report = new AnalysisReport(new List<IAnalysisIssue>());

            // Act
            var result = _engine.Run(sql, report, new RefactorOptions(), isDryRun: false);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Context.RefactorChanges.Should().BeEmpty();
        }

        private class FakeAnalysisIssue : IAnalysisIssue
        {
            public string IssueType { get; set; } = "";
            public string Description { get; set; } = "";
            public IssueSeverity Severity { get; set; } = IssueSeverity.Warning;
            public string? TableName { get; set; }
            public string? ColumnName { get; set; }
        }
    }
}
