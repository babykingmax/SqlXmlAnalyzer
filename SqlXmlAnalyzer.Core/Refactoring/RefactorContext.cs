using System.Collections.Generic;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlXmlAnalyzer.Core.Refactoring
{
    public class RefactorContext
    {
        public string OriginalSql { get; }
        public IList<string> Logs { get; } = new List<string>();
        public IList<string> Warnings { get; } = new List<string>();
        public bool Changed { get; set; }

        public RefactorContext(string originalSql)
        {
            OriginalSql = originalSql;
        }

        public void Log(string message) => Logs.Add(message);
        public void Warn(string message) => Warnings.Add(message);
    }
}

