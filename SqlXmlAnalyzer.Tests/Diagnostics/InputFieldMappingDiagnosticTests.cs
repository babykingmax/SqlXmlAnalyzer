using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class InputFieldMappingDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SqlXmlAnalyzer-Imp07-{Guid.NewGuid():N}");

    [Fact]
    public void UnknownMappingFailure_GeneratesValidatedDumpAndPreservesOriginalException()
    {
        var reporter = new UnexpectedErrorReporter(new WindowsMiniDumpWriter(), _directory);
        var error = new InvalidOperationException("IMP-07 synthetic field mapping failure");
        var builder = new PlanGraphNodeBuilderService(new ThrowingReader(error), reporter);
        Action build = () => builder.Build(InputFieldMappingTests.RelOp(), XNamespace.Get(
            "http://schemas.microsoft.com/sqlserver/2004/07/showplan"), new(10, 1000));
        build.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(error);
        var report = reporter.Report(error, "deduplication check");
        report.DumpCreated.Should().BeTrue(report.Failure);
        MinidumpValidator.Validate(report.DumpPath!);
        File.ReadAllText(report.MetadataPath!).Should().Contain("PlanGraphNodeBuilderService.Build")
            .And.Contain("ThrowingReader");
        Directory.GetDirectories(_directory).Should().ContainSingle();
    }

    [Fact]
    public void MappingLogs_RespectBuildConfigurationAndDoNotContainInputValues()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "mapping.log");
        Logger.Shutdown();
        try
        {
            Logger.Initialize(forceVerbose: true, logLevel: LogLevel.Debug, customLogFilePath: path);
            XElement relOp = InputFieldMappingTests.RelOp("<IndexScan Ordered='sensitive-invalid-value'/>");
            new PlanExecutionFactsService().Read(relOp, relOp.Name.Namespace);
            new PlanGraphCostUiActionService().ApplyCostCalculations(
                [relOp], new Dictionary<XElement, PlanNodeViewModel>
                {
                    [relOp] = new() { SubtreeCost = 1, EstRowsNum = 100, HasActualRows = false }
                }, relOp.Name.Namespace, DiagramViewMode.Rows, PlanColorMode.TotalCost);
            Logger.Error("imp07-error-marker");
            Logger.Critical("imp07-fatal-marker");
        }
        finally { Logger.Shutdown(); }
        string log = File.ReadAllText(path);
        log.Should().Contain("[ERROR]").And.Contain("[CRITICAL]").And.NotContain("sensitive-invalid-value");
#if DEBUG
        log.Should().Contain("[DEBUG]").And.Contain("[WARN]").And.Contain("Ordered")
            .And.Contain("成本重算完成；节点数=1，实际行数已知=0");
#else
        log.Should().NotContain("[DEBUG]").And.NotContain("[WARN]");
#endif
    }

    private sealed class ThrowingReader(Exception exception) : IPlanExecutionFactsReader
    {
        public PlanExecutionFacts Read(XElement relOp, XNamespace ns) => throw exception;
    }

    public void Dispose()
    {
        Logger.Shutdown();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
