using System;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Documents;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class SqlDiffDocumentRendererTests
    {
        [Fact]
        public void Render_WritesAlignedLinesToRichTextBox()
        {
            RunOnStaThread(() =>
            {
                var renderer = new SqlDiffDocumentRenderer(new SqlDiffService());
                var target = new RichTextBox();

                renderer.Render(
                    target,
                    new string?[] { "SELECT 'old'", null },
                    false,
                    new string?[] { "SELECT 'new'", "WHERE Id = 1" },
                    "SELECT 'old'");

                target.Document.Blocks.Should().HaveCount(2);
                string text = new TextRange(
                    target.Document.ContentStart,
                    target.Document.ContentEnd).Text;
                text.Should().Contain("SELECT");
            });
        }

        [Fact]
        public void Render_WhenTargetIsNull_Throws()
        {
            var renderer = new SqlDiffDocumentRenderer(new SqlDiffService());

            Action act = () => renderer.Render(
                null!,
                Array.Empty<string?>(),
                false,
                Array.Empty<string?>(),
                "");

            act.Should().Throw<ArgumentNullException>();
        }

        private static void RunOnStaThread(Action action)
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (exception != null)
            {
                throw exception;
            }
        }
    }
}
