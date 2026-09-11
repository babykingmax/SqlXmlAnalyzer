using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Analysis;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanGraphPagingTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, 0, 0, 1)]
    [InlineData(100, 0, 0, 64)]
    [InlineData(100, 99, 64, 36)]
    [InlineData(1000, 999, 960, 40)]
    [InlineData(5000, 4999, 4992, 8)]
    [InlineData(5000, int.MaxValue, 4992, 8)]
    [InlineData(100, -1, 0, 64)]
    public void Page_BoundsEveryContainerWindow(int total, int index, int start, int count)
    {
        var page = PlanGraphPageService.ForIndex(total, index);
        page.Start.Should().Be(start); page.Count.Should().Be(count); page.Total.Should().Be(total);
        page.Count.Should().BeLessThanOrEqualTo(PlanGraphPageService.MaximumVisibleNodes);
    }

    [Fact]
    public void Pages_CoverEveryOperatorExactlyOnceWithoutDiscardingTail()
    {
        var indices = new List<int>();
        for (int index = 0; index < 5000; index += PlanGraphPageService.MaximumVisibleNodes)
        {
            var page = PlanGraphPageService.ForIndex(5000, index);
            indices.AddRange(Enumerable.Range(page.Start, page.Count));
        }
        indices.Should().Equal(Enumerable.Range(0, 5000));
    }

    [Fact]
    public void ReusedDiagnostics_AcceptInjectedBuilderForTheSameSourceSnapshot()
    {
        var input = PlanAnalysisSourceTests.Input("select 1");
        var report = new SqlXmlAnalysisEngine(planBuilder: new PlanDocumentBuilder()).AnalyzeInput(input);
        report.InputStatus.Should().Be(InputStatus.Success);
        var reused = SqlXmlAnalysisEngine.FromDiagnostics(input, report.Plan!, report.Diagnostics!);
        reused.Plan.Should().BeSameAs(report.Plan);
        reused.Diagnostics.Should().BeSameAs(report.Diagnostics);
    }

    [Fact]
    public void ReusedDiagnostics_RejectForeignDocumentOrChangedSource()
    {
        var a = PlanAnalysisSourceTests.Input("select 1"); var b = PlanAnalysisSourceTests.Input("select 2");
        var analyzed = new SqlXmlAnalysisEngine().AnalyzeInput(a);
        Action foreign = () => SqlXmlAnalysisEngine.FromDiagnostics(b, analyzed.Plan!, analyzed.Diagnostics!);
        foreign.Should().Throw<System.IO.InvalidDataException>();
        a.Document!.Root!.SetAttributeValue("changed", "yes");
        Action changed = () => SqlXmlAnalysisEngine.FromDiagnostics(a, analyzed.Plan!, analyzed.Diagnostics!);
        changed.Should().Throw<System.IO.InvalidDataException>();
    }
}
