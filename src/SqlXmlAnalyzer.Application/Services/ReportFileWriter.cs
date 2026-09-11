namespace SqlXmlAnalyzer.Application.Services;

/// <summary>Stage a complete report beside its destination, then recheck every input before publication.</summary>
public static class ReportFileWriter
{
    public static void WriteText(string path, string text, IEnumerable<string?> inputPaths,
        CancellationToken cancellationToken = default) =>
        Write(path, staged => File.WriteAllText(staged, text, new System.Text.UTF8Encoding(true)), inputPaths, cancellationToken);

    public static void Write(string path, Action<string> writeReport, IEnumerable<string?> inputPaths,
        CancellationToken cancellationToken = default)
    {
        string output = Path.GetFullPath(path);
        string?[] inputs = inputPaths.ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        SqlReportPathGuard.ValidateInputs(inputs, output);
        // Preserve the extension for exporters that select a format from the filename.
        string temporary = Path.Combine(Path.GetDirectoryName(output)!,
            $".tmp.report-{Guid.NewGuid():N}{Path.GetExtension(output)}");
        bool owned = false;
        try
        {
            using (new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) owned = true;
            writeReport(temporary);
            using (var completed = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                completed.Flush(flushToDisk: true);
            cancellationToken.ThrowIfCancellationRequested();
            SqlReportPathGuard.ValidateInputs(inputs, output);
            File.Move(temporary, output, overwrite: true);
            owned = false;
        }
        finally
        {
            if (owned)
            {
                try { File.Delete(temporary); }
                catch (Exception exception) { Core.Diagnostics.ExceptionPolicy.Describe(exception, "ReportFileWriter.Cleanup"); }
            }
        }
    }
}
