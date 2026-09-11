using System.Runtime.InteropServices;
using System.Text;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Privacy;

namespace SqlXmlAnalyzer.Core.Services;

public sealed record ReviewOutputResult(bool Succeeded, string Message);

/// <summary>Exports a reviewed candidate; never grants permission to apply or overwrites a file.</summary>
public sealed class ReviewOutputService(IUnexpectedErrorReporter? unexpectedErrors = null)
{
    public ReviewOutputResult Copy(string sql, Action<string> clipboard) => Run(() =>
    {
        RequireSql(sql);
        try { clipboard(sql); }
        catch (ExternalException exception) { throw new IOException("剪贴板暂时不可用，请重试。", exception); }
    }, "Copy", "SQL 已复制；本次操作仅复制文本。");

    public ReviewOutputResult SaveNew(string sql, string path) => Run(() =>
    {
        RequireSql(sql);
        if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetExtension(path), ".sql", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请指定一个新的 .sql 文件。");
        string destination;
        try { destination = Path.GetFullPath(path); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        { throw new InvalidDataException("候选 SQL 输出路径无效。", exception); }
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".tmp.review-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = new UTF8Encoding(false, true).GetBytes(sql);
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            // Only remove the unique temporary file owned by this export.
            try { File.Delete(temporary); }
            catch (Exception exception) { ExceptionPolicy.Describe(exception, "IMP21.ReviewOutput.Cleanup", unexpectedErrors); }
        }
    }, "SaveNew", "候选 SQL 已保存到新文件。");

    private ReviewOutputResult Run(Action action, string operation, string success)
    {
        try
        {
            action();
            Logger.Debug("IMP-21: 审核候选输出成功；" + operation);
            return new(true, success + Environment.NewLine + OutputPrivacy.RawNotice);
        }
        catch (Exception exception)
        {
            return new(false, ExceptionPolicy.Describe(exception, "IMP21.ReviewOutput." + operation, unexpectedErrors));
        }
    }

    private static void RequireSql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) throw new InvalidDataException("没有可输出的候选 SQL。");
    }
}
