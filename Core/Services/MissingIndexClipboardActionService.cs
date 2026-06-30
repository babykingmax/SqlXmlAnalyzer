using System;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Services
{
    public enum MissingIndexClipboardActionStatus
    {
        Ready,
        MissingContent
    }

    public sealed record MissingIndexClipboardActionResult(
        MissingIndexClipboardActionStatus Status,
        string Text,
        string SuccessMessage);

    public sealed class MissingIndexClipboardActionService
    {
        private readonly MissingIndexDeploymentScriptService _deploymentScriptService;

        public MissingIndexClipboardActionService(
            MissingIndexDeploymentScriptService? deploymentScriptService = null)
        {
            _deploymentScriptService = deploymentScriptService
                ?? new MissingIndexDeploymentScriptService();
        }

        public MissingIndexClipboardActionResult BuildCreateScript(string? ddl)
        {
            return BuildDdlResult(
                ddl,
                "CREATE INDEX DDL copied to clipboard.");
        }

        public MissingIndexClipboardActionResult BuildRollbackScript(string? ddl)
        {
            return BuildDdlResult(
                ddl,
                "DROP INDEX rollback DDL copied to clipboard.");
        }

        public MissingIndexClipboardActionResult BuildDeploymentBundle(
            MissingIndexSuggestion? suggestion)
        {
            if (suggestion == null)
            {
                return Missing();
            }

            return new MissingIndexClipboardActionResult(
                MissingIndexClipboardActionStatus.Ready,
                _deploymentScriptService.BuildDeploymentBundle(suggestion),
                "Deployment bundle copied to clipboard.");
        }

        private static MissingIndexClipboardActionResult BuildDdlResult(
            string? ddl,
            string successMessage)
        {
            if (string.IsNullOrEmpty(ddl))
            {
                return Missing();
            }

            return new MissingIndexClipboardActionResult(
                MissingIndexClipboardActionStatus.Ready,
                ddl,
                successMessage);
        }

        private static MissingIndexClipboardActionResult Missing()
        {
            return new MissingIndexClipboardActionResult(
                MissingIndexClipboardActionStatus.MissingContent,
                string.Empty,
                string.Empty);
        }
    }
}
