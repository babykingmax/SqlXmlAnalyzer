using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Rules;
using Xunit;

namespace SqlXmlAnalyzer.Tests.Rules
{
    public class RowEstimateMismatchRuleTests
    {
        [Fact]
        public void RowEstimateMismatch_Critical_ReturnsCritical()
        {
            string xml = EmbeddedResourceHelper.GetResourceContent("plan_critical_mismatch.sqlplan");
            var rule = new RowEstimateMismatchRule();
            XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
            var doc = XDocument.Parse(xml);
            var rootOp = doc.Descendants(ns + "RelOp").FirstOrDefault();

            var result = rule.Analyze(rootOp!, ns);
            
            result.Should().NotBeNull();
            result!.Severity.Should().Be("Critical");
            result.Message.Should().Contain("1,000"); // 1000 estimate, 1 actual -> 1000x mismatch
        }
    }
}
