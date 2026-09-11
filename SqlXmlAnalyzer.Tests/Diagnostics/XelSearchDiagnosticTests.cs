using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class XelSearchDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP23-Diagnostics-" + Guid.NewGuid().ToString("N"));
    public XelSearchDiagnosticTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownFailure_WritesValidatedNativeDumpOrExplainsCaptureFailure(bool dumpFails)
    {
        var writer = new DumpWriter(dumpFails); var reporter = new UnexpectedErrorReporter(writer, _directory);
        var service = new XelSearchViewModelTests.Service();
        using var model = new XelSearchViewModel(_ => Task.CompletedTask, service, reporter);
        await model.InitializeAsync(null); var previous = model.Events;
        var failure = new InvalidOperationException("synthetic IMP23 load failure"); service.Failure = failure;
        await model.ImportAsync(["next.xel"]); await model.ImportAsync(["next.xel"]);
        model.Events.Should().BeSameAs(previous); model.Status.Should().Contain("synthetic IMP23 load failure").And.Contain("DUMP");
        writer.Calls.Should().Be(1);
        var incident = reporter.Report(failure, "deduplicate");
        File.ReadAllText(incident.MetadataPath!).Should().Contain("IMP23.XelSearch");
        if (dumpFails) model.Status.Should().Contain("DUMP 生成失败");
        else { incident.DumpCreated.Should().BeTrue(); MinidumpValidator.Validate(incident.DumpPath!); }
    }

    [Fact]
    public async Task BrokenReporter_PreservesNavigationFailure()
    {
        using var model = new XelSearchViewModel(_ => throw new InvalidOperationException("primary navigation failure"), new XelSearchViewModelTests.Service(), new BrokenReporter());
        await model.InitializeAsync(null); model.SelectedEvent = model.Events[0]; await model.NavigateAsync();
        model.Status.Should().Contain("primary navigation failure").And.Contain("诊断组件失败");
    }

    [Fact]
    public async Task Logs_UseRequiredBuildLevelsAndDoNotContainQueryOrSourcePayload()
    {
        var output = new StringWriter(); var error = new StringWriter(); var oldOutput = Console.Out; var oldError = Console.Error;
        string path = Path.Combine(_directory, "xel.log"); Logger.Shutdown();
        try
        {
            Console.SetOut(output); Console.SetError(error); Logger.Initialize(forceVerbose: true, logLevel: LogLevel.Debug, customLogFilePath: path);
            var service = new XelSearchViewModelTests.Service();
            using var model = new XelSearchViewModel(_ => Task.CompletedTask, service, new UnexpectedErrorReporter(new DumpWriter(true), _directory));
            await model.InitializeAsync(null); model.From = "invalid"; await model.SearchAsync(); model.From = "";
            service.BlockLoad = true; var pending = model.ImportAsync(["sensitive-source.xel"]); await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            model.Cancel(); service.Release.TrySetResult(); await pending;
            service.BlockLoad = false; service.Failure = new InvalidOperationException("synthetic unknown failure"); await model.ImportAsync(["sensitive-source.xel"]);
        }
        finally { Logger.Shutdown(); Console.SetOut(oldOutput); Console.SetError(oldError); }
        output.ToString().Should().BeEmpty();
        foreach (string log in new[] { error.ToString(), File.ReadAllText(path) })
        {
            log.Should().Contain("[ERROR]").And.Contain("[CRITICAL]").And.NotContain("search-marker").And.NotContain("Sales.dbo.Orders").And.NotContain("sensitive-source.xel");
#if DEBUG
            log.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
            log.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]");
#endif
        }
    }

    private sealed class DumpWriter(bool fails) : ICrashDumpWriter
    {
        public int Calls { get; private set; }
        public void Write(string path) { Calls++; if (fails) throw new IOException("synthetic DUMP failure"); new WindowsMiniDumpWriter().Write(path); }
    }
    private sealed class BrokenReporter : IUnexpectedErrorReporter
    {
        public UnexpectedErrorReport Report(Exception exception, string operation) => throw new IOException("synthetic reporter failure");
    }
    public void Dispose()
    {
        Logger.Shutdown();
        if (!Path.GetFullPath(_directory).StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-IMP23-Diagnostics-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected cleanup path");
        Directory.Delete(_directory, true);
    }
}
