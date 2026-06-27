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
                "CREATE INDEX DDL 已成功复制到剪贴板！");
        }

        public MissingIndexClipboardActionResult BuildRollbackScript(string? ddl)
        {
            return BuildDdlResult(
                ddl,
                "DROP INDEX (回滚) DDL 已成功复制到剪贴板！");
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
                "完整部署包 (包含安全事务与回滚脚本) 已复制到剪贴板！");
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
