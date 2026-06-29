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

        public SqlDiffUiActionService(
            Core.Services.SqlDiffService sqlDiffService,
            Core.Services.SqlDiffDocumentRenderer documentRenderer,
            RichTextBox originalTextBox,
            RichTextBox refactoredTextBox)
        {
            _sqlDiffService = sqlDiffService
                ?? throw new ArgumentNullException(nameof(sqlDiffService));
            _documentRenderer = documentRenderer
                ?? throw new ArgumentNullException(nameof(documentRenderer));
            _originalTextBox = originalTextBox
                ?? throw new ArgumentNullException(nameof(originalTextBox));
            _refactoredTextBox = refactoredTextBox
                ?? throw new ArgumentNullException(nameof(refactoredTextBox));
        }

        public void RenderDiff(
            string originalSql,
            string refactoredSql,
            Func<ScalarSubquery, UIElement>? quickFixElementFactory)
        {
            if (string.IsNullOrEmpty(originalSql) && string.IsNullOrEmpty(refactoredSql))
            {
                _originalTextBox.Document.Blocks.Clear();
                _refactoredTextBox.Document.Blocks.Clear();
                return;
            }

            string[] originalLines = originalSql.Split(
                new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None);
            string[] refactoredLines = refactoredSql.Split(
                new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None);

            Core.Services.SqlAlignedLines alignedLines =
                _sqlDiffService.AlignLines(originalLines, refactoredLines);

            _documentRenderer.Render(
                _originalTextBox,
                alignedLines.Original,
                false,
                alignedLines.Refactored,
                originalSql,
                quickFixElementFactory);
            _documentRenderer.Render(
                _refactoredTextBox,
                alignedLines.Refactored,
                true,
                alignedLines.Original,
                originalSql);
        }
    }
}
