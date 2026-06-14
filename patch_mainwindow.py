import re

with open('E:/SqlXmlAnalyzer/MainWindow.xaml', 'r', encoding='utf-8') as f:
    content = f.read()

# Replace Window configuration
content = content.replace('Title="{Binding AppTitle}" ', 'Title="{Binding AppTitle}"\n        xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"\n        TextElement.Foreground="{DynamicResource MaterialDesignBody}"\n        TextElement.FontWeight="Regular"\n        TextElement.FontSize="13"\n        TextOptions.TextFormattingMode="Ideal"\n        TextOptions.TextRenderingMode="Auto"\n        FontFamily="{DynamicResource MaterialDesignFont}"\n        ')

# Find <Grid> inside Window
grid_idx = content.find('<Grid>')
if grid_idx == -1:
    grid_idx = content.find('<Grid ')

# We want to replace everything from <Grid.RowDefinitions> down to just before <TabControl Grid.Row="2"
start_idx = content.find('<Grid.RowDefinitions>')
end_idx = content.find('<!-- 主内容区 - 参考 SQL Sentry Plan Explorer 风格 -->')

new_top_section = '''<Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- 现代化极简顶部导航 (高度压缩) -->
        <materialDesign:ColorZone Grid.Row="0" Mode="PrimaryMid" Padding="8,4" materialDesign:ElevationAssist.Elevation="Dp2">
            <DockPanel>
                <!-- 标题 -->
                <StackPanel DockPanel.Dock="Left" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,16,0">
                    <materialDesign:PackIcon Kind="DatabaseSearchOutline" Width="20" Height="20" Foreground="White" VerticalAlignment="Center"/>
                    <TextBlock Text="智能诊断引擎" FontSize="14" FontWeight="SemiBold" Margin="6,0,0,0" Foreground="White" VerticalAlignment="Center"/>
                </StackPanel>

                <!-- 隐藏传统的菜单栏，将功能合并到工具栏 -->
                <Menu DockPanel.Dock="Left" Background="Transparent" Foreground="White" VerticalAlignment="Center" Margin="0,0,12,0">
                    <MenuItem Header="文件" Padding="8,4" Foreground="White">
                        <MenuItem Header="打开死锁 XML..." Click="OpenDeadlockFile_Click" Foreground="Black">
                            <MenuItem.Icon><materialDesign:PackIcon Kind="LockOpenVariantOutline" /></MenuItem.Icon>
                        </MenuItem>
                        <MenuItem Header="打开执行计划 XML..." Click="OpenPlanFile_Click" Foreground="Black">
                            <MenuItem.Icon><materialDesign:PackIcon Kind="ChartTree" /></MenuItem.Icon>
                        </MenuItem>
                        <Separator/>
                        <MenuItem Header="退出" Click="Exit_Click" Foreground="Black"/>
                    </MenuItem>
                    <MenuItem Header="帮助" Padding="8,4" Foreground="White">
                        <MenuItem Header="关于" Click="About_Click" Foreground="Black"/>
                    </MenuItem>
                </Menu>

                <!-- 核心工具按钮 (去文字，纯图标化) -->
                <StackPanel DockPanel.Dock="Left" Orientation="Horizontal" VerticalAlignment="Center">
                    <Button Style="{StaticResource MaterialDesignToolForegroundButton}" Click="OpenDeadlockFile_Click" ToolTip="打开死锁" Height="28" Width="28" Padding="0" Margin="0,0,4,0">
                        <materialDesign:PackIcon Kind="LockAlert" Width="18" Height="18"/>
                    </Button>
                    <Button Style="{StaticResource MaterialDesignToolForegroundButton}" Click="OpenPlanFile_Click" ToolTip="打开执行计划" Height="28" Width="28" Padding="0" Margin="0,0,16,0">
                        <materialDesign:PackIcon Kind="ChartTree" Width="18" Height="18"/>
                    </Button>

                    <Separator Style="{StaticResource MaterialDesignLightSeparator}" Width="1" Margin="0,4,16,4" Background="#80FFFFFF"/>

                    <Button Style="{StaticResource MaterialDesignToolForegroundButton}" Click="GenerateHtmlReport_Click" ToolTip="导出 HTML" Height="28" Width="28" Padding="0" Margin="0,0,4,0">
                        <materialDesign:PackIcon Kind="LanguageHtml5" Width="18" Height="18"/>
                    </Button>
                    <Button Style="{StaticResource MaterialDesignToolForegroundButton}" Click="ExportToWord_Click" ToolTip="导出 Word" Height="28" Width="28" Padding="0" Margin="0,0,4,0">
                        <materialDesign:PackIcon Kind="FileWordOutline" Width="18" Height="18"/>
                    </Button>
                    <Button Style="{StaticResource MaterialDesignToolForegroundButton}" Click="ExportToPdf_Click" ToolTip="导出 PDF" Height="28" Width="28" Padding="0" Margin="0,0,4,0">
                        <materialDesign:PackIcon Kind="FilePdfBox" Width="18" Height="18"/>
                    </Button>
                    <Button Style="{StaticResource MaterialDesignToolForegroundButton}" Click="ExportObfuscatedPlan_Click" ToolTip="脱敏导出" Height="28" Width="28" Padding="0" Margin="0,0,4,0">
                        <materialDesign:PackIcon Kind="Incognito" Width="18" Height="18"/>
                    </Button>
                </StackPanel>

                <!-- 右侧清理功能 -->
                <StackPanel DockPanel.Dock="Right" Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Center">
                    <Button Style="{StaticResource MaterialDesignToolForegroundButton}" Click="CopyAnalysisResult_Click" ToolTip="复制结果" Height="28" Width="28" Padding="0" Margin="0,0,4,0">
                        <materialDesign:PackIcon Kind="ContentCopy" Width="16" Height="16"/>
                    </Button>
                    <Button Style="{StaticResource MaterialDesignToolForegroundButton}" Click="ClearResults_Click" ToolTip="清空所有视图" Height="28" Width="28" Padding="0">
                        <materialDesign:PackIcon Kind="Broom" Width="16" Height="16"/>
                    </Button>
                </StackPanel>
            </DockPanel>
        </materialDesign:ColorZone>

        <!-- 主内容区 - 紧凑型 Tabs -->
        '''

