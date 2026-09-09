using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class MisleadingDisplayDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SqlXmlAnalyzer-Imp08-{Guid.NewGuid():N}");

    [Fact]
    public void Comparison_UnknownFailure_WritesValidatedDumpAndRethrowsOriginal()
    {
        var reporter = new UnexpectedErrorReporter(new WindowsMiniDumpWriter(), _directory);
        var error = new InvalidOperationException("synthetic comparison reader fault");
        var controller = new PlanComparisonController(new ThrowingReader(error), reporter);
        Action compare = () => controller.BuildComparison(MisleadingDisplayTests.Snapshot(""), null, XNamespace.None);

        compare.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(error);
        VerifyDump(reporter.Report(error, "duplicate"), "PlanComparisonController.BuildComparison");
    }

    [Fact]
    public void Sandbox_UnknownNotificationFailure_PreservesOriginalAndClearsStaleDdl()
    {
        var reporter = new UnexpectedErrorReporter(new WindowsMiniDumpWriter(), _directory);
        var vm = new IndexSandboxViewModel(MisleadingDisplayTests.Suggestion(), unexpectedErrors: reporter);
        vm.CreateIndexStatement.Should().NotBeEmpty();
        var error = new InvalidOperationException("synthetic display notification fault");
        vm.PropertyChanged += (_, _) => throw error;

        Action recalculate = vm.Recalculate;
        recalculate.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(error);
        vm.CreateIndexStatement.Should().BeEmpty();
        vm.CurrentScore.Should().Be(0);
        vm.CostReductionDescription.Should().Contain("失败");
        VerifyDump(reporter.Report(error, "duplicate"), "IndexSandboxViewModel.Recalculate");
    }

    [Fact]
    public void DisplayLogs_RespectDebugReleasePolicyAndOmitRawInput()
    {
        Directory.CreateDirectory(_directory);
        string logPath = Path.Combine(_directory, "display.log");
        Logger.Shutdown();
        try
        {
            Logger.Initialize(forceVerbose: true, logLevel: LogLevel.Debug, customLogFilePath: logPath);
            new PlanComparisonController().BuildComparison(
                MisleadingDisplayTests.Snapshot("<RunTimeCountersPerThread ActualElapsedms='private-invalid-value'/>"),
                MisleadingDisplayTests.Snapshot(""), XNamespace.None);
            new IndexSandboxViewModel(MisleadingDisplayTests.Suggestion()).ReturnedRows = double.NaN;
            Logger.Error("imp08-error");
            Logger.Critical("imp08-fatal");
        }
        finally { Logger.Shutdown(); }

        string log = File.ReadAllText(logPath);
        log.Should().Contain("[ERROR]").And.Contain("[CRITICAL]").And.NotContain("private-invalid-value");
#if DEBUG
        log.Should().Contain("[DEBUG]").And.Contain("[WARN]").And.Contain("比较展示已生成").And.Contain("假设输入无效");
#else
        log.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]");
#endif
    }

    private void VerifyDump(UnexpectedErrorReport report, string operation)
    {
        report.DumpCreated.Should().BeTrue(report.Failure);
        MinidumpValidator.Validate(report.DumpPath!);
        File.ReadAllText(report.MetadataPath!).Should().Contain(operation);
        Directory.GetDirectories(_directory).Should().ContainSingle();
    }

    private sealed class ThrowingReader(Exception error) : IPlanComparisonRuntimeMetricsReader
    {
        public PlanComparisonRuntimeMetrics Read(XElement relOp) => throw error;
    }

    public void Dispose()
    {
        Logger.Shutdown();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
