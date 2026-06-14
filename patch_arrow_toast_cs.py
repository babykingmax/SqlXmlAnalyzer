import re

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml.cs', 'r', encoding='utf-8') as f:
    c = f.read()

# 1. Add ArrowAngle to ConnectionViewModel
arrow_angle_prop = """
        public double ArrowAngle
        {
            get
            {
                return LayoutMode == PlanLayoutMode.Horizontal ? 180 : -90;
            }
        }
"""
c = c.replace('public PlanLayoutMode LayoutMode', arrow_angle_prop + '\n        public PlanLayoutMode LayoutMode')

# Also need to notify ArrowAngle when LayoutMode changes
c = c.replace('OnPropertyChanged(nameof(SourceLocation));', 'OnPropertyChanged(nameof(SourceLocation));\n                    OnPropertyChanged(nameof(ArrowAngle));')

# 2. Update CopyNodeInfo_Click to use ToastPopup
old_msg_box = 'System.Windows.MessageBox.Show("节点信息已成功复制到剪贴板！", "复制成功", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);'
new_toast = """
                ToastPopup.IsOpen = true;
                System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ => Dispatcher.Invoke(() => ToastPopup.IsOpen = false));
"""
c = c.replace(old_msg_box, new_toast)

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(c)

print("C# updated.")