if start_idx != -1 and end_idx != -1:
    content = content[:start_idx] + new_top_section + content[end_idx + len('<!-- 主内容区 - 参考 SQL Sentry Plan Explorer 风格 -->'):]

# Also change TabControl to grid row 1 instead of 2
content = content.replace('<TabControl Grid.Row="2" Margin="6,6,6,6"', '<TabControl Grid.Row="1" Margin="2" Style="{StaticResource MaterialDesignFilledTabControl}"')

# Also fix the inner TabControl (Sub tabs)
content = content.replace('<TabControl Grid.Row="1" x:Name="PlanGraphTabControl">', '<TabControl Grid.Row="1" x:Name="PlanGraphTabControl" Style="{StaticResource MaterialDesignSecondaryTabControl}" Margin="0,2,0,0">')

# Make the Expander smaller
content = content.replace('Header="查询语句 (Statement Text)" ExpandDirection="Down" IsExpanded="False" Margin="4,4,4,12"', 'Header="查询语句 (Statement Text)" ExpandDirection="Down" IsExpanded="False" Margin="2,0,2,4"')

# Make Bottom Expander smaller
content = content.replace('Header="执行计划深度诊断报告 (Deep Diagnostic Report) ★ 13 类专家调优建议" ExpandDirection="Up" IsExpanded="True" Margin="4"', 'Header="执行计划深度诊断报告 (★ 13类专家建议)" ExpandDirection="Up" IsExpanded="True" Margin="2,4,2,0"')

with open('E:/SqlXmlAnalyzer/MainWindow.xaml', 'w', encoding='utf-8') as f:
    f.write(content)

