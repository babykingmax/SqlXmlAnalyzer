import re

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'r', encoding='utf-8') as f:
    c = f.read()

# Replace StaticResource with DynamicResource for Material Design brushes
c = re.sub(r'\{StaticResource PrimaryHue', '{DynamicResource PrimaryHue', c)
c = re.sub(r'\{StaticResource SecondaryHue', '{DynamicResource SecondaryHue', c)
c = re.sub(r'\{StaticResource MaterialDesignPaper\}', '{DynamicResource MaterialDesignPaper}', c)
c = re.sub(r'\{StaticResource MaterialDesignLightSeparator\}', '{DynamicResource MaterialDesignLightSeparator}', c)
c = re.sub(r'\{StaticResource TextBrush\}', '{DynamicResource TextBrush}', c)
c = re.sub(r'\{StaticResource SecondaryTextBrush\}', '{DynamicResource SecondaryTextBrush}', c)

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'w', encoding='utf-8') as f:
    f.write(c)
