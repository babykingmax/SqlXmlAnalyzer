using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests.Diagnostics;

public sealed class RuleConfigurationDiagnosticTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP24-Diagnostics-" + Guid.NewGuid().ToString("N"));
    public RuleConfigurationDiagnosticTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownFailure_CreatesValidatedNativeDumpOrExplainsFailureAndDeduplicates(bool fails)
    {
        var writer = new DumpWriter(fails); var reporter = new UnexpectedErrorReporter(writer, _directory);
        var store = new RuleConfigurationViewModelTests.Store();
        using var model = new RuleConfigurationViewModel(new(RuleConfigurationDocument.Defaults), store: store, reporter: reporter);
        await model.InitializeAsync(); var previous = model.Draft;
        var failure = new InvalidOperationException("synthetic IMP24 load failure"); store.Failure = failure;
        await model.LoadAsync("bad.json"); await model.LoadAsync("bad.json");
        model.Draft.Should().BeSameAs(previous); model.Status.Should().Contain("synthetic IMP24 load failure").And.Contain("DUMP");
        writer.Calls.Should().Be(1); var incident = reporter.Report(failure, "deduplicate");
        File.ReadAllText(incident.MetadataPath!).Should().Contain("IMP24.Configuration.Operation");
        if (fails) model.Status.Should().Contain("DUMP 生成失败");
        else { incident.DumpCreated.Should().BeTrue(); MinidumpValidator.Validate(incident.DumpPath!); }
    }

    [Fact]
    public async Task ApplyCallbackFailureAndBrokenReporter_DoNotMisreportCommittedConfiguration()
    {
        var session = new RuleConfigurationSession(RuleConfigurationDocument.Defaults);
        using var model = new RuleConfigurationViewModel(session, _ => throw new InvalidOperationException("synthetic refresh failure"),
            store: new RuleConfigurationViewModelTests.Store(), reporter: new BrokenReporter());
        await model.InitializeAsync(); model.Apply();
        session.Capture().Should().BeSameAs(model.Draft); model.Status.Should().Contain("配置已应用").And.Contain("synthetic refresh failure").And.Contain("诊断组件失败");
    }

    [Theory]
    [InlineData("{\"Rules\":[{\"RuleId\":\"\\uD800\"}]}")]
    [InlineData("{\"extensions\":{\"\\uDC00\":0}}")]
    [InlineData("{\"extensions\":[\"\\uD800\"]}")]
    public async Task InvalidUnicode_LoadAndEditorRejectWithoutDumpOrChangingDraft(string json)
    {
        string path = Path.Combine(_directory, "invalid-unicode.json");
        File.WriteAllText(path, json);
        var writer = new DumpWriter(true);
        var reporter = new UnexpectedErrorReporter(writer, _directory);
        var loaded = RuleConfigurationLoader.Load(path, reporter);
        loaded.IsSuccess.Should().BeFalse();
        loaded.Errors.Should().Contain(error => error.Contains("Unicode"));
        using var model = new RuleConfigurationViewModel(new(RuleConfigurationDocument.Defaults),
            store: new RuleConfigurationStore(reporter: reporter), reporter: reporter);
        await model.InitializeAsync();
        var previous = model.Draft;
        await model.LoadAsync(path);
        model.Draft.Should().BeSameAs(previous);
        model.Status.Should().Contain("Unicode").And.NotContain("DUMP");
        writer.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Logs_RespectBuildLevelsAndDoNotContainUnknownJsonPayload()
    {
        var output = new StringWriter(); var error = new StringWriter(); var oldOutput = Console.Out; var oldError = Console.Error;
        string logPath = Path.Combine(_directory, "configuration.log"); Logger.Shutdown();
        try
        {
            Console.SetOut(output); Console.SetError(error); Logger.Initialize(forceVerbose: true, logLevel: LogLevel.Debug, customLogFilePath: logPath);
            string path = Path.Combine(_directory, "rules.json"); File.WriteAllText(path, RuleConfigurationDocumentTests.CompatibleJson);
            await new RuleConfigurationStore().LoadAsync(path, default);
            var store = new RuleConfigurationViewModelTests.Store();
            using var model = new RuleConfigurationViewModel(new(RuleConfigurationDocument.Defaults), store: store,
                reporter: new UnexpectedErrorReporter(new DumpWriter(true), _directory));
            await model.InitializeAsync(); model.Rows[0].SeveritySelection = "INVALID";
            store.Failure = new InvalidOperationException("synthetic unknown configuration failure"); await model.LoadAsync("other.json");
        }
        finally { Logger.Shutdown(); Console.SetOut(oldOutput); Console.SetError(oldError); }
        output.ToString().Should().BeEmpty();
        foreach (string log in new[] { error.ToString(), File.ReadAllText(logPath) })
        {
            log.Should().Contain("[ERROR]").And.Contain("[CRITICAL]").And.NotContain("private-config-marker").And.NotContain("future-mode");
#if DEBUG
            log.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
            log.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]");
#endif
        }
    }

    private sealed class DumpWriter(bool fails) : ICrashDumpWriter
    {
        public int Calls;
        public void Write(string path) { Calls++; if (fails) throw new IOException("synthetic DUMP failure"); new WindowsMiniDumpWriter().Write(path); }
    }
    private sealed class BrokenReporter : IUnexpectedErrorReporter
    { public UnexpectedErrorReport Report(Exception exception, string operation) => throw new IOException("synthetic reporter failure"); }
    public void Dispose() { Logger.Shutdown(); Directory.Delete(_directory, true); }
}
