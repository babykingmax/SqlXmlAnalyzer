import re

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'r', encoding='utf-8') as f:
    c = f.read()

# Fix Tooltip header background and text
c = c.replace('{DynamicResource MaterialDesignPaper}', '{DynamicResource SurfaceBrush}')
c = c.replace('{DynamicResource PrimaryHueMidBrush}', '{DynamicResource AccentBrush}')
c = c.replace('{DynamicResource PrimaryHueMidForegroundBrush}', 'White')
c = c.replace('{DynamicResource PrimaryHueLightForegroundBrush}', '#E0F0FF')
c = c.replace('{DynamicResource MaterialDesignLightSeparator}', '{DynamicResource BorderBrush}')
c = c.replace('{DynamicResource SecondaryHueMidBrush}', '{DynamicResource AccentHoverBrush}')

# Fix animated line and capsule
c = c.replace('{DynamicResource PrimaryHueLightBrush}', '{DynamicResource AccentHoverBrush}')

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'w', encoding='utf-8') as f:
    f.write(c)

print("Patch applied to PlanGraphControl.xaml")
