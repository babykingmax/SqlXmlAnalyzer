using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Refactoring;
using Xunit;

namespace SqlXmlAnalyzer.Tests.Refactoring
{
    public class NonSargableDetectorTests
    {
        [Fact]
        public void Detect_ArithmeticOnColumn_ShouldDetectNonSargable()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE Age + 1 = 20";

            // Act
            var findings = NonSargableDetector.Detect(sql);

            // Assert
            findings.Should().ContainSingle(f => f.IssueType == "Arithmetic" && f.ColumnName == "Age");
        }

        [Fact]
        public void Detect_FunctionOnColumn_ShouldDetectNonSargable()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE UPPER(Email) = 'TEST@TEST.COM'";

            // Act
            var findings = NonSargableDetector.Detect(sql);

            // Assert
            findings.Should().ContainSingle(f => f.IssueType == "Function" && f.ColumnName == "Email");
        }

        [Fact]
        public void Detect_ColumnComparison_ShouldDetectNonSargable()
        {
            // Arrange
            string sql = "SELECT * FROM Users WHERE CreatedBy <> ModifiedBy";

            // Act
            var findings = NonSargableDetector.Detect(sql);

            // Assert
            findings.Should().ContainSingle(f => f.IssueType == "ColumnComparison");
        }
    }
}
