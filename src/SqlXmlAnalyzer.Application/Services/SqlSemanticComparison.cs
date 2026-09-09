using System.Globalization;
using System.Text.Json;
using SqlXmlAnalyzer.Application.Models;

namespace SqlXmlAnalyzer.Application.Services;

public static class SqlSemanticComparison
{
    // Every row is length/escape delimited JSON; retain type tags, NULLs and duplicates.
    public static string EncodeRow(IEnumerable<object?> values) => JsonSerializer.Serialize(values.Select(value => value switch
    {
        null or DBNull => new[] { "NULL", "" },
        string text => EncodeString(text),
        byte[] bytes => new[] { "binary", Convert.ToBase64String(bytes) },
        System.Data.SqlTypes.SqlDecimal number => new[] { "decimal", number.IsPositive + ":" +
            number.Scale.ToString(CultureInfo.InvariantCulture) + ":" + string.Join(",", number.Data.Select(d => d.ToString(CultureInfo.InvariantCulture))) },
        DateTime date => new[] { "datetime", date.ToString("O", CultureInfo.InvariantCulture) },
        DateTimeOffset date => new[] { "datetimeoffset", date.ToString("O", CultureInfo.InvariantCulture) },
        TimeSpan time => new[] { "time", time.ToString("c", CultureInfo.InvariantCulture) },
        IFormattable number => new[] { value.GetType().FullName!, number.ToString(null, CultureInfo.InvariantCulture) },
        _ => new[] { value.GetType().FullName!, value.ToString() ?? "" }
    }));

    private static string[] EncodeString(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (!char.IsSurrogate(text[i])) continue;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            { i++; continue; }

            // SQL Server can return unpaired UTF-16 surrogates. JSON and Encoding.Unicode
            // replace them with U+FFFD, which would collapse distinct results. Encode code
            // units explicitly, independent of machine endianness or encoder fallbacks.
            var bytes = new byte[checked(text.Length * 2)];
            for (int j = 0; j < text.Length; j++)
            {
                bytes[j * 2] = (byte)text[j];
                bytes[j * 2 + 1] = (byte)(text[j] >> 8);
            }
            return ["System.String.UTF16LE", Convert.ToBase64String(bytes)];
        }
        return ["System.String", text];
    }

    public static bool Equal(SqlSemanticObservation left, SqlSemanticObservation right, bool ordered) =>
        left.TransactionCount == right.TransactionCount && left.TransactionState == right.TransactionState &&
        left.SessionOptions == right.SessionOptions && Equal(left.Execution, right.Execution, ordered) &&
        Equal(left.ObservedState, right.ObservedState, ordered) && Equal(left.ObjectState, right.ObjectState, false);

    public static bool Equal(SqlExecutionObservation left, SqlExecutionObservation right, bool ordered)
    {
        if (left.RecordsAffected != right.RecordsAffected || !left.ErrorNumbers.SequenceEqual(right.ErrorNumbers) ||
            left.Results.Length != right.Results.Length) return false;
        return left.Results.Zip(right.Results).All(pair => pair.First.Columns.SequenceEqual(pair.Second.Columns) &&
            (ordered ? pair.First.Rows.SequenceEqual(pair.Second.Rows) :
                pair.First.Rows.Order(StringComparer.Ordinal).SequenceEqual(pair.Second.Rows.Order(StringComparer.Ordinal))));
    }
}
