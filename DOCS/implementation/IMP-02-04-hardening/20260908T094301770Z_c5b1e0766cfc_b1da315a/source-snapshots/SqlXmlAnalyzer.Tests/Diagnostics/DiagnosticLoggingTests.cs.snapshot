using System.IO;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class DiagnosticLoggingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SqlXmlAnalyzer-Imp04Tests-{Guid.NewGuid():N}");
    public DiagnosticLoggingTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void Logger_EnforcesBuildPolicyForFileAndStderrWithoutPollutingStdout()
    {
        Logger.Shutdown();
        string path = Path.Combine(_directory, "mode.log");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var oldOut = Console.Out;
        var oldError = Console.Error;
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Logger.Initialize(forceVerbose: true, logLevel: SqlXmlAnalyzer.LogLevel.Debug, customLogFilePath: path);
            Logger.Debug("debug-marker");
            Logger.Info("legacy-info-marker");
            Logger.Warning("warning-marker");
            Logger.Error("error-marker");
            Logger.Critical("fatal-marker");
            Logger.Flush();
        }
        finally { Console.SetOut(oldOut); Console.SetError(oldError); Logger.Shutdown(); }
        stdout.ToString().Should().BeEmpty();
        foreach (string text in new[] { stderr.ToString(), File.ReadAllText(path) })
        {
            text.Should().Contain("[ERROR]").And.Contain("error-marker").And.Contain("[CRITICAL]").And.Contain("fatal-marker");
#if DEBUG
            text.Should().Contain("[DEBUG]").And.Contain("debug-marker").And.Contain("legacy-info-marker").And.Contain("[WARN]");
#else
            text.Should().NotContain("debug-marker").And.NotContain("legacy-info-marker").And.NotContain("warning-marker");
            text.Should().NotContain("[DEBUG]").And.NotContain("[INFO]").And.NotContain("[WARN]");
#endif
        }
    }

    [Fact]
    public void Provider_UsesSameFilePolicyForLayeredLogs()
    {
        Logger.Shutdown();
        string path = Path.Combine(_directory, "provider.log");
        Logger.Initialize(customLogFilePath: path);
        using var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace).AddProvider(new ApplicationLoggerProvider()));
        var logger = factory.CreateLogger("Imp04Test");
        logger.LogDebug("layer-debug");
        logger.LogWarning("layer-warning");
        logger.LogError("layer-error");
        logger.LogCritical("layer-critical");
        Logger.Flush();
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        string text = reader.ReadToEnd();
        text.Should().Contain("layer-error").And.Contain("layer-critical");
#if DEBUG
        text.Should().Contain("layer-debug").And.Contain("layer-warning");
#else
        text.Should().NotContain("layer-debug").And.NotContain("layer-warning");
