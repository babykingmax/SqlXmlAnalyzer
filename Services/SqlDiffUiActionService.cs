using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class SqlDiffUiActionService
    {
        private readonly Core.Services.SqlDiffService _sqlDiffService;
        private readonly Core.Services.SqlDiffDocumentRenderer _documentRenderer;
        private readonly RichTextBox _originalTextBox;
        private readonly RichTextBox _refactoredTextBox;
        private readonly TextBox _statementTextBox;
        private Func<ScalarSubquery, UIElement>? _quickFixElementFactory;

        public string CurrentOriginalSql { get; private set; } = "";

        public string CurrentRefactoredSql { get; private set; } = "";

        public SqlDiffUiActionService(
            Core.Services.SqlDiffService sqlDiffService,
            Core.Services.SqlDiffDocumentRenderer documentRenderer,
            RichTextBox originalTextBox,
            RichTextBox refactoredTextBox,
            TextBox statementTextBox)
        {
            _sqlDiffService = sqlDiffService
                ?? throw new ArgumentNullException(nameof(sqlDiffService));
            _documentRenderer = documentRenderer
                ?? throw new ArgumentNullException(nameof(documentRenderer));
            _originalTextBox = originalTextBox
                ?? throw new ArgumentNullException(nameof(originalTextBox));
            _refactoredTextBox = refactoredTextBox
                ?? throw new ArgumentNullException(nameof(refactoredTextBox));
            _statementTextBox = statementTextBox
                ?? throw new ArgumentNullException(nameof(statementTextBox));
        }

        public void SetSql(
            string originalSql,
            string refactoredSql,
            Func<ScalarSubquery, UIElement>? quickFixElementFactory)
        {
            CurrentOriginalSql = originalSql;
            CurrentRefactoredSql = refactoredSql;
            _quickFixElementFactory = quickFixElementFactory;
            RenderDiff(quickFixElementFactory);
        }

        public void ApplyQuickFixResult(SqlQuickFixAppliedResult result)
        {
            CurrentOriginalSql = result.RewrittenSql;
            CurrentRefactoredSql = result.RewrittenSql;
            RenderDiff(_quickFixElementFactory);
            _statementTextBox.Text = result.StatementPreview;
        }

        private void RenderDiff(Func<ScalarSubquery, UIElement>? quickFixElementFactory)
        {
            if (string.IsNullOrEmpty(CurrentOriginalSql) && string.IsNullOrEmpty(CurrentRefactoredSql))
            {
                _originalTextBox.Document.Blocks.Clear();
                _refactoredTextBox.Document.Blocks.Clear();
                return;
            }

            string[] originalLines = CurrentOriginalSql.Split(
                new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None);
            string[] refactoredLines = CurrentRefactoredSql.Split(
                new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None);

            Core.Services.SqlAlignedLines alignedLines =
                _sqlDiffService.AlignLines(originalLines, refactoredLines);

            _documentRenderer.Render(
                _originalTextBox,
                alignedLines.Original,
                false,
                alignedLines.Refactored,
                CurrentOriginalSql,
                quickFixElementFactory);
            _documentRenderer.Render(
                _refactoredTextBox,
                alignedLines.Refactored,
                true,
                alignedLines.Original,
                CurrentOriginalSql);
        }
    }
}
