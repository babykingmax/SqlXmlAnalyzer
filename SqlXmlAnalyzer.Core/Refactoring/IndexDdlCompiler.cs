using System;
using System.Collections.Generic;
using System.Linq;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Refactoring
{
    public static class IndexDdlCompiler
    {
        public static string Generate(MissingIndexSuggestion suggestion, IndexDdlOptions options)
        {
            if (suggestion == null) throw new ArgumentNullException(nameof(suggestion));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var orderedKeys = suggestion.KeyColumns.Select(c => c.Name).ToList();
            if (orderedKeys.Count == 0) return "";

            string cleanTable = new string(suggestion.Table.Trim('[', ']').Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
            var keyColNames = suggestion.KeyColumns
                .Select(c => new string(c.Name.Trim('[', ']', '@').Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray()))
                .Where(n => !string.IsNullOrEmpty(n))
                .Take(3)
                .ToList();

            string indexName = $"IX_{cleanTable}_{string.Join("_", keyColNames)}";
            if (indexName.Length > 120)
            {
                indexName = indexName.Substring(0, 120);
            }

            var keysList = suggestion.KeyColumns.Select(c => EscapeName(c.Name));
            string keys = string.Join(", ", keysList);

            string includes = "";
            if (suggestion.IncludeColumns.Count > 0)
            {
                var includesList = suggestion.IncludeColumns.Select(c => EscapeName(c.Name));
                includes = $"\nINCLUDE ({string.Join(", ", includesList)})";
            }

            var optionsList = new List<string>();
            optionsList.Add(options.Online ? "ONLINE = ON" : "ONLINE = OFF");

            if (!string.IsNullOrEmpty(options.DataCompression))
            {
                optionsList.Add($"DATA_COMPRESSION = {options.DataCompression.ToUpperInvariant()}");
            }

            optionsList.Add(options.SortInTempDb ? "SORT_IN_TEMPDB = ON" : "SORT_IN_TEMPDB = OFF");

            if (options.MaxDop.HasValue)
            {
                optionsList.Add($"MAXDOP = {options.MaxDop.Value}");
            }

            string withClause = $"\nWITH ({string.Join(", ", optionsList)})";

            string target = string.IsNullOrEmpty(suggestion.Schema)
                ? EscapeName(suggestion.Table)
                : $"{EscapeName(suggestion.Schema)}.{EscapeName(suggestion.Table)}";

            return $"CREATE NONCLUSTERED INDEX [{indexName}]\n" +
                   $"ON {target} ({keys}){includes}{withClause};";
        }

        public static string EscapeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var parts = name.Split('.');
            var escapedParts = parts.Select(p => "[" + p.Trim('[', ']').Replace("]", "]]") + "]");
            return string.Join(".", escapedParts);
        }
    }
}
