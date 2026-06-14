import re

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'r', encoding='utf-8') as f:
    c = f.read()

# 1. Change nodify:Connection to nodify:LineConnection for the lines, so arrows are correctly drawn on straight lines.
c = c.replace('<nodify:Connection Source=', '<nodify:LineConnection Source=')
c = c.replace('</nodify:Connection>', '</nodify:LineConnection>')
c = c.replace('<Style TargetType="{x:Type nodify:Connection}">', '<Style TargetType="{x:Type nodify:LineConnection}">')
c = c.replace('<nodify:Connection.Style>', '<nodify:LineConnection.Style>')
c = c.replace('</nodify:Connection.Style>', '</nodify:LineConnection.Style>')

# 2. Add ContextMenu to the Node's main Border
# Find <Border MinWidth="130" Background="{Binding DynamicBackgroundBrush}"
context_menu_xaml = """<Border.ContextMenu>
                                    <ContextMenu>
                                        <MenuItem Header="📋 复制节点所有信息 (Copy All Details)" Click="CopyNodeInfo_Click"/>
                                    </ContextMenu>
                                </Border.ContextMenu>"""

c = c.replace('<Border.Effect>', context_menu_xaml + '\n                                <Border.Effect>')

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'w', encoding='utf-8') as f:
    f.write(c)

print("XAML updated.")
