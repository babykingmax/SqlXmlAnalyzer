import re

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml.cs', 'r', encoding='utf-8') as f:
    c = f.read()

handler_code = """
        private void CopyNodeInfo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.DataContext is PlanNodeViewModel node)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Node ID: {node.NodeId}");
                sb.AppendLine($"Physical Op: {node.PhysicalOp}");
                sb.AppendLine($"Logical Op: {node.LogicalOp}");
                sb.AppendLine($"Estimated Cost: {node.SubtreeCost} ({node.CostPercent:F1}%)");
                sb.AppendLine($"Estimated Rows: {node.EstimatedRows}");
                sb.AppendLine($"Actual Rows: {node.ActualRows}");
                sb.AppendLine($"Estimated Data Size: {node.EstimatedDataSize}");
                
                if (!string.IsNullOrEmpty(node.ObjectDetails)) 
                    sb.AppendLine($"Object: {node.ObjectDetails}");
                if (!string.IsNullOrEmpty(node.OutputList)) 
                    sb.AppendLine($"Output List: {node.OutputList}");
                if (!string.IsNullOrEmpty(node.SeekPredicates)) 
                    sb.AppendLine($"Seek Predicates: {node.SeekPredicates}");
                if (!string.IsNullOrEmpty(node.Predicate)) 
                    sb.AppendLine($"Predicate: {node.Predicate}");
                if (!string.IsNullOrEmpty(node.Warnings)) 
                    sb.AppendLine($"Warnings: {node.Warnings}");

                System.Windows.Clipboard.SetText(sb.ToString());
                System.Windows.MessageBox.Show("节点信息已成功复制到剪贴板！", "复制成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
        }
"""

if "CopyNodeInfo_Click" not in c:
    # Insert it before the last closing brace of the class
    # We can find the end of PlanGraphControl class by looking for the last "}" before the namespace closing
    # An easier way is to just insert it before `private void ResetView_Click`
    c = c.replace('private void ResetView_Click', handler_code + '\n        private void ResetView_Click')
    
with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(c)

print("C# handler added.")
