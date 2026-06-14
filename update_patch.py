import re

with open('E:/SqlXmlAnalyzer/patch_menu_toast_design.py', 'r', encoding='utf-8') as f:
    c = f.read()

c = c.replace('Placement="Bottom"', 'Placement="Center"')
c = c.replace('VerticalOffset="-80"', 'VerticalOffset="250"')

with open('E:/SqlXmlAnalyzer/patch_menu_toast_design.py', 'w', encoding='utf-8') as f:
    f.write(c)

print("Patch script updated.")
