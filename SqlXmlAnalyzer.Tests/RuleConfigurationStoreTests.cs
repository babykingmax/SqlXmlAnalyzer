using System.IO;
using System.Text;
using FluentAssertions;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Services;
using static SqlXmlAnalyzer.Tests.RuleConfigurationDocumentTests;

namespace SqlXmlAnalyzer.Tests;

public sealed class RuleConfigurationStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP24-Store-" + Guid.NewGuid().ToString("N"));
    public RuleConfigurationStoreTests() => Directory.CreateDirectory(_directory);
    private string Source => Path.Combine(_directory, "rules.json");
    private readonly FaultFiles _files = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Load_MalformedBomEncodedText_IsRejectedByEditorAndLegacyLoader(bool utf16)
    {
        byte[] bytes = utf16
            ? new UnicodeEncoding(false, true).GetPreamble().Concat(Encoding.Unicode.GetBytes("{\"x\":\"")).Concat(new byte[] { 0, 0xD8 }).Concat(Encoding.Unicode.GetBytes("\"}")).ToArray()
            : new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("{\"x\":\"")).Concat(new byte[] { 0xFF }).Concat(Encoding.UTF8.GetBytes("\"}")).ToArray();
        File.WriteAllBytes(Source, bytes);
        RuleConfigurationLoader.Load(Source).IsSuccess.Should().BeFalse();
        await ((Func<Task>)(() => new RuleConfigurationStore().LoadAsync(Source, default))).Should().ThrowAsync<DecoderFallbackException>();
        File.ReadAllBytes(Source).Should().Equal(bytes);
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16")]
    [InlineData("utf32")]
    public async Task Save_PreservesEncodingUnknownFieldsAndVerifiedBackup(string format)
    {
        Encoding encoding = format switch { "utf16" => new UnicodeEncoding(false, true, true), "utf32" => new UTF32Encoding(false, true, true), _ => new UTF8Encoding(true, true) };
        byte[] original = encoding.GetPreamble().Concat(encoding.GetBytes(CompatibleJson)).ToArray(); File.WriteAllBytes(Source, original);
        var store = new RuleConfigurationStore(); var file = await store.LoadAsync(Source, default);
        var edited = file.Document.WithSettings([new(Id, true, "Warning")]);
        var saved = await store.SaveAsync(file, edited, default);
        saved.Succeeded.Should().BeTrue(saved.Message); saved.SourceWritten.Should().BeTrue();
        File.ReadAllBytes(Source).Should().Equal(encoding.GetPreamble().Concat(encoding.GetBytes(edited.Json)));
        Directory.GetFiles(_directory, "*.bak").Should().ContainSingle();
        File.ReadAllBytes(Directory.GetFiles(_directory, "*.bak")[0]).Should().Equal(original);
        saved.File!.Document.UnknownRules.Should().ContainSingle();
        RuleConfigurationLoader.Load(Source).Document!.Fingerprint.Should().Be(edited.Fingerprint);
        // Saving again uses the newly committed snapshot, not the original file bytes.
        (await store.SaveAsync(saved.File, edited.WithSettings([new(Id, false, null)]), default)).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_ConcurrentEditBeforeOrDuringStaging_DoesNotOverwriteExternalContent(bool during)
    {
        File.WriteAllText(Source, CompatibleJson); var store = new RuleConfigurationStore(_files);
        var file = await store.LoadAsync(Source, default);
        const string external = "{\"Rules\":[],\"changedByOtherEditor\":true}";
        if (during) _files.OnFlush = () => File.WriteAllText(Source, external); else File.WriteAllText(Source, external);
        var result = await store.SaveAsync(file, file.Document.WithSettings([new(Id, true, null)]), default);
        result.Succeeded.Should().BeFalse(); result.SourceWritten.Should().BeFalse();
        File.ReadAllText(Source).Should().Be(external); _files.ReplaceCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_ReplacementFailure_ReportsActualCommitState(bool afterCommit)
    {
        File.WriteAllText(Source, CompatibleJson); var store = new RuleConfigurationStore(_files);
        var file = await store.LoadAsync(Source, default); var edited = file.Document.WithSettings([new(Id, true, "Warning")]);
        _files.FailReplace = true; _files.FailAfterReplace = afterCommit;
        var result = await store.SaveAsync(file, edited, default);
        result.Succeeded.Should().BeFalse(); result.SourceWritten.Should().Be(afterCommit);
        File.ReadAllText(Source).Should().Be(afterCommit ? edited.Json : CompatibleJson);
        if (afterCommit) result.Message.Should().Contain("写入已发生");
        File.ReadAllText(Directory.GetFiles(_directory, "*.bak").Single()).Should().Be(CompatibleJson);
    }

    [Fact]
    public async Task Save_CancelledAfterCommit_DoesNotReportAnUnwrittenFile()
    {
        File.WriteAllText(Source, CompatibleJson); using var cancellation = new CancellationTokenSource();
        var store = new RuleConfigurationStore(_files); var file = await store.LoadAsync(Source, default);
        _files.OnReplace = cancellation.Cancel;
        var result = await store.SaveAsync(file, file.Document.WithSettings([new(Id, true, null)]), cancellation.Token);
        result.SourceWritten.Should().BeTrue(); File.ReadAllText(Source).Should().NotBe(CompatibleJson);
    }

    [Fact]
    public async Task SaveAs_StagesNewFileAndNeverOverwritesExistingDestination()
    {
        var store = new RuleConfigurationStore(); var config = RuleConfigurationDocument.Parse(CompatibleJson);
        (await store.SaveAsAsync(Source, config, default)).Succeeded.Should().BeTrue();
        var repeat = () => store.SaveAsAsync(Source, RuleConfigurationDocument.Defaults, default);
        await repeat.Should().ThrowAsync<IOException>(); File.ReadAllText(Source).Should().Be(CompatibleJson);
        Directory.GetFiles(_directory, ".tmp.*").Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAndSaveAs_RejectOversizeMalformedAndCancelledOperations()
    {
        var store = new RuleConfigurationStore();
        File.WriteAllText(Source, new string('x', RuleConfigurationDocument.MaxBytes + 1));
        await ((Func<Task>)(() => store.LoadAsync(Source, default))).Should().ThrowAsync<InvalidDataException>();
        File.WriteAllBytes(Source, [0x7b, 0x22, 0x78, 0x22, 0x3a, 0x22, 0xff, 0x22, 0x7d]);
        await ((Func<Task>)(() => store.LoadAsync(Source, default))).Should().ThrowAsync<DecoderFallbackException>();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        string target = Path.Combine(_directory, "cancelled.json");
        await ((Func<Task>)(() => store.SaveAsAsync(target, RuleConfigurationDocument.Defaults, cancellation.Token))).Should().ThrowAsync<OperationCanceledException>();
        File.Exists(target).Should().BeFalse();
    }

    private sealed class FaultFiles : ISqlWritebackFileSystem
    {
        private readonly PhysicalSqlWritebackFileSystem _physical = new();
        public Action? OnFlush, OnReplace;
        public bool FailReplace, FailAfterReplace;
        public int ReplaceCalls;
        public byte[] ReadAllBytes(string path) => _physical.ReadAllBytes(path);
        public byte[] ReadAllBytes(string path, int maximumBytes, CancellationToken cancellationToken) => _physical.ReadAllBytes(path, maximumBytes, cancellationToken);
        public Stream CreateNew(string path, string sourcePath) => _physical.CreateNew(path, sourcePath);
        public void FlushToDisk(Stream stream) { _physical.FlushToDisk(stream); OnFlush?.Invoke(); }
        public void Replace(string temporaryPath, string sourcePath)
        {
            ReplaceCalls++;
            if (!FailReplace || FailAfterReplace) { _physical.Replace(temporaryPath, sourcePath); OnReplace?.Invoke(); }
            if (FailReplace) throw new IOException("synthetic replace failure");
        }
        public void Delete(string path) => _physical.Delete(path);
    }
    public void Dispose() => Directory.Delete(_directory, true);
}
