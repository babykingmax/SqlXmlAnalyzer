using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Refactoring;
using SqlXmlAnalyzer.Tests.Refactoring;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class IndexDdlDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP15-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownFailure_ValidatesRealDumpOrPreservesCaptureFailure(bool dumpFails)
    {
        var writer = new Writer(dumpFails);
        var reporter = new UnexpectedErrorReporter(writer, _directory);
        var error = new InvalidOperationException("synthetic-imp15-validator-failure");
        Action action = () => IndexDdlCompiler.Compile(IndexDdlIdentityTests.Suggestion(), new(), new FailingValidator(error), reporter);
        action.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(error);
        var incident = reporter.Report(error, "duplicate verification");
        writer.Calls.Should().Be(1);
        incident.DumpCreated.Should().Be(!dumpFails);
        if (!dumpFails) MinidumpValidator.Validate(incident.DumpPath!);
        else incident.Summary.Should().Contain("DUMP 生成失败");
        File.ReadAllText(incident.MetadataPath!).Should().Contain("IndexDdlCompiler.Compile").And.Contain("synthetic-imp15-validator-failure");
    }

    [Fact]
    public void ExpectedInputAndCancellation_DoNotGenerateDump()
    {
        var reporter = new Rules.DiagnosticProtocolTests.RecordingReporter();
        foreach (var error in new Exception[] { new IOException("expected"), new OperationCanceledException() })
        {
            Action action = () => IndexDdlCompiler.Compile(IndexDdlIdentityTests.Suggestion(), new(), new FailingValidator(error), reporter);
            action.Should().Throw<Exception>().Which.Should().BeSameAs(error);
        }
        Action invalid = () => IndexDdlCompiler.Compile(IndexDdlIdentityTests.Suggestion(), new() { DataCompression = "invalid" }, unexpectedErrors: reporter);
        invalid.Should().Throw<InvalidDataException>();
        reporter.Operations.Should().BeEmpty();
    }

    [Fact]
    public void Logs_EnforceBuildModesAndDoNotIncludeIdentifierOrDdlText()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "imp15.log");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;
        Logger.Shutdown();
        try
        {
            Console.SetOut(stdout); Console.SetError(stderr);
            Logger.Initialize(forceVerbose: true, logLevel: LogLevel.Debug, customLogFilePath: path);
            var suggestion = IndexDdlIdentityTests.Suggestion();
            suggestion.Table = "secret-imp15-table";
            IndexDdlCompiler.Compile(suggestion, new());
            var doc = SafeXmlHelper.ParseSafe(EmbeddedResourceHelper.GetResourceContent("imp15_review_actual.sqlplan"));
            var model = Core.Services.PlanIdentityAdapter.GetDocument(doc)!;
            var op = model.Operators.First(o => o.Objects.Count == 1);
            var evidenceSuggestion = new Core.Models.MissingIndexSuggestion { ObjectIdentity = op.Objects[0].Identity, Location = op.Location };
            Core.Services.IndexTargetResolver.FindColumns(evidenceSuggestion, doc, doc.Root!.Name.Namespace).ToArray();
            Core.Services.IndexTargetResolver.FindSortOrders(evidenceSuggestion, doc, doc.Root.Name.Namespace);
            Action fault = () => IndexDdlCompiler.Compile(suggestion, new(), new FailingValidator(new InvalidOperationException("synthetic-imp15-fault")),
                new UnexpectedErrorReporter(new Writer(true), _directory));
            fault.Should().Throw<InvalidOperationException>();
            suggestion.KeyColumns.Clear();
            IndexDdlCompiler.Compile(suggestion, new());
        }
        finally { Logger.Shutdown(); Console.SetOut(previousOut); Console.SetError(previousError); }
        stdout.ToString().Should().BeEmpty();
        foreach (string log in new[] { File.ReadAllText(path), stderr.ToString() })
        {
            log.Should().Contain("[ERROR]").And.Contain("[CRITICAL]").And.NotContain("secret-imp15-table").And.NotContain("CREATE NONCLUSTERED");
            log.Should().NotContain("imp15review").And.NotContain("@real").And.NotContain("[literal]");
#if DEBUG
            log.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
            log.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]");
#endif
        }
    }

    private sealed class FailingValidator(Exception error) : IIndexDdlValidator
    {
        public void Validate(string sql) => throw error;
    }
    private sealed class Writer(bool fail) : ICrashDumpWriter
    {
        public int Calls { get; private set; }
        public void Write(string path)
        {
            Calls++;
            if (fail) throw new IOException("synthetic dump writer failure");
            new WindowsMiniDumpWriter().Write(path);
        }
    }
    public void Dispose() { Logger.Shutdown(); if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}
