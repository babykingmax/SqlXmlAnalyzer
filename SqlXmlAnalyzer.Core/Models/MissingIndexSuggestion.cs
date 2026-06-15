using System.Collections.Generic;
using System.Linq;

namespace SqlXmlAnalyzer.Core.Models
{
    public class MissingIndexSuggestion
    {
        public string Schema { get; set; } = "";
        public string Table { get; set; } = "";
        public double Impact { get; set; }
        public int Score { get; set; }
        
        public List<IndexColumn> KeyColumns { get; set; } = new();
        public List<IndexColumn> IncludeColumns { get; set; } = new();
        
        public string CreateIndexStatement
        {
            get
            {
                return SqlXmlAnalyzer.Core.Refactoring.IndexDdlCompiler.Generate(this, new SqlXmlAnalyzer.Core.Refactoring.IndexDdlOptions());
            }
        }

        public string RollbackStatement
        {
            get
            {
                string cleanTable = new string(Table.Trim('[', ']').Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
                var keyColNames = KeyColumns
                    .Select(c => new string(c.Name.Trim('[', ']', '@').Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray()))
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Take(3)
                    .ToList();

                string indexName = $"IX_{cleanTable}_{string.Join("_", keyColNames)}";
                if (indexName.Length > 120)
                {
                    indexName = indexName.Substring(0, 120);
                }

                string target = string.IsNullOrEmpty(Schema)
                    ? SqlXmlAnalyzer.Core.Refactoring.IndexDdlCompiler.EscapeName(Table)
                    : $"{SqlXmlAnalyzer.Core.Refactoring.IndexDdlCompiler.EscapeName(Schema)}.{SqlXmlAnalyzer.Core.Refactoring.IndexDdlCompiler.EscapeName(Table)}";

                return $"DROP INDEX [{indexName}] ON {target};";
            }
        }
    }
}
