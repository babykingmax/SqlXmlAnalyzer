using System;
using System.Text;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Refactoring;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed class MissingIndexDeploymentScriptService
    {
        public string BuildDeploymentBundle(MissingIndexSuggestion suggestion)
        {
            ArgumentNullException.ThrowIfNull(suggestion);
            var compiled = IndexDdlCompiler.Compile(suggestion, new());
            if (compiled.Create.Length == 0) return string.Empty;
            static string CommentValue(string value) => value.Replace("/*", "/ *").Replace("*/", "* /")
                .Replace('\r', ' ').Replace('\n', ' ');

            var builder = new StringBuilder();
            builder.AppendLine("/*******************************************************************************");
            builder.AppendLine(" * SQL Server Missing Index Deployment Bundle");
            builder.AppendLine($" * Table:  {CommentValue(suggestion.Table)}");
            if (!string.IsNullOrEmpty(suggestion.Schema))
            {
                builder.AppendLine($" * Schema: {CommentValue(suggestion.Schema)}");
            }

            string impact = suggestion.Source == IndexSuggestionSource.CapturedMissingIndex
                && suggestion.CapturedImpact is { } value && double.IsFinite(value) && value is >= 0 and <= 100
                ? value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "%" : "N/A";
            builder.AppendLine($" * SQL Server Impact: {impact} (optimizer estimate)");
            builder.AppendLine($" * Score:  {suggestion.Score}/100");
            builder.AppendLine(" *******************************************************************************/");
            builder.AppendLine();
            builder.AppendLine("-- === 1. DEPLOYMENT DDL (CREATE INDEX) ===");
            builder.AppendLine("BEGIN TRANSACTION;");
            builder.AppendLine("BEGIN TRY");
            builder.AppendLine("    " + compiled.Create);
            builder.AppendLine("    COMMIT TRANSACTION;");
            builder.AppendLine("    PRINT 'Missing Index deployed successfully.';");
            builder.AppendLine("END TRY");
            builder.AppendLine("BEGIN CATCH");
            builder.AppendLine("    ROLLBACK TRANSACTION;");
            builder.AppendLine("    DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();");
            builder.AppendLine("    RAISERROR(@ErrMsg, 16, 1);");
            builder.AppendLine("END CATCH");
            builder.AppendLine();
            builder.AppendLine("-- === 2. ROLLBACK DDL (DROP INDEX) ===");
            foreach (string line in compiled.Rollback.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                builder.AppendLine("-- " + line);

            return builder.ToString();
        }
    }
}
