import re

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'r', encoding='utf-8') as f:
    c = f.read()

# 1. Fix Arrow visibility: Increase ArrowSize on background line
c = c.replace('ArrowSize="12,12"', 'ArrowSize="18,18"')

# 2. Fix Tooltip size and interactivity
# Allow Tooltip to stay open, change Placement so mouse can enter
c = c.replace('<ToolTip Background="Transparent" BorderThickness="0" Padding="0" Placement="Right">',
              '<ToolTip Background="Transparent" BorderThickness="0" Padding="0" Placement="Right" ToolTipService.ShowDuration="60000" ToolTipService.InitialShowDelay="100">')

# Reduce Width
c = c.replace('<StackPanel Width="420">', '<StackPanel Width="310">')

# Reduce Paddings
c = c.replace('Padding="16,12"', 'Padding="10,8"')
c = c.replace('<Border Padding="16">', '<Border Padding="10">')

# Convert text blocks for values to selectable text boxes
# We want to replace <TextBlock Text="{Binding ...}" ... Foreground="{DynamicResource TextBrush}" ... />
# with <TextBox Text="{Binding ...}" IsReadOnly="True" BorderThickness="0" Background="Transparent" ... />
def replace_textblock_with_textbox(match):
    original = match.group(0)
    # Don't replace labels (usually they don't have {Binding})
    if '"{Binding' not in original:
        return original
    # Replace TextBlock with TextBox and add required properties for flat look
    replaced = original.replace('<TextBlock', '<TextBox IsReadOnly="True" BorderThickness="0" Background="Transparent" Padding="0" Margin="0,3"')
    # Remove existing Margin="0,3" if duplicated
    replaced = re.sub(r'Margin="[^"]*"', 'Margin="0"', replaced)
    return replaced

# We'll do a regex sub for TextBlocks that are in Grid.Column="1" (which are the values)
c = re.sub(r'<TextBlock[^>]*Grid\.Column="1"[^>]*>', replace_textblock_with_textbox, c)

# Also replace the ones in StackPanels for ObjectDetails, SeekPredicates, etc.
c = re.sub(r'<TextBlock Text="{Binding ObjectDetails}"[^>]*>', lambda m: m.group(0).replace('<TextBlock', '<TextBox IsReadOnly="True" BorderThickness="0" Background="Transparent" Padding="0" Margin="0"').replace('TextWrapping="Wrap"', 'TextWrapping="Wrap"'), c)
c = re.sub(r'<TextBlock Text="{Binding OutputList}"[^>]*>', lambda m: m.group(0).replace('<TextBlock', '<TextBox IsReadOnly="True" BorderThickness="0" Background="Transparent" Padding="0" Margin="0"').replace('TextWrapping="Wrap"', 'TextWrapping="Wrap"'), c)
c = re.sub(r'<TextBlock Text="{Binding SeekPredicates}"[^>]*>', lambda m: m.group(0).replace('<TextBlock', '<TextBox IsReadOnly="True" BorderThickness="0" Background="Transparent" Padding="0" Margin="0"').replace('TextWrapping="Wrap"', 'TextWrapping="Wrap"'), c)
c = re.sub(r'<TextBlock Text="{Binding Predicate}"[^>]*>', lambda m: m.group(0).replace('<TextBlock', '<TextBox IsReadOnly="True" BorderThickness="0" Background="Transparent" Padding="0" Margin="0"').replace('TextWrapping="Wrap"', 'TextWrapping="Wrap"'), c)
c = re.sub(r'<TextBlock Text="{Binding Warnings}"[^>]*>', lambda m: m.group(0).replace('<TextBlock', '<TextBox IsReadOnly="True" BorderThickness="0" Background="Transparent" Padding="0" Margin="0"').replace('TextWrapping="Wrap"', 'TextWrapping="Wrap"'), c)

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'w', encoding='utf-8') as f:
    f.write(c)

print("Patch applied.")
