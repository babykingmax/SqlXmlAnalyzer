namespace SqlXmlAnalyzer.Application.Services;

internal static class SqlBoundedFileReader
{
    internal static byte[] Read(Stream stream, int maxBytes, CancellationToken token)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        token.ThrowIfCancellationRequested();
        if (stream.CanSeek && stream.Length - stream.Position > maxBytes) throw Exceeded();
        using var result = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            // Read at most one byte past the limit; length is only an early rejection,
            // never the security boundary (short reads and growing streams are possible).
            int requested = (int)Math.Min(buffer.Length, (long)maxBytes - result.Length + 1);
            int count = stream.Read(buffer, 0, requested);
            token.ThrowIfCancellationRequested();
            if (count == 0) return result.ToArray();
            if (count > maxBytes - result.Length) throw Exceeded();
            result.Write(buffer, 0, count);
        }
    }

    internal static InvalidDataException Exceeded() => new("SqlInputBudget: SQL 文件超过读取预算，已停止读取。");
}
