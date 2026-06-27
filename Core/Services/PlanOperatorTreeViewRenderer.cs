using System;
using System.Windows.Controls;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed class PlanOperatorTreeViewRenderer
    {
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
