import re

with open('E:/SqlXmlAnalyzer/patch_arrow_cs.py', 'r', encoding='utf-8') as f:
    c = f.read()

c = c.replace(r'\\n', '\n')

with open('E:/SqlXmlAnalyzer/patch_arrow_cs.py', 'w', encoding='utf-8') as f:
    f.write(c)

with open('E:/SqlXmlAnalyzer/patch_arrow_cs2.py', 'r', encoding='utf-8') as f:
    c2 = f.read()

c2 = c2.replace(r'\n', '\n')

with open('E:/SqlXmlAnalyzer/patch_arrow_cs2.py', 'w', encoding='utf-8') as f:
    f.write(c2)

print("Python scripts fixed.")
