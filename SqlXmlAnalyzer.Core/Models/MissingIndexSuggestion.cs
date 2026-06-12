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
                var orderedKeys = KeyColumns.Select(c => c.Name).ToList();
                var includes = IncludeColumns.Select(c => c.Name).ToList();
                
                if (orderedKeys.Count == 0) return "";
                
                string cleanTable = Table.Trim('[', ']');
                string firstCol = orderedKeys.First().Trim('[', ']');
                string indexName = $"IX_{cleanTable}_{firstCol}";
                
                string stmt = $"CREATE NONCLUSTERED INDEX [{indexName}] ON {Schema}.{Table} ({string.Join(", ", orderedKeys)})";
                if (includes.Count > 0)
                {
                    stmt += $" INCLUDE ({string.Join(", ", includes)})";
                }
                return stmt;
            }
        }
    }
}
