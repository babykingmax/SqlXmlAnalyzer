import re

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml.cs', 'r', encoding='utf-8') as f:
    c = f.read()

c = c.replace(r'\n', '\n')

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml.cs', 'w', encoding='utf-8') as f:
    f.write(c)

print("Fixed literal slash n.")
