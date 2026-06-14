import re

with open('E:/SqlXmlAnalyzer/App.xaml','r',encoding='utf-8') as f:
    c = f.read()

c = c.replace('<ResourceDictionary Source="pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Defaults.xaml" />',
              '<ResourceDictionary Source="pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Defaults.xaml" />\n                <ResourceDictionary Source="ThemeColors.xaml" />')

with open('E:/SqlXmlAnalyzer/App.xaml','w',encoding='utf-8') as f:
    f.write(c)
