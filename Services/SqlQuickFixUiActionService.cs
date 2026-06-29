using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlXmlAnalyzer.Services
{
    internal sealed record SqlQuickFixAppliedResult(
        string RewrittenSql,
        string StatementPreview);

    internal sealed class SqlQuickFixUiActionService
    {
        private readonly Window _owner;
        private readonly Core.Services.SqlQuickFixService _quickFixService;
        private readonly Func<string> _currentSqlProvider;
        private readonly Action<SqlQuickFixAppliedResult> _appliedHandler;

        public SqlQuickFixUiActionService(
            Window owner,
            Core.Services.SqlQuickFixService quickFixService,
            Func<string> currentSqlProvider,
            Action<SqlQuickFixAppliedResult> appliedHandler)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _quickFixService = quickFixService
                ?? throw new ArgumentNullException(nameof(quickFixService));
            _currentSqlProvider = currentSqlProvider
                ?? throw new ArgumentNullException(nameof(currentSqlProvider));
            _appliedHandler = appliedHandler
                ?? throw new ArgumentNullException(nameof(appliedHandler));
        }

        public UIElement CreateLightbulbButton(ScalarSubquery subquery)
        {
            var textBlock = new TextBlock
            {
                Text = "\uD83D\uDCA1",
                ToolTip = "标量子查询可优化为 JOIN，点击一键修复并对比效果",
                Cursor = Cursors.Hand,
                Margin = new Thickness(2, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            textBlock.MouseDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    e.Handled = true;
                    ApplyQuickFix(subquery);
                }
            };

            return textBlock;
        }

        private void ApplyQuickFix(ScalarSubquery subquery)
        {
            string currentSql = _currentSqlProvider();
            Core.Services.SqlQuickFixResult quickFix =
                _quickFixService.TryRewriteSelectedSubquery(
                    currentSql,
                    subquery.StartOffset,
                    subquery.FragmentLength);

            if (!quickFix.IsAvailable)
            {
                MessageBox.Show(quickFix.FailureMessage, "快速修复不可用", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new QuickFixWindow(currentSql, quickFix.RewrittenSql, subquery)
            {
                Owner = _owner
            };

            dialog.ShowDialog();
            if (!dialog.Applied)
            {
                return;
            }

            _appliedHandler(
                new SqlQuickFixAppliedResult(
                    quickFix.RewrittenSql,
                    quickFix.StatementPreview));
            MessageBox.Show("已应用所选标量子查询的 JOIN 重写。", "修复成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
