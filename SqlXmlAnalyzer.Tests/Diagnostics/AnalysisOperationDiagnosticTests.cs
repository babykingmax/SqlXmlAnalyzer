using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class AnalysisOperationDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP26-" + Guid.NewGuid().ToString("N"));
    public AnalysisOperationDiagnosticTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownPreparationFailure_CapturesDumpOnceOrReportsWriterFailure(bool fails)
    {
        var writer = new Writer(fails); var reporter = new UnexpectedErrorReporter(writer, _directory);
        using var sessions = new AnalysisSessionCoordinator(reporter);
        var session = sessions.Begin(); sessions.Report(session.RequestId, AnalysisOperationState.PreparingView);
        var error = new InvalidOperationException("synthetic preparation failure");
        sessions.Fail(session.RequestId, error).Should().Contain("DUMP");
        sessions.Fail(session.RequestId, error).Should().Contain("DUMP");
        sessions.Progress.State.Should().Be(AnalysisOperationState.Failed);
        writer.Calls.Should().Be(1);
        var diagnostic = reporter.Report(error, "verify");
        if (fails) diagnostic.DumpCreated.Should().BeFalse();
        else MinidumpValidator.Validate(diagnostic.DumpPath!);
    }

    [Fact]
    public async Task UnknownCancellationCallback_IsObservedAndReported()
    {
        var writer = new Writer(true);
        var reporter = new SignallingReporter(new UnexpectedErrorReporter(writer, _directory));
        using var sessions = new AnalysisSessionCoordinator(reporter);
        var session = sessions.Begin();
        using var registration = session.Token.Register(() => throw new InvalidOperationException("synthetic cancel callback failure"));
        sessions.CancelCurrent();
        await reporter.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        writer.Calls.Should().Be(1);
        reporter.Operation.Should().Be("IMP26.CancelCallbacks");
        sessions.Progress.State.Should().Be(AnalysisOperationState.Cancelled);
    }

    [Fact]
    public void States_RespectBuildLogLevelsAndExpectedErrorsDoNotDump()
    {
        string path = Path.Combine(_directory, "states.log");
        var writer = new Writer(true);
        using var sessions = new AnalysisSessionCoordinator(new UnexpectedErrorReporter(writer, _directory));
        Logger.Shutdown(); Logger.Initialize(forceVerbose: true, logLevel: LogLevel.Debug, customLogFilePath: path);
        try
        {
            var first = sessions.Begin(); sessions.Report(first.RequestId, AnalysisOperationState.Partial);
            var second = sessions.Begin(); sessions.Fail(second.RequestId, new InvalidDataException("synthetic input rejection"));
            writer.Calls.Should().Be(0);
            var third = sessions.Begin(); sessions.Fail(third.RequestId, new InvalidOperationException("synthetic unknown failure"));
        }
        finally { Logger.Shutdown(); }
        string log = File.ReadAllText(path);
        log.Should().Contain("[ERROR]").And.Contain("[CRITICAL]");
#if DEBUG
        log.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
        log.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]");
#endif
    }

    private sealed class Writer(bool fails) : ICrashDumpWriter
    {
        public int Calls;
        public void Write(string path) { Calls++; if (fails) throw new IOException("synthetic writer failure"); new WindowsMiniDumpWriter().Write(path); }
    }
    private sealed class SignallingReporter(IUnexpectedErrorReporter inner) : IUnexpectedErrorReporter
    {
        public string? Operation;
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public UnexpectedErrorReport Report(Exception exception, string operation)
        {
            Operation = operation;
            var result = inner.Report(exception, operation); Completed.TrySetResult(); return result;
        }
    }
    public void Dispose() { Logger.Shutdown(); Directory.Delete(_directory, true); }
}
