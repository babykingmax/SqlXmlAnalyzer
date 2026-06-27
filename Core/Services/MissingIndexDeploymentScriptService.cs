using System;
using System.Text;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed class MissingIndexDeploymentScriptService
    {
        public string BuildDeploymentBundle(MissingIndexSuggestion suggestion)
        {
            ArgumentNullException.ThrowIfNull(suggestion);

            var builder = new StringBuilder();
            builder.AppendLine("/*******************************************************************************");
            builder.AppendLine(" * SQL Server Missing Index Deployment Bundle");
            builder.AppendLine($" * Table:  {suggestion.Table}");
            if (!string.IsNullOrEmpty(suggestion.Schema))
            {
                builder.AppendLine($" * Schema: {suggestion.Schema}");
            }

            builder.AppendLine($" * Impact: {suggestion.Impact:F2}%");
            builder.AppendLine($" * Score:  {suggestion.Score}/100");
            builder.AppendLine(" *******************************************************************************/");
            builder.AppendLine();
            builder.AppendLine("-- === 1. DEPLOYMENT DDL (CREATE INDEX) ===");
            builder.AppendLine("BEGIN TRANSACTION;");
            builder.AppendLine("BEGIN TRY");
            builder.AppendLine("    " + suggestion.CreateIndexStatement);
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
            builder.AppendLine("/*");
            builder.AppendLine("    " + suggestion.RollbackStatement);
            builder.AppendLine("*/");

            return builder.ToString();
        }
    }
}
