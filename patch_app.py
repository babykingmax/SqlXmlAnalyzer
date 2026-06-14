import re

with open('E:/SqlXmlAnalyzer/App.xaml','r',encoding='utf-8') as f:
    c = f.read()

d = '''<ResourceDictionary.MergedDictionaries>
                <materialDesign:BundledTheme BaseTheme="Light" PrimaryColor="DeepPurple" SecondaryColor="Lime" />
                <ResourceDictionary Source="pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesignTheme.Defaults.xaml" />
            </ResourceDictionary.MergedDictionaries>'''

c = re.sub(r'(<ResourceDictionary>)', r'\1\n            ' + d, c)
c = c.replace('xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"', 'xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"\n             xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"')

with open('E:/SqlXmlAnalyzer/App.xaml','w',encoding='utf-8') as f:
    f.write(c)