#endif
    }

    [Fact]
    public void Logger_WhenFileSinkCannotBeCreated_ReportsErrorWithoutThrowing()
    {
        Logger.Shutdown();
        string blocker = Path.Combine(_directory, "not-a-directory");
        File.WriteAllText(blocker, "keep");
        var output = new StringWriter();
        var oldError = Console.Error;
        try
        {
            Console.SetError(output);
            Logger.Initialize(customLogFilePath: Path.Combine(blocker, "log.txt"));
            Logger.Error("original-operation-error");
        }
        finally { Console.SetError(oldError); }
        output.ToString().Should().Contain("日志写入失败").And.Contain("original-operation-error");
        File.ReadAllText(blocker).Should().Be("keep");
    }

    [Fact]
    public void Reporter_CreatesSidecarAndDeduplicatesSameExceptionAcrossBoundaries()
    {
        var writer = new TestDumpWriter();
        var reporter = new UnexpectedErrorReporter(writer, _directory);
        var exception = new InvalidOperationException("synthetic unknown failure");
        var first = reporter.Report(exception, "RuleBoundary");
        var second = reporter.Report(exception, "OrchestratorBoundary");
        first.DumpCreated.Should().BeTrue();
        second.Should().BeSameAs(first);
        writer.Calls.Should().Be(1);
        using var json = JsonDocument.Parse(File.ReadAllText(first.MetadataPath!));
        json.RootElement.GetProperty("Exception").GetString().Should().Contain("synthetic unknown failure");
        var other = reporter.Report(new InvalidOperationException("different failure"), "NextOperation");
        other.DumpPath.Should().NotBe(first.DumpPath);
        File.Exists(first.DumpPath).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reporter_WhenCaptureFailsOrHeaderIsInvalid_DoesNotClaimDumpWasCreated(bool invalidHeader)
    {
        var writer = new TestDumpWriter { InvalidHeader = invalidHeader, Failure = invalidHeader ? null : new IOException("disk full") };
        var reporter = new UnexpectedErrorReporter(writer, _directory);
        var result = reporter.Report(new InvalidOperationException("original failure"), "Synthetic");
        result.DumpCreated.Should().BeFalse();
        result.DumpPath.Should().BeNull();
        result.Summary.Should().Contain("DUMP 生成失败");
        result.Failure.Should().NotBeNullOrWhiteSpace();
        File.ReadAllText(result.MetadataPath!).Should().Contain("original failure");
    }

    [Fact]
    public void NativeWriter_OnWindows_CreatesReadableMinidumpHeaderAndPrivateIncidentDirectory()
    {
        var reporter = new UnexpectedErrorReporter(new WindowsMiniDumpWriter(), _directory);
        var result = reporter.Report(new InvalidOperationException("synthetic native dump verification"), "UnitNativeDump");
        result.DumpCreated.Should().BeTrue(result.Failure);
        using var reader = new BinaryReader(File.OpenRead(result.DumpPath!));
        reader.ReadUInt32().Should().Be(0x504D444D); // MDMP
        reader.ReadUInt32();
        reader.ReadUInt32().Should().BeGreaterThan(0); // Stream count
        if (OperatingSystem.IsWindows())
        {
            var access = new DirectoryInfo(Path.GetDirectoryName(result.DumpPath!)!).GetAccessControl();
            access.AreAccessRulesProtected.Should().BeTrue();
        }
    }

    [Fact]
    public void ExceptionPolicy_DoesNotDumpExpectedFailures()
    {
        ExceptionPolicy.IsExpected(new OperationCanceledException()).Should().BeTrue();
        ExceptionPolicy.IsExpected(new IOException()).Should().BeTrue();
        ExceptionPolicy.IsExpected(new UnauthorizedAccessException()).Should().BeTrue();
        ExceptionPolicy.IsExpected(new InvalidOperationException()).Should().BeFalse();
    }

    private sealed class TestDumpWriter : ICrashDumpWriter
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; init; }
        public bool InvalidHeader { get; init; }
        public void Write(string path)
        {
            Calls++;
            if (Failure != null) throw Failure;
            byte[] bytes = MinimalDump();
            if (InvalidHeader) bytes[0] = 0;
            File.WriteAllBytes(path, bytes);
        }
    }

    private static byte[] MinimalDump()
    {
        using var stream = new MemoryStream(new byte[48], writable: true);
        using var writer = new BinaryWriter(stream);
        writer.Write(0x504D444Du); writer.Write(0xa793u); writer.Write(1u); writer.Write(32u);
        stream.Position = 32;
        writer.Write(0xffffu); writer.Write(4u); writer.Write(44u);
        return stream.ToArray();
    }

    [Theory]
    [InlineData(8, 0u)]
    [InlineData(8, uint.MaxValue)]
    [InlineData(12, 49u)]
    [InlineData(36, uint.MaxValue)]
    [InlineData(40, 48u)]
    public void Validator_RejectsEmptyOrOutOfBoundsStreams(int offset, uint value)
    {
        var bytes = MinimalDump();
        BitConverter.GetBytes(value).CopyTo(bytes, offset);
        string path = Path.Combine(_directory, "invalid.dmp");
        File.WriteAllBytes(path, bytes);
        Action act = () => MinidumpValidator.Validate(path);
        act.Should().Throw<IOException>();
    }

    [Theory]
    [InlineData(SqlXmlAnalyzer.LogLevel.Critical)]
    [InlineData(SqlXmlAnalyzer.LogLevel.None)]
    public void Logger_LegacyThresholdCannotSuppressRequiredLevels(SqlXmlAnalyzer.LogLevel requested)
    {
        Logger.Shutdown();
        Logger.Initialize(logLevel: requested, enableFileLogging: false);
        Logger.IsEnabled(SqlXmlAnalyzer.LogLevel.Error).Should().BeTrue();
        Logger.IsEnabled(SqlXmlAnalyzer.LogLevel.Critical).Should().BeTrue();
        Logger.IsEnabled(SqlXmlAnalyzer.LogLevel.Debug).Should().Be(Logger.IsDebugMode);
        Logger.IsEnabled(SqlXmlAnalyzer.LogLevel.Warning).Should().Be(Logger.IsDebugMode);
    }

    [Fact]
    public void Logger_WhenExceptionFormattingThrows_KeepsOriginalErrorWithoutThrowing()
    {
        Logger.Shutdown();
        string path = Path.Combine(_directory, "formatting.log");
        Logger.Initialize(customLogFilePath: path);
        Logger.Error("primary message", new BrokenException());
        Logger.Shutdown();
        File.ReadAllText(path).Should().Contain("primary message").And.Contain("exception formatting failed");
    }

    private sealed class BrokenException : Exception
    {
        public override string Message => throw new InvalidOperationException("Message failed");
        public override string ToString() => throw new InvalidOperationException("ToString failed");
    }

    [Fact]
    public void Provider_WhenFormatterThrows_CapturesDiagnosticAndKeepsOriginalError()
    {
        Logger.Shutdown();
        string path = Path.Combine(_directory, "provider-formatter.log");
        Logger.Initialize(customLogFilePath: path);
        var diagnostics = new FormatterDiagnostics();
        using var provider = new ApplicationLoggerProvider(diagnostics);
        provider.CreateLogger("FormatterTest").Log(Microsoft.Extensions.Logging.LogLevel.Error, new EventId(1), "state",
            new IOException("primary operation failed"), (state, exception) => throw new InvalidOperationException("formatter failed"));
        Logger.Shutdown();
        diagnostics.Calls.Should().Be(1);
        File.ReadAllText(path).Should().Contain("日志格式化失败").And.Contain("primary operation failed");
    }

    private sealed class FormatterDiagnostics : IUnexpectedErrorReporter
    {
        public int Calls { get; private set; }
        public UnexpectedErrorReport Report(Exception exception, string operation)
        {
            Calls++;
            operation.Should().Be("LogFormatter");
            return new("synthetic.dmp", "synthetic.json", null);
        }
    }

    [Fact]
    public void NativeWriter_WhenTimedOut_DoesNotQueueAnotherWorkerOrPublishLateDump()
    {
        using var release = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        int captures = 0;
        var writer = new WindowsMiniDumpWriter(stream =>
        {
            Interlocked.Increment(ref captures);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            stream.Write(MinimalDump());
        }, TimeSpan.FromMilliseconds(200));
        string first = Path.Combine(_directory, "timed-out.dmp");
        string second = Path.Combine(_directory, "queued.dmp");
        try
        {
            Action act = () => writer.Write(first);
            act.Should().Throw<TimeoutException>();
            entered.IsSet.Should().BeTrue();
            Action next = () => writer.Write(second);
            next.Should().Throw<IOException>().WithMessage("*no additional worker*");
            captures.Should().Be(1);
        }
        finally
        {
            release.Set();
            SpinWait.SpinUntil(() => !File.Exists(first + ".partial"), TimeSpan.FromSeconds(5)).Should().BeTrue();
        }
        File.Exists(first).Should().BeFalse();
        File.Exists(second).Should().BeFalse();
    }

    public void Dispose()
    {
        Logger.Shutdown();
        string path = Path.GetFullPath(_directory);
        string prefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-Imp04Tests-");
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected cleanup path");
        Directory.Delete(path, recursive: true);
    }
}
