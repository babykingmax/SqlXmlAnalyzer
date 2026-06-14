import re

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml.cs', 'r', encoding='utf-8') as f:
    c = f.read()

# Make sure TargetLocation updates trigger Arrow points
c = c.replace('OnPropertyChanged(nameof(TargetLocation));', 'OnPropertyChanged(nameof(TargetLocation));
                    OnPropertyChanged(nameof(ArrowTransformX));
                    OnPropertyChanged(nameof(ArrowTransformY));')
c = c.replace('OnPropertyChanged(nameof(SourceLocation));', 'OnPropertyChanged(nameof(SourceLocation));
                    OnPropertyChanged(nameof(ArrowTransformX));
                    OnPropertyChanged(nameof(ArrowTransformY));')

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(c)

print("C# property changed events updated.")
