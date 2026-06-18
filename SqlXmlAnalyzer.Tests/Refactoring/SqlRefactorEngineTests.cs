using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core.Refactoring;
using Xunit;

namespace SqlXmlAnalyzer.Tests.Refactoring
{
    public class SqlRefactorEngineTests
    {
        [Fact]
        public void DefaultConstructor_ShouldOnlyRegisterCoreRules()
        {
            // Arrange & Act
            var engine = new SqlRefactorEngine();

            // Assert
            engine.Rules.Should().ContainSingle();
            engine.Rules.First().GetType().Name.Should().Be("SargableRefactorRule");
        }



        [Fact]
        public void Refactor_WithNonPredicateAssignment_ShouldNotStripNPrefix()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            // Variable assignments and parameters should NOT be modified by the visitor.
            string sql = "DECLARE @UnicodeVar NVARCHAR(50) = N'UnicodeValue';";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("N'UnicodeValue'");
        }

        [Fact]
        public void Refactor_WithUnicodeCharacters_ShouldNotStripNPrefix()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE UserName = N'张三'";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("UserName = N'张三'");
        }


        [Fact]
        public void Refactor_WithIsNullComparisonDifferentValue_ShouldRemoveIsNull()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE ISNULL(Status, '') = 'Active'";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("Status = 'Active'");
            refactored.Should().NotContain("ISNULL(Status");
        }

        [Fact]
        public void Refactor_WithIsNullComparisonDifferentValueReversed_ShouldRemoveIsNull()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE 'Active' = ISNULL(Status, '')";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("Status = 'Active'");
            refactored.Should().NotContain("ISNULL(Status");
        }

        [Fact]
        public void Refactor_WithIsNullComparisonSameValue_ShouldRewriteToOrIsNull()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE ISNULL(Status, 'Unknown') = 'Unknown'";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("Status = 'Unknown'");
            refactored.Should().Contain("Status IS NULL");
        }



        [Fact]
        public void Refactor_WithCoalesceComparisonDifferentValue_ShouldRemoveCoalesce()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE COALESCE(Status, '') = 'Active'";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("Status = 'Active'");
            refactored.Should().NotContain("COALESCE");
        }

        [Fact]
        public void Refactor_WithIsNullNumericEqual_ShouldRewriteToOrIsNull()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE ISNULL(Age, 0) = 0.0";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("Age = 0.0");
            refactored.Should().Contain("Age IS NULL");
        }

        [Fact]
        public void Refactor_WithIsNullNumericDifferent_ShouldRemoveIsNull()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE ISNULL(Age, 0) = 5.0";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("Age = 5.0");
        }

        [Fact]
        public void Refactor_WithIsNullComparisonVariable_ShouldKeepOriginal()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE ISNULL(Status, '') = @Status";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("ISNULL(Status, '') = @Status");
        }

        [Fact]
        public void Test_CoalesceType()
        {
            var parser = new TSql160Parser(true);
            using (var reader = new System.IO.StringReader("SELECT COALESCE(Status, '')"))
            {
                var fragment = parser.Parse(reader, out var errors);
                var selectStatement = (SelectStatement)((TSqlScript)fragment).Batches[0].Statements[0];
                var querySpecification = (QuerySpecification)selectStatement.QueryExpression;
                var selectElement = (SelectScalarExpression)querySpecification.SelectElements[0];
                var expr = selectElement.Expression;
                
                expr.GetType().FullName.Should().Be("Microsoft.SqlServer.TransactSql.ScriptDom.CoalesceExpression");
            }
        }

        [Fact]
        public void Refactor_WithLeftEqualLiteral_ShouldConvertToLike()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE LEFT(UserName, 3) = 'adm'";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("UserName LIKE 'adm%'");
            refactored.Should().NotContain("LEFT(");
        }



        [Fact]
        public void Refactor_WithLeftEqualLiteralReversed_ShouldConvertToLike()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE 'adm' = LEFT(UserName, 3)";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("UserName LIKE 'adm%'");
        }

        [Fact]
        public void Refactor_WithLeftEqualLiteralLengthMismatch_ShouldKeepOriginal()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql1 = "SELECT * FROM Users WHERE LEFT(UserName, 3) = 'ad'";
            string sql2 = "SELECT * FROM Users WHERE LEFT(UserName, 3) = 'admi'";

            // Act & Assert
            engine.Refactor(sql1, out var errors1).Should().Contain("LEFT(UserName, 3) = 'ad'");
            engine.Refactor(sql2, out var errors2).Should().Contain("LEFT(UserName, 3) = 'admi'");
        }

        [Fact]
        public void Refactor_WithLeftNotEqualLiteral_ShouldConvertToNotLike()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE LEFT(UserName, 3) <> 'adm'";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("UserName NOT LIKE 'adm%'");
        }

        [Fact]
        public void Refactor_WithSubstringStart1EqualLiteral_ShouldConvertToLike()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE SUBSTRING(UserName, 1, 4) = 'john'";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("UserName LIKE 'john%'");
        }

        [Fact]
        public void Refactor_WithSubstringStartNot1EqualLiteral_ShouldKeepOriginal()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE SUBSTRING(UserName, 2, 3) = 'john'";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("SUBSTRING(UserName, 2, 3) = 'john'");
        }

        [Fact]
        public void Refactor_WithLeftEqualWildcardLiteral_ShouldEscapeWildcards()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE LEFT(UserName, 5) = 'a%b_c'";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            // % -> [%], _ -> [_]
            refactored.Should().Contain("UserName LIKE 'a[%]b[_]c%'");
        }

        [Fact]
        public void Refactor_WithLeftEqualVariable_ShouldKeepOriginal()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE LEFT(UserName, 3) = @pattern";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("LEFT(UserName, 3) = @pattern");
        }

        [Fact]
        public void Refactor_WithYearEqualLiteral_ShouldConvertToDateRange()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE YEAR(OrderDate) = 2026";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("OrderDate >= '2026-01-01'");
            refactored.Should().Contain("OrderDate < '2027-01-01'");
            refactored.Should().NotContain("YEAR(");
        }

        [Fact]
        public void Refactor_WithYearGreaterThanLiteral_ShouldConvertToDateComparison()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE YEAR(OrderDate) > 2026";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("OrderDate >= '2027-01-01'");
            refactored.Should().NotContain("YEAR(");
        }

        [Fact]
        public void Refactor_WithYearReversedComparison_ShouldConvertToCorrectComparison()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE 2026 >= YEAR(OrderDate)";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("OrderDate < '2027-01-01'");
            refactored.Should().NotContain("YEAR(");
        }

        [Fact]
        public void Refactor_WithConvertDateEqualLiteral_ShouldConvertToDateRange()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE CONVERT(date, OrderDate) = '2026-06-15'";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("OrderDate >= '2026-06-15'");
            refactored.Should().Contain("OrderDate < '2026-06-16'");
            refactored.Should().NotContain("CONVERT(");
        }

        [Fact]
        public void Refactor_WithCastDateNotEqualLiteral_ShouldConvertToDisjointRange()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE CAST(OrderDate AS date) <> '2026-06-15'";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("OrderDate < '2026-06-15'");
            refactored.Should().Contain("OrderDate >= '2026-06-16'");
            refactored.Should().NotContain("CAST(");
        }

        [Fact]
        public void Refactor_WithConvertDateInvalidLiteral_ShouldKeepOriginal()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE CONVERT(date, OrderDate) = 'invalid-date'";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("CONVERT (DATE, OrderDate) = 'invalid-date'");
        }

        [Fact]
        public void Refactor_WithDatePartYear_ShouldConvertToDateRange()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE DATEPART(year, OrderDate) = 2026";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("OrderDate >= '2026-01-01'");
            refactored.Should().Contain("OrderDate < '2027-01-01'");
            refactored.Should().NotContain("DATEPART");
        }

        [Fact]
        public void Refactor_WithDatePartYearQuoted_ShouldConvertToDateRange()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE DATEPART('yyyy', OrderDate) > 2026";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("OrderDate >= '2027-01-01'");
            refactored.Should().NotContain("DATEPART");
        }

        [Fact]
        public void Refactor_WithIsNullNotSatisfyingComparison_ShouldRemoveIsNull()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE ISNULL(Amount, 0) > 100";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("Amount > 100");
            refactored.Should().NotContain("ISNULL");
        }

        [Fact]
        public void Refactor_WithIsNullSatisfyingComparison_ShouldRewriteToOrIsNull()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE ISNULL(Amount, 200) > 100";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("Amount > 100");
            refactored.Should().Contain("Amount IS NULL");
        }

        [Fact]
        public void Refactor_WithIsNullReversedComparisonNotSatisfying_ShouldRemoveIsNull()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE 100 < ISNULL(Amount, 0)";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("Amount > 100");
            refactored.Should().NotContain("ISNULL");
        }

        [Fact]
        public void Refactor_WithRedundantAndConditions_ShouldDeduplicate()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE Status = 'Active' AND Amount > 100 AND Status = 'Active'";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("Status = 'Active'");
            refactored.Should().Contain("Amount > 100");
            
            int count = refactored.Split("Status = 'Active'").Length - 1;
            count.Should().Be(1);
        }

        [Fact]
        public void Refactor_WithTautologyInAnd_ShouldRemoveTautology()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE 1 = 1 AND Status = 'Active'";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("Status = 'Active'");
            refactored.Should().NotContain("1 = 1");
        }

        [Fact]
        public void Refactor_WithContradictionInAnd_ShouldCollapseToContradiction()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE Status = 'Active' AND 1 = 0";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("1 = 0");
            refactored.Should().NotContain("Status = 'Active'");
        }

        [Fact]
        public void Refactor_WithTautologyInOr_ShouldCollapseToTautology()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE Status = 'Active' OR 1 = 1";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("1 = 1");
            refactored.Should().NotContain("Status = 'Active'");
        }

        [Fact]
        public void Refactor_WithContradictionInOr_ShouldRemoveContradiction()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE Status = 'Active' OR 1 = 0";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("Status = 'Active'");
            refactored.Should().NotContain("1 = 0");
        }

        [Fact]
        public void Refactor_WithDateAddConstant_ShouldMoveToRightSide()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE DATEADD(day, 30, OrderDate) >= GETDATE()";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("OrderDate >= DATEADD(day, -30, GETDATE())");
            refactored.Should().NotContain("DATEADD(day, 30");
        }

        [Fact]
        public void Refactor_WithDateAddNegativeConstant_ShouldMoveToRightSide()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE DATEADD(month, -1, CreatedDate) < '2026-01-01'";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("CreatedDate < DATEADD(month, 1, '2026-01-01')");
            refactored.Should().NotContain("DATEADD(month, -1");
        }

        [Fact]
        public void Refactor_WithDateDiffDayEqualsZero_ShouldConvertToRange()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE DATEDIFF(day, OrderDate, '2026-06-15') = 0";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("OrderDate >= '2026-06-15'");
            refactored.Should().Contain("OrderDate < '2026-06-16'");
            refactored.Should().NotContain("DATEDIFF");
        }

        [Fact]
        public void Refactor_WithDateDiffDayGreaterThanConstant_ShouldConvertToComparison()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE DATEDIFF(day, '2026-06-15', OrderDate) > 5";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("OrderDate >= '2026-06-21'");
            refactored.Should().NotContain("DATEDIFF");
        }

        [Fact]
        public void Refactor_WithDateDiffYearEqualsConstant_ShouldConvertToRange()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Orders WHERE DATEDIFF(year, OrderDate, '2026-06-15') = 3";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("OrderDate >= '2023-01-01'");
            refactored.Should().Contain("OrderDate < '2024-01-01'");
            refactored.Should().NotContain("DATEDIFF");
        }

        [Fact]
        public void Refactor_WithAbsNegativeConstant_ShouldRewriteToNullPropagatingFalse()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Products WHERE ABS(Weight) < -5";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("Weight < -5");
            refactored.Should().Contain("Weight > 5");
            refactored.Should().NotContain("1 = 0");
        }

        [Fact]
        public void Refactor_WithAbsZero_ShouldRewriteToDirectComparison()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Products WHERE ABS(Weight) = 0";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("Weight = 0");
        }

        [Fact]
        public void Refactor_WithAbsPositiveConstant_ShouldRewriteToDoubleBoundedComparison()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Products WHERE ABS(Weight) < 10";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("Weight < 10");
            refactored.Should().Contain("Weight > -10");
        }

        [Fact]
        public void Refactor_WithRTrim_ShouldRemoveFunction()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE RTRIM(UserName) = 'admin'";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("UserName = 'admin'");
            refactored.Should().NotContain("RTRIM");
        }

        [Fact]
        public void Refactor_WithRecursiveLogicSimplification_ShouldSimplify()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE UserName = 'admin' AND 1 = 1";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("UserName = 'admin'");
            refactored.Should().NotContain("1 = 1");
        }

        [Fact]
        public void Refactor_WithIsNullInList_DefaultValueInList_ShouldRewriteToOrIsNull()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE ISNULL(UserRole, 'Guest') IN ('Admin', 'Guest')";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("UserRole IN ('Admin', 'Guest')");
            refactored.Should().Contain("UserRole IS NULL");
            refactored.Should().Contain("OR");
            refactored.Should().NotContain("ISNULL");
        }

        [Fact]
        public void Refactor_WithIsNullInList_DefaultValueNotInList_ShouldRewriteToAndIsNotNull()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE ISNULL(UserRole, 'Guest') IN ('Admin', 'Manager')";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("UserRole IN ('Admin', 'Manager')");
            refactored.Should().Contain("UserRole IS NOT NULL");
            refactored.Should().Contain("AND");
            refactored.Should().NotContain("ISNULL");
        }

        [Fact]
        public void Refactor_WithIsNullNotInList_DefaultValueInList_ShouldRewriteToAndIsNotNull()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE ISNULL(UserRole, 'Guest') NOT IN ('Admin', 'Guest')";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("UserRole NOT IN ('Admin', 'Guest')");
            refactored.Should().Contain("UserRole IS NOT NULL");
            refactored.Should().Contain("AND");
            refactored.Should().NotContain("ISNULL");
        }

        [Fact]
        public void Refactor_WithIsNullNotInList_DefaultValueNotInList_ShouldRewriteToOrIsNull()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE ISNULL(UserRole, 'Guest') NOT IN ('Admin', 'Manager')";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("UserRole NOT IN ('Admin', 'Manager')");
            refactored.Should().Contain("UserRole IS NULL");
            refactored.Should().Contain("OR");
            refactored.Should().NotContain("ISNULL");
        }

        [Fact]
        public void Refactor_WithCoalesceInList_DefaultValueInList_ShouldRewriteToOrIsNull()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE COALESCE(UserRole, 'Guest') IN ('Admin', 'Guest')";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("UserRole IN ('Admin', 'Guest')");
            refactored.Should().Contain("UserRole IS NULL");
            refactored.Should().Contain("OR");
            refactored.Should().NotContain("COALESCE");
        }


        [Fact]
        public void Refactor_WithAbsNegativeConstantAndNot_ShouldPreserveThreeValuedLogic()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Products WHERE NOT (ABS(Weight) < -5)";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("NOT");
            refactored.Should().Contain("Weight < -5");
            refactored.Should().Contain("Weight > 5");
        }

        [Fact]
        public void Refactor_WithAbsZeroLessThan_ShouldRewriteToNullPropagatingFalse()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Products WHERE ABS(Weight) < 0";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("Weight < 0");
            refactored.Should().Contain("Weight > 0");
        }

        [Fact]
        public void Refactor_WithAbsZeroGreaterThanOrEqualTo_ShouldRewriteToNullPropagatingTrue()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Products WHERE ABS(Weight) >= 0";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("Weight = Weight");
        }

        [Fact]
        public void Refactor_WithIsNullComparisonDifferentValue_ShouldRewriteToAndIsNotNull()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE ISNULL(Status, '') = 'Active'";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("Status = 'Active'");
            refactored.Should().Contain("Status IS NOT NULL");
        }

        [Fact]
        public void Refactor_WithIsNullComparisonDifferentValueAndNot_ShouldPreserveThreeValuedLogic()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE NOT (ISNULL(Status, '') = 'Active')";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("NOT");
            refactored.Should().Contain("Status = 'Active'");
            refactored.Should().Contain("Status IS NOT NULL");
        }


        [Fact]
        public void Refactor_WithIsNullInList_VariableOrDefaultValueVariable_ShouldKeepOriginal()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql1 = "SELECT * FROM Users WHERE ISNULL(UserRole, 'Guest') IN ('Admin', @Var)";
            string sql2 = "SELECT * FROM Users WHERE ISNULL(UserRole, @Var) IN ('Admin', 'Guest')";

            // Act
            string refactored1 = engine.Refactor(sql1, out var errors1);
            string refactored2 = engine.Refactor(sql2, out var errors2);

            // Assert
            errors1.Should().BeEmpty();
            refactored1.Should().Contain("ISNULL(UserRole, 'Guest') IN ('Admin', @Var)");

            errors2.Should().BeEmpty();
            refactored2.Should().Contain("ISNULL(UserRole, @Var) IN ('Admin', 'Guest')");
        }

        [Fact]
        public void Refactor_WithIsNullComparisonTrailingSpaces_ShouldNotOptimize()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql1 = "SELECT * FROM Users WHERE ISNULL(Status, 'Default ') = 'Default'";
            string sql2 = "SELECT * FROM Users WHERE ISNULL(Status, 'Default') = 'Default '";
            string sql3 = "SELECT * FROM Users WHERE ISNULL(Status, 'Default ') <> 'Default'";

            // Act
            string refactored1 = engine.Refactor(sql1, out var errors1);
            string refactored2 = engine.Refactor(sql2, out var errors2);
            string refactored3 = engine.Refactor(sql3, out var errors3);

            // Assert
            errors1.Should().BeEmpty();
            refactored1.Should().Contain("ISNULL(Status, 'Default ')");

            errors2.Should().BeEmpty();
            refactored2.Should().Contain("ISNULL(Status, 'Default')");

            errors3.Should().BeEmpty();
            refactored3.Should().Contain("ISNULL(Status, 'Default ')");
        }

        [Fact]
        public void Refactor_WithIsNullComparisonCaseMismatch_ShouldNotOptimize()
        {
            // Arrange
            var engine = new SqlRefactorEngine(registerLegacyRules: true);
            string sql = "SELECT * FROM Users WHERE ISNULL(Status, 'default') = 'Default'";

            // Act
            string refactored = engine.Refactor(sql, out var errors);

            // Assert
            errors.Should().BeEmpty();
            refactored.Should().Contain("ISNULL(Status, 'default')");
        }





    }
}
