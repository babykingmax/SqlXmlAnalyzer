using System.IO;
using System.Runtime.InteropServices;
using FluentAssertions;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.CLI;

namespace SqlXmlAnalyzer.Tests.Application;

public sealed class CliInputProtectionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-CliProtection-" + Guid.NewGuid().ToString("N"));
    public CliInputProtectionTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData("pdf", false)]
    [InlineData("docx", false)]
    [InlineData("txt", false)]
    [InlineData("pdf", true)]
    [InlineData("docx", true)]
    [InlineData("txt", true)]
    public void DesktopExport_RejectsSourceAndHardLinkBeforeInvokingExporter(string format, bool hardLink)
    {
        string source = Write("plan.sqlplan", "<synthetic-input/>");
        string output = hardLink ? Path.Combine(_directory, "report." + format) : Path.Combine(_directory, ".", "plan.sqlplan");
        if (hardLink) CreateHardLink(output, source, IntPtr.Zero).Should().BeTrue();
        Action export = () => Core.Services.CliService.RunAnalysis(source, format, output);
        export.Should().Throw<IOException>();
        File.ReadAllText(source).Should().Be("<synthetic-input/>");
        Directory.GetFiles(_directory, ".tmp.report-*").Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RefactorReport_ProtectsPlanOnSuccessAndFailureIncludingHardLinks(bool missingSql, bool hardLink)
    {
        string sql = Path.Combine(_directory, "query.sql");
        if (!missingSql) File.WriteAllText(sql, "SELECT 1;");
        string plan = Write("plan.sqlplan", "<ShowPlanXML/>");
        string output = hardLink ? Path.Combine(_directory, "report.json") : plan;
        if (hardLink) CreateHardLink(output, plan, IntPtr.Zero).Should().BeTrue();
        Program.Main(["refactor", sql, "--plan", plan, "--format", "json", "--output", output]).Should().Be(2);
        File.ReadAllText(plan).Should().Be("<ShowPlanXML/>");
    }

    [Theory]
    [InlineData("--config", false)]
    [InlineData("--config", true)]
    [InlineData("--read-options", false)]
    [InlineData("--read-options", true)]
    public void RefactorReport_ProtectsAuxiliaryConfigurationOnSuccessAndFailure(string option, bool missingSql)
    {
        string sql = Path.Combine(_directory, "query.sql");
        if (!missingSql) File.WriteAllText(sql, "SELECT 1;");
        string configuration = Write("options.json", "{}");
        Program.Main(["refactor", sql, option, configuration, "--format", "json", "--output", configuration]).Should().Be(2);
        File.ReadAllText(configuration).Should().Be("{}");
    }

    [Fact]
    public void DesktopBatchExport_ProtectsOtherInputsAsWellAsCurrentInput()
    {
        string source = Write("one.sqlplan", "first-input");
        string other = Write("two.sqlplan", "second-input");
        Action export = () => Core.Services.CliService.RunAnalysis(source, "txt", other, protectedInputPaths: [source, other]);
        export.Should().Throw<IOException>();
        File.ReadAllText(other).Should().Be("second-input");
    }

    [Fact]
    public void ReportWriter_RechecksAliasesCreatedDuringReportGeneration()
    {
        string source = Write("input.sqlplan", "source-bytes");
        string output = Path.Combine(_directory, "report.txt");
        Action write = () => ReportFileWriter.Write(output, temporary =>
        {
            File.WriteAllText(temporary, "complete-report");
            CreateHardLink(output, source, IntPtr.Zero).Should().BeTrue();
        }, [source]);
        write.Should().Throw<IOException>();
        File.ReadAllText(source).Should().Be("source-bytes");
        File.ReadAllText(output).Should().Be("source-bytes");
        Directory.GetFiles(_directory, ".tmp.report-*").Should().BeEmpty();
    }

    [Fact]
    public void ReportWriter_PreservesExistingReportWhenExporterFailsOrOperationIsCanceled()
    {
        string output = Write("report.txt", "previous-report");
        Action fail = () => ReportFileWriter.Write(output, temporary =>
        {
            File.WriteAllText(temporary, "partial-report");
            throw new IOException("export interrupted");
        }, []);
        fail.Should().Throw<IOException>();
        using var canceled = new CancellationTokenSource();
        Action cancel = () => ReportFileWriter.Write(output, temporary =>
        {
            File.WriteAllText(temporary, "candidate-report");
            canceled.Cancel();
        }, [], canceled.Token);
        cancel.Should().Throw<OperationCanceledException>();
        File.ReadAllText(output).Should().Be("previous-report");
        Directory.GetFiles(_directory, ".tmp.report-*").Should().BeEmpty();
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("docx")]
    public void ReportWriter_PublishesCompletePortableReportOverExistingReport(string format)
    {
        string output = Write("report." + format, "previous-report");
        ReportFileWriter.Write(output, temporary =>
        {
            if (format == "pdf") Core.Services.ReportExportService.ExportToPdf(temporary, "Report", "Synthetic report");
            else Core.Services.ReportExportService.ExportToWord(temporary, "Report", "Synthetic report");
        }, []);
        if (format == "pdf") System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(output)[..5]).Should().Be("%PDF-");
        else
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(output);
            using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
            reader.ReadToEnd().Should().Contain("Synthetic report");
        }
        Directory.GetFiles(_directory, ".tmp.report-*").Should().BeEmpty();
    }

    private string Write(string name, string text)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

    public void Dispose()
    {
        string full = Path.GetFullPath(_directory);
        if (!full.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-CliProtection-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected cleanup path");
        Directory.Delete(full, true);
    }
}
