using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlXmlAnalyzer.Application;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Tests.Utilities;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class DocumentControllerTests
    {
        private static readonly XNamespace ShowplanNs =
            "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        [Fact]
        public async Task DeadlockDocumentController_AnalyzeAsync_ReturnsAnalysisResult()
        {
            string xml = EmbeddedResourceHelper.GetResourceContent(
                "deadlock_bookmark_lookup.xdl");
            var document = XDocument.Parse(xml);
            var controller = new DeadlockDocumentController(new DeadlockAnalysisService());

            DeadlockDocumentResult result = await controller.AnalyzeAsync(
                document,
                "deadlock.xdl");

            result.Document.Should().BeSameAs(document);
            result.FilePath.Should().Be("deadlock.xdl");
            result.Analysis.Processes.Should().HaveCount(2);
            result.Analysis.Resources.Should().HaveCount(2);
            result.Analysis.Graph.Should().NotBeNull();
        }

        [Fact]
        public async Task PlanDocumentController_AnalyzeAsync_ReturnsPlanAnalysisResult()
        {
            var document = XDocument.Parse(
                $$"""
                <ShowPlanXML xmlns="{{ShowplanNs}}">
                  <BatchSequence />
                </ShowPlanXML>
                """);
            var controller = new PlanDocumentController(CreatePlanAnalysisService());

            PlanDocumentResult result = await controller.AnalyzeAsync(
                document,
                "plan.sqlplan",
                ShowplanNs);

            result.Document.Should().BeSameAs(document);
            result.FilePath.Should().Be("plan.sqlplan");
            result.ShowplanNamespace.Should().Be(ShowplanNs);
            result.Analysis.DocumentText.Should().Contain("ShowPlanXML");
            result.Analysis.WarningsText.Should().NotBeNull();
        }

        private static PlanAnalysisService CreatePlanAnalysisService()
        {
            var orchestrator = new ApplicationOrchestrator(
                new EmptyAnalysisEngine(),
                new PassThroughRefactoringEngine(),
                new InMemoryFileHandler(),
                new NoopResultReporter(),
                NullLogger<ApplicationOrchestrator>.Instance);

            return new PlanAnalysisService(
                orchestrator,
                new InMemoryFileHandler(),
                new TemporaryFileManager());
        }

        private sealed class EmptyAnalysisEngine : IAnalysisEngine
        {
            public AnalysisReport Analyze(string xmlContent)
            {
                return new AnalysisReport(Array.Empty<IAnalysisIssue>());
            }
        }

        private sealed class PassThroughRefactoringEngine : IRefactoringEngine
        {
            public RefactorResult Run(
                string sql,
                AnalysisReport report,
                RefactorOptions options,
                bool isDryRun)
            {
                return new RefactorResult(
                    sql,
                    true,
                    Array.Empty<string>(),
                    new RefactorContext(sql));
            }
        }

        private sealed class InMemoryFileHandler : IFileHandler
        {
            private readonly Dictionary<string, string> _files = new();

            public string ReadAllText(string path)
            {
                return _files[path];
            }

            public void WriteAllText(string path, string contents)
            {
                _files[path] = contents;
            }

            public bool Exists(string path)
            {
                return _files.ContainsKey(path);
            }
        }

        private sealed class NoopResultReporter : IResultReporter
        {
            public void Report(RefactorResult result)
            {
            }

            public void Report(
                RefactorResult result,
                bool isDryRun,
                string? outputPath = null)
            {
            }
        }
    }
}
