using System;
using System.Windows.Controls;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed class PlanOperatorTreeViewRenderer
    {
        public TreeViewItem RenderLazy(PlanOperatorTreeNode node)
        {
            ArgumentNullException.ThrowIfNull(node);
            var item = new TreeViewItem { Header = node.Header, Tag = node.Source };
            if (node.Children.Count == 0) return item;
            var placeholder = new TreeViewItem { Header = "展开以加载" };
            item.Items.Add(placeholder);
            item.Expanded += (_, e) =>
            {
                if (!ReferenceEquals(e.OriginalSource, item) || !item.Items.Contains(placeholder)) return;
                try
                {
                    item.Items.Clear();
                    foreach (var child in node.Children) item.Items.Add(RenderLazy(child));
                }
                catch (Exception exception) { Diagnostics.ExceptionPolicy.Describe(exception, "IMP26.OperatorTreeExpand"); }
            };
            return item;
        }

        public TreeViewItem Render(PlanOperatorTreeNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            var item = new TreeViewItem
            {
                Header = node.Header,
                Tag = node.Source
            };

            foreach (PlanOperatorTreeNode child in node.Children)
            {
                item.Items.Add(Render(child));
            }

            return item;
        }
    }
}
