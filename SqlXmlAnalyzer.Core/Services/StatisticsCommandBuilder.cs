using System.Text;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Services;

/// <summary>Builds commands from individual Showplan identifiers, never from SQL fragments.</summary>
public static class StatisticsCommandBuilder
{
    public static string BuildShowStatistics(StatisticsInfo statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        string table = QualifiedTable(statistics);
        string name = DecodeIdentifier(statistics.Statistics, nameof(statistics.Statistics));
        return $"DBCC SHOW_STATISTICS ({Literal(table)}, {Literal(name)}) WITH HISTOGRAM;";
    }

    public static string BuildUpdateStatistics(StatisticsInfo statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        return $"UPDATE STATISTICS {QualifiedTable(statistics)} ({Quote(DecodeIdentifier(statistics.Statistics, nameof(statistics.Statistics)))}) WITH FULLSCAN;";
    }

    private static string QualifiedTable(StatisticsInfo statistics) => string.Join(".",
        Quote(DecodeIdentifier(statistics.Database, nameof(statistics.Database))),
        Quote(DecodeIdentifier(statistics.Schema, nameof(statistics.Schema))),
        Quote(DecodeIdentifier(statistics.Table, nameof(statistics.Table))));

    private static string Quote(string value) => "[" + value.Replace("]", "]]", StringComparison.Ordinal) + "]";
    private static string Literal(string value) => "N'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string DecodeIdentifier(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Invalid(field);
        string decoded = value;
        if (value[0] is '[' or '"')
        {
            char close = value[0] == '[' ? ']' : '"';
            if (value.Length < 2 || value[^1] != close) throw Invalid(field);
            var builder = new StringBuilder();
            for (int i = 1; i < value.Length - 1; i++)
            {
                char current = value[i];
                if (current == close && (i + 1 >= value.Length - 1 || value[++i] != close))
                    throw Invalid(field);
                builder.Append(current);
            }
            decoded = builder.ToString();
        }
        if (string.IsNullOrWhiteSpace(decoded) || decoded.Length > 128 || decoded.Any(char.IsControl))
            throw Invalid(field);
        return decoded;
    }

    private static ArgumentException Invalid(string field) =>
        new("统计信息对象标识缺失或不是有效的单个 SQL Server 标识符。", field);
}
