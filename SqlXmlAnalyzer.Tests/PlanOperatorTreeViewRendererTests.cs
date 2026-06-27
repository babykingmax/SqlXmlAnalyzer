using System;
using System.Threading;
using System.Windows.Controls;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanOperatorTreeViewRendererTests
    {
        [Fact]
        public void Render_CreatesTreeViewItemWithHeaderTagAndChildren()
        {
            RunOnStaThread(() =>
            {
                XElement source = new("RelOp");
                var child = new PlanOperatorTreeNode
                {
                    Header = "Index Seek",
                    Source = new XElement("Child")
                };
                var node = new PlanOperatorTreeNode
                {
                    Header = "Nested Loops",
                    Source = source,
                    Children = new[] { child }
                };
                var renderer = new PlanOperatorTreeViewRenderer();

                TreeViewItem item = renderer.Render(node);

                item.Header.Should().Be("Nested Loops");
                item.Tag.Should().BeSameAs(source);
                item.Items.Count.Should().Be(1);
                ((TreeViewItem)item.Items[0]).Header.Should().Be("Index Seek");
            });
        }

        [Fact]
        public void Render_WhenNodeIsNull_Throws()
        {
            var renderer = new PlanOperatorTreeViewRenderer();

            Action act = () => renderer.Render(null!);

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
