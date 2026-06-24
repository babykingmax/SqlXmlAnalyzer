using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Rules;
using Xunit;

namespace SqlXmlAnalyzer.Tests.Rules
{
    public class LargeMemoryGrantRuleTests
    {
        [Fact]
        public void LargeMemoryGrant_Excessive_ReturnsWarningAndCritical()
        {
            string xml = EmbeddedResourceHelper.GetResourceContent("plan_large_memory_grant.sqlplan");
            var rule = new LargeMemoryGrantRule();
            XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
            var doc = XDocument.Parse(xml);
            var rootOp = doc.Descendants(ns + "RelOp").FirstOrDefault();

            var result = rule.Analyze(rootOp!, ns);

            // Because there's a spill, it returns Critical
            result.Should().NotBeNull();
            result!.Severity.Should().Be("Critical");
            result.Message.Should().Contain("TempDB");
        }
    }
}
