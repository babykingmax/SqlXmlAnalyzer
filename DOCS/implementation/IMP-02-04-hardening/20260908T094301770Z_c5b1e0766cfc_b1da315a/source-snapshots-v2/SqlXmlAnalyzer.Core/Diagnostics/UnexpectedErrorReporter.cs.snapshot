using System.Runtime.CompilerServices;
using System.Text.Json;

namespace SqlXmlAnalyzer.Core.Diagnostics;

public sealed record UnexpectedErrorReport(string? DumpPath, string? MetadataPath, string? Failure)
{
    public bool DumpCreated => DumpPath != null;
    public string Summary => DumpCreated ? $"DUMP：{DumpPath}；异常记录：{MetadataPath ?? "未生成"}" + (Failure == null ? "" : $"；{Failure}")
        : $"DUMP 生成失败：{Failure}；异常记录：{MetadataPath ?? "未生成"}";
}

public interface IUnexpectedErrorReporter
{
    UnexpectedErrorReport Report(Exception exception, string operation);
}

public interface ICrashDumpWriter
{
    void Write(string path);
}

/// <summary>Captures unexpected failures once per exception instance without hiding the original failure.</summary>
public sealed class UnexpectedErrorReporter : IUnexpectedErrorReporter
{
    public static IUnexpectedErrorReporter Shared { get; } = new UnexpectedErrorReporter(new WindowsMiniDumpWriter());
    private readonly ICrashDumpWriter _writer;
    private readonly string[] _directories;
    private readonly ConditionalWeakTable<Exception, UnexpectedErrorReport> _reports = new();
    private readonly object _gate = new();

    public UnexpectedErrorReporter(ICrashDumpWriter writer, string? directory = null)
    {
        _writer = writer;
        _directories = directory != null ? new[] { Path.GetFullPath(directory) } : new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SqlXmlAnalyzer", "dumps"),
            Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer", "dumps")
        };
    }

    public UnexpectedErrorReport Report(Exception exception, string operation)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_gate)
        {
            if (_reports.TryGetValue(exception, out var existing)) return existing;
            Logger.Critical($"未知异常：{operation}", exception);
            var errors = new List<string>();
            string? savedMetadata = null;
            foreach (string directory in _directories)
            {
                string? dump = null;
                try
                {
                    string incident = Path.Combine(directory,
                        $"{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}_{Environment.ProcessId}_{Guid.NewGuid():N}");
                    DiagnosticFileAccess.CreatePrivateDirectory(incident);
                    string path = Path.Combine(incident, "process.dmp");
                    // Preserve the managed stack before native capture can fail or time out.
                    string metadata = Path.Combine(incident, "exception.json");
                    using (var stream = new FileStream(metadata, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    {
                        JsonSerializer.Serialize(stream, new
                        {
                            TimestampUtc = DateTime.UtcNow, ProcessId = Environment.ProcessId,
                            Operation = operation, Exception = ExceptionPolicy.Details(exception), RequestedDumpPath = path,
                            DumpKind = "Windows minidump with thread information; managed exception stack is in this sidecar"
                        }, new JsonSerializerOptions { WriteIndented = true });
                        stream.Flush(flushToDisk: true);
                    }
                    savedMetadata = metadata;
                    _writer.Write(path);
                    MinidumpValidator.Validate(path);
                    dump = path;
                    return Remember(exception, new(dump, metadata, errors.Count == 0 ? null : string.Join("; ", errors)));
                }
                catch (Exception captureError)
                {
                    // Diagnostics must never replace the application's original failure.
                    errors.Add($"{captureError.GetType().Name}: {ExceptionPolicy.Message(captureError)}");
                    if (dump != null)
                        return Remember(exception, new(dump, savedMetadata, errors[^1]));
                }
            }
            return Remember(exception, new(null, savedMetadata, string.Join("; ", errors)));
        }
    }

    private UnexpectedErrorReport Remember(Exception exception, UnexpectedErrorReport report)
    {
        _reports.Add(exception, report);
        if (report.Failure != null) Logger.Error(report.Summary + "；" + report.Failure);
        else Logger.Critical(report.Summary);
        Logger.Flush();
        return report;
    }
}
