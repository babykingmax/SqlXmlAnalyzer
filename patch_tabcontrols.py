import re

with open('E:/SqlXmlAnalyzer/MainWindow.xaml', 'r', encoding='utf-8') as f:
    c = f.read()

# Replace invalid TabControl styles
c = c.replace('Style="{StaticResource MaterialDesignFilledTabControl}" ', '')
c = c.replace('Style="{StaticResource MaterialDesignSecondaryTabControl}" ', '')

# Replace invalid MaterialDesignToolForegroundButton if it's invalid (MaterialDesignFlatButton is usually safer)
# Let's replace MaterialDesignToolForegroundButton with MaterialDesignFlatLightButton
c = c.replace('MaterialDesignToolForegroundButton', 'MaterialDesignFlatLightButton')

with open('E:/SqlXmlAnalyzer/MainWindow.xaml', 'w', encoding='utf-8') as f:
    f.write(c)
