import re

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'r', encoding='utf-8') as f:
    c = f.read()

# Fix the Separator Style bug
c = c.replace('Style="{DynamicResource BorderBrush}"', 'Background="{DynamicResource BorderBrush}"')

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'w', encoding='utf-8') as f:
    f.write(c)

print("Patch applied to fix Separator Style cast exception.")
