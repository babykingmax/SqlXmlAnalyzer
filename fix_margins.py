import re

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'r', encoding='utf-8') as f:
    c = f.read()

# Fix duplicated Margin attribute
def fix_duplicate_margins(match):
    tag = match.group(0)
    # find all Margin="..."
    margins = list(re.finditer(r'Margin="[^"]*"', tag))
    if len(margins) > 1:
        # Keep the first, remove the rest
        for m in reversed(margins[1:]):
            tag = tag[:m.start()] + tag[m.end():]
    return tag

c = re.sub(r'<TextBox[^>]*>', fix_duplicate_margins, c)

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'w', encoding='utf-8') as f:
    f.write(c)

print("Duplicate margins removed.")
