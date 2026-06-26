using System;
using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class TuningSessionServiceTests
    {
        private static readonly XNamespace ShowplanNs =
            "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        [Fact]
        public void CaptureSnapshot_ExtractsPlanMetadata()
        {
            var service = new TuningSessionService();
            XDocument document = CreatePlanDocument(
                "SELECT * FROM dbo.Customer",
                rootCost: 12.5,
                includeMissingIndex: true);

            PlanSnapshot snapshot = service.CaptureSnapshot(
                document,
                "C:\\plans\\customer.sqlplan",
                3,
                new DateTime(2026, 6, 26, 10, 0, 0, DateTimeKind.Utc));

            snapshot.Title.Should().Be("Plan version #3 - customer.sqlplan");
            snapshot.FilePath.Should().Be("C:\\plans\\customer.sqlplan");
            snapshot.TotalCost.Should().Be(12.5);
            snapshot.OperatorCount.Should().Be(1);
            snapshot.MissingIndexCount.Should().Be(1);
            snapshot.StatementText.Should().Be("SELECT * FROM dbo.Customer");
            snapshot.Document.Should().NotBeSameAs(document);
            snapshot.Document.Root.Should().NotBeNull();
        }

        [Fact]
        public void SaveAndLoad_RoundtripsSnapshotsAndComparisonSelection()
        {
            string tempFile = Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.pesession");
            var service = new TuningSessionService();
            PlanSnapshot planA = service.CaptureSnapshot(
                CreatePlanDocument("SELECT 1", rootCost: 1, includeMissingIndex: false),
                "a.sqlplan",
                1);
            PlanSnapshot planB = service.CaptureSnapshot(
                CreatePlanDocument("SELECT 2", rootCost: 0.5, includeMissingIndex: true),
                "b.sqlplan",
                2);

            try
            {
                service.Save(
                    tempFile,
                    new[] { planA, planB },
                    planA,
                    planB);

                TuningSessionLoadResult result = service.Load(tempFile);

                result.Snapshots.Should().HaveCount(2);
                result.PlanA.Should().NotBeNull();
                result.PlanB.Should().NotBeNull();
                result.PlanA!.Title.Should().Be(planA.Title);
                result.PlanB!.Title.Should().Be(planB.Title);
                result.Snapshots[1].TotalCost.Should().Be(0.5);
                result.Snapshots[1].MissingIndexCount.Should().Be(1);
                result.Snapshots[1].Document.Root!.Name.Should().Be(ShowplanNs + "ShowPlanXML");
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Fact]
        public void Load_WhenSessionHasNoSnapshots_ReturnsEmptyResult()
        {
            string tempFile = Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid():N}.pesession");

            try
            {
                File.WriteAllText(
                    tempFile,
                    """
                    <TuningSession xmlns="http://schemas.sqlxmlanalyzer.com/session" Version="2.0" />
                    """);

                var service = new TuningSessionService();

                TuningSessionLoadResult result = service.Load(tempFile);

                result.Snapshots.Should().BeEmpty();
                result.PlanA.Should().BeNull();
                result.PlanB.Should().BeNull();
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        private static XDocument CreatePlanDocument(
            string statementText,
            double rootCost,
            bool includeMissingIndex)
        {
            var queryPlan = new XElement(
                ShowplanNs + "QueryPlan",
                new XElement(
                    ShowplanNs + "RelOp",
                    new XAttribute("NodeId", "0"),
                    new XAttribute("PhysicalOp", "Index Scan"),
                    new XAttribute("EstimatedTotalSubtreeCost", rootCost)));

            if (includeMissingIndex)
            {
                queryPlan.Add(
                    new XElement(
                        ShowplanNs + "MissingIndexes",
                        new XElement(
                            ShowplanNs + "MissingIndexGroup",
                            new XAttribute("Impact", "75"),
                            new XElement(
                                ShowplanNs + "MissingIndex",
                                new XAttribute("Database", "[db]"),
                                new XAttribute("Schema", "[dbo]"),
                                new XAttribute("Table", "[Customer]")))));
            }

            return new XDocument(
                new XElement(
                    ShowplanNs + "ShowPlanXML",
                    new XElement(
                        ShowplanNs + "BatchSequence",
                        new XElement(
                            ShowplanNs + "Batch",
                            new XElement(
                                ShowplanNs + "Statements",
                                new XElement(
                                    ShowplanNs + "StmtSimple",
                                    new XAttribute("StatementText", statementText),
                                    queryPlan))))));
        }
    }
}
