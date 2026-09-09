using System.Text;

namespace SqlXmlAnalyzer.Application.Services;

public static class SqlSemanticValueReader
{
    public const int MaxCellLength = 65536;

    internal static void EnsureComparableType(string sqlTypeName)
    {
        // GetValue erases sql_variant's base type, precision/scale and collation.
        // Reject the column before reading rows, including empty/all-NULL results.
        if (sqlTypeName.Equals("sql_variant", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("UnsupportedSemanticType: sql_variant 的内部类型属性尚不能完整比较，禁止将该结果用于应用验证。");
    }

    public static async Task<string> ReadTextAsync(TextReader reader, CancellationToken token = default)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            if (result.Length + count > MaxCellLength) throw new InvalidDataException("单元格超过验证预算，不接受截断值。");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }

    public static async Task<byte[]> ReadBytesAsync(Stream stream, CancellationToken token = default)
    {
        using var result = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            if (result.Length + count > MaxCellLength) throw new InvalidDataException("单元格超过验证预算，不接受截断值。");
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }
}
