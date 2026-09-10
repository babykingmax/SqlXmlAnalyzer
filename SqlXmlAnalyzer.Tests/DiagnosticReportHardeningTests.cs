using FluentAssertions;
using SqlXmlAnalyzer.Core.Reporting;
using SqlXmlAnalyzer.Tests.Privacy;

namespace SqlXmlAnalyzer.Tests;

public sealed class DiagnosticReportHardeningTests
{
    [Fact]
    public void Redaction_WhenCostMetricsArePresent_PreservesOptimizerUnitsAndValues()
    {
        var source = PlanRedactionServiceTests.Fixture();
        var op = source.Descendants().Single(e => e.Name.LocalName == "RelOp");
        op.SetAttributeValue("EstimateCPU", "0.1");
        op.SetAttributeValue("EstimateIO", "0.2");
        op.SetAttributeValue("EstimatedTotalSubtreeCost", "0.3");
        var original = DiagnosticReportTests.Report(source);
        var result = new ReportRedactionService().Preview(original);
        result.CanExport.Should().BeTrue(result.Summary);
        foreach (string metric in new[] { "EstimatedCpuCost", "EstimatedIoCost", "SubtreeCost", "OwnCost" })
        {
            string prefix = "B1/S1/Q1/O1/" + metric;
            result.Report!.Facts.Single(f => f.Name == prefix + "/Unit").Value.Should().Be("optimizer-cost");
            result.Report.Facts.Single(f => f.Name == prefix + "/Value").Should().Be(original.Facts.Single(f => f.Name == prefix + "/Value"));
        }
        DiagnosticReportRenderer.Json(result.Report!).Should().NotContain(PlanRedactionServiceTests.Marker);
    }
}
