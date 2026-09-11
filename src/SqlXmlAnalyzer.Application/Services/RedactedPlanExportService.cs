using System.Security.Cryptography;
using System.Text;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Privacy;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Application.Services;

public interface IRedactedPlanWriter
{
    void WriteNew(string path, byte[] bytes, CancellationToken cancellationToken);
}

public sealed class RedactedPlanWriter : IRedactedPlanWriter
{
    public void WriteNew(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        string output = Path.GetFullPath(path);
        if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Export target already exists.");
        string temporary = output + $".{Guid.NewGuid():N}.partial";
        bool owned = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                owned = true;
                for (int offset = 0; offset < bytes.Length; offset += 65536)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    stream.Write(bytes, offset, Math.Min(65536, bytes.Length - offset));
                }
                stream.Flush(true);
            }
            if (!SHA256.HashData(File.ReadAllBytes(temporary)).SequenceEqual(SHA256.HashData(bytes)))
                throw new IOException("Export verification failed.");
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, output); // Exclusive final publication; never overwrite an input or report.
            owned = false;
        }
        finally
        {
            if (owned)
            {
                try { File.Delete(temporary); }
                catch (Exception exception) { ExceptionPolicy.Describe(exception, "RedactedPlanExport.Cleanup"); }
            }
        }
    }
}

public sealed record RedactedPlanExportResult(bool IsSuccess, string Message, UnexpectedErrorReport? Diagnostic = null)
{
    public PlanRedactionResult? Redaction { get; init; }
}

public sealed class RedactedPlanExportService
{
    private readonly IRedactedPlanWriter _writer;
    private readonly IUnexpectedErrorReporter _diagnostics;
    public RedactedPlanExportService(IRedactedPlanWriter? writer = null, IUnexpectedErrorReporter? diagnostics = null)
    {
        _writer = writer ?? new RedactedPlanWriter();
        _diagnostics = diagnostics ?? UnexpectedErrorReporter.Shared;
    }

    public RedactedPlanExportResult ExportFile(string inputPath, string outputPath, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = File.OpenRead(inputPath);
            var document = SafeXmlHelper.LoadSafe(stream, new DocumentReadOptions(), cancellationToken);
            var result = new PlanRedactionService(_diagnostics).Redact(document, cancellationToken);
            return Export(result, outputPath, cancellationToken) with { Redaction = result };
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("RedactedPlanExport input cancelled.");
            return new(false, "脱敏导出已取消。");
        }
        catch (Exception exception)
        {
            var diagnostic = ExceptionPolicy.IsExpected(exception) || exception is ArgumentException or NotSupportedException ? null :
                ExceptionPolicy.Capture(exception, "RedactedPlanExport.Read", _diagnostics);
            Logger.Error("RedactedPlanExport could not read the input. No export payload was produced.");
            return new(false, "无法安全读取 XML，未生成脱敏副本。" + (diagnostic == null ? "" : diagnostic.Summary), diagnostic);
        }
    }

    public RedactedPlanExportResult Export(PlanRedactionResult result, string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.CanExport || result.Xml == null)
        {
            Logger.Warning("RedactedPlanExport blocked: no verified payload.");
            return new(false, result.Summary, result.Diagnostic);
        }
        try
        {
            Logger.Debug("RedactedPlanExport started.");
            _writer.WriteNew(outputPath, new UTF8Encoding(false, true).GetBytes(result.Xml), cancellationToken);
            Logger.Debug("RedactedPlanExport completed.");
            return new(true, result.Summary);
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("RedactedPlanExport cancelled.");
            return new(false, "脱敏导出已取消。");
        }
        catch (Exception exception)
        {
            var diagnostic = ExceptionPolicy.IsExpected(exception) || exception is ArgumentException or NotSupportedException ? null :
                ExceptionPolicy.Capture(exception, "RedactedPlanExport", _diagnostics);
            Logger.Error("RedactedPlanExport failed. Check target availability and permissions; existing files are never overwritten.");
            return new(false, "脱敏导出失败：请使用未存在的新文件名，并检查目录、占用和权限；原输入及既有文件不会被覆盖。若存储中断，请先核对目标状态。" +
                (diagnostic == null ? "" : " " + diagnostic.Summary), diagnostic);
        }
    }
}
