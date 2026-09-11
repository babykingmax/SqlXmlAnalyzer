using System.Collections.Generic;
using System.Linq;

namespace SqlXmlAnalyzer.Core.Models
{
    public class MissingIndexSuggestion
    {
        public string Schema { get; set; } = "";
        public string Table { get; set; } = "";
        public string? Server { get; set; }
        public string? Database { get; set; }
        public SqlObjectIdentity? ObjectIdentity { get; set; }
        public PlanLocation? Location { get; set; }
        public double Impact { get; set; }
        public double? CapturedImpact { get; set; }
        public IndexSuggestionSource Source { get; set; }
        public int Score { get; set; }
        public Scoring.IndexScoreResult? ScoreAssessment { get; set; }

        public List<IndexColumn> KeyColumns { get; set; } = new();
        public List<IndexColumn> IncludeColumns { get; set; } = new();

        public string CreateIndexStatement
        {
            get
            {
                return SqlXmlAnalyzer.Core.Refactoring.IndexDdlCompiler.Generate(this, new SqlXmlAnalyzer.Core.Refactoring.IndexDdlOptions());
            }
        }

        public string RollbackStatement => SqlXmlAnalyzer.Core.Refactoring.IndexDdlCompiler.GenerateRollback(this);
    }

    public enum IndexSuggestionSource { Unknown, CapturedMissingIndex, SqlSyntax }
}
