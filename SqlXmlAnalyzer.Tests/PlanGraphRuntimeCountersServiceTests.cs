using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphRuntimeCountersServiceTests
    {
        private static readonly XNamespace Ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
        private readonly PlanGraphRuntimeCountersService _service = new();

        [Fact]
        public void Parse_WhenRuntimeInformationIsMissing_ReturnsEmptyResult()
        {
            XElement relOp = new(Ns + "RelOp");

            PlanGraphRuntimeCountersResult result = _service.Parse(relOp, Ns);

            result.HasActual.Should().BeFalse();
            result.HasActualRead.Should().BeFalse();
            result.ActualRows.Should().Be(0);
            result.ActualRowsRead.Should().Be(0);
            result.ActualExecutions.Should().Be(0);
            result.ActualRebinds.Should().Be(0);
            result.ActualRewinds.Should().Be(0);
            result.IsThreadDataSkewed.Should().BeFalse();
        }

        [Fact]
        public void Parse_WhenRuntimeCountersExist_AggregatesRuntimeMetrics()
        {
            XElement relOp = CreateRelOp(
                new XElement(Ns + "RunTimeCountersPerThread",
                    new XAttribute("Thread", "0"),
                    new XAttribute("ActualRows", "10"),
                    new XAttribute("ActualRowsRead", "25"),
                    new XAttribute("ActualExecutions", "2"),
                    new XAttribute("ActualRebinds", "3"),
                    new XAttribute("ActualRewinds", "4")),
                new XElement(Ns + "RunTimeCountersPerThread",
                    new XAttribute("Thread", "1"),
                    new XAttribute("ActualRows", "30"),
                    new XAttribute("ActualRowsRead", "45"),
                    new XAttribute("ActualExecutions", "5"),
                    new XAttribute("ActualRebinds", "6"),
                    new XAttribute("ActualRewinds", "7")));

            PlanGraphRuntimeCountersResult result = _service.Parse(relOp, Ns);

            result.HasActual.Should().BeTrue();
            result.HasActualRead.Should().BeTrue();
            result.ActualRows.Should().Be(40);
            result.ActualRowsRead.Should().Be(70);
            result.ActualExecutions.Should().Be(7);
            result.ActualRebinds.Should().Be(9);
            result.ActualRewinds.Should().Be(11);
        }

        [Fact]
        public void Parse_WhenActualRowsReadIsMissing_UsesActualRowsAsRowsRead()
        {
            XElement relOp = CreateRelOp(
                new XElement(Ns + "RunTimeCountersPerThread",
                    new XAttribute("ActualRows", "42")));

            PlanGraphRuntimeCountersResult result = _service.Parse(relOp, Ns);

            result.HasActual.Should().BeTrue();
            result.HasActualRead.Should().BeFalse();
            result.ActualRows.Should().Be(42);
            result.ActualRowsRead.Should().Be(42);
            result.ActualExecutions.Should().Be(1);
        }

        [Fact]
        public void Parse_WhenWorkerRowsAreSkewed_DetectsThreadDataSkew()
        {
            XElement relOp = CreateRelOp(
                new XElement(Ns + "RunTimeCountersPerThread",
                    new XAttribute("Thread", "1"),
                    new XAttribute("ActualRows", "9000")),
                new XElement(Ns + "RunTimeCountersPerThread",
                    new XAttribute("Thread", "2"),
                    new XAttribute("ActualRows", "1000")),
                new XElement(Ns + "RunTimeCountersPerThread",
                    new XAttribute("Thread", "3"),
                    new XAttribute("ActualRows", "100")));

            PlanGraphRuntimeCountersResult result = _service.Parse(relOp, Ns);

            result.IsThreadDataSkewed.Should().BeTrue();
        }

        [Fact]
        public void Parse_WhenOnlyCoordinatorThreadIsLarge_DoesNotFlagSkew()
        {
            XElement relOp = CreateRelOp(
                new XElement(Ns + "RunTimeCountersPerThread",
                    new XAttribute("Thread", "0"),
                    new XAttribute("ActualRows", "100000")),
                new XElement(Ns + "RunTimeCountersPerThread",
                    new XAttribute("Thread", "1"),
                    new XAttribute("ActualRows", "50")),
                new XElement(Ns + "RunTimeCountersPerThread",
                    new XAttribute("Thread", "2"),
                    new XAttribute("ActualRows", "50")));

            PlanGraphRuntimeCountersResult result = _service.Parse(relOp, Ns);

            result.IsThreadDataSkewed.Should().BeFalse();
        }

        [Fact]
        public void Parse_WhenWorkerRowsAreSmall_DoesNotFlagSkew()
        {
            XElement relOp = CreateRelOp(
                new XElement(Ns + "RunTimeCountersPerThread",
                    new XAttribute("Thread", "1"),
                    new XAttribute("ActualRows", "90")),
                new XElement(Ns + "RunTimeCountersPerThread",
                    new XAttribute("Thread", "2"),
                    new XAttribute("ActualRows", "10")));

            PlanGraphRuntimeCountersResult result = _service.Parse(relOp, Ns);

            result.IsThreadDataSkewed.Should().BeFalse();
        }

        [Fact]
        public void Parse_WhenValuesAreInvalid_UsesExistingDefaults()
        {
            XElement relOp = CreateRelOp(
                new XElement(Ns + "RunTimeCountersPerThread",
                    new XAttribute("ActualRows", "not-a-number"),
                    new XAttribute("ActualRowsRead", "bad"),
                    new XAttribute("ActualExecutions", "bad")));

            PlanGraphRuntimeCountersResult result = _service.Parse(relOp, Ns);

            result.ActualRows.Should().Be(0);
            result.ActualRowsRead.Should().Be(0);
            result.ActualExecutions.Should().Be(1);
        }

        private static XElement CreateRelOp(params XElement[] runtimeCounters)
        {
            return new XElement(Ns + "RelOp",
                new XElement(Ns + "RunTimeInformation", runtimeCounters));
        }
    }
}
