import re

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'r', encoding='utf-8') as f:
    c = f.read()

# TextBox does not support TextTrimming
c = re.sub(r'(<TextBox[^>]*)TextTrimming="[^"]*"([^>]*>)', r'\1\2', c)

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'w', encoding='utf-8') as f:
    f.write(c)

print("TextTrimming removed from TextBox.")
