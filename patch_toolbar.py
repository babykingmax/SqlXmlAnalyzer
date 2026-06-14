import re

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'r', encoding='utf-8') as f:
    c = f.read()

# Replace the bulky Toolbar with a modern Material ToolBar
new_toolbar = '''                <!-- 现代轻量化工具栏 -->
                <Border Background="{StaticResource SurfaceBrush}" BorderBrush="{StaticResource BorderBrush}" BorderThickness="0,0,0,1" Padding="8,6" VerticalAlignment="Top" Panel.ZIndex="10">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        
                        <!-- 左侧：视图标题 -->
                        <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center">
                            <materialDesign:PackIcon Kind="ChartTree" Width="18" Height="18" Foreground="{StaticResource AccentBrush}" VerticalAlignment="Center"/>
                            <TextBlock Text="执行计划图" FontWeight="Bold" FontSize="13" VerticalAlignment="Center" Margin="6,0,16,0" Foreground="{StaticResource TextBrush}"/>
                        </StackPanel>
                        
                        <!-- 中间：精简的视图配置选项 (使用 HintAssist 替代前面的文本 Label，节省空间) -->
                        <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center">
                            <!-- 视图模式 -->
                            <ComboBox x:Name="CmbViewMode" SelectedIndex="0" Width="130" Margin="0,0,12,0" 
                                      materialDesign:HintAssist.Hint="视图模式"
                                      Style="{StaticResource MaterialDesignFloatingHintComboBox}"
                                      SelectionChanged="CmbViewMode_SelectionChanged" FontSize="12">
                                <ComboBoxItem Content="成本百分比 (%)"/>
                                <ComboBoxItem Content="CPU + I/O 成本"/>
                                <ComboBoxItem Content="行数对比 (Act/Est)"/>
                            </ComboBox>

                            <!-- 热力图模式 -->
                            <ComboBox x:Name="CmbColorMode" SelectedIndex="0" Width="110" Margin="0,0,12,0" 
                                      materialDesign:HintAssist.Hint="热力图着色"
                                      Style="{StaticResource MaterialDesignFloatingHintComboBox}"
                                      SelectionChanged="CmbColorMode_SelectionChanged" FontSize="12">
                                <ComboBoxItem Content="总成本百分比"/>
                                <ComboBoxItem Content="CPU 成本"/>
                                <ComboBoxItem Content="I/O 成本"/>
                            </ComboBox>

                            <!-- 连线指标 -->
                            <ComboBox x:Name="CmbLinkMetric" SelectedIndex="0" Width="90" Margin="0,0,12,0"
                                      materialDesign:HintAssist.Hint="连线粗细"
                                      Style="{StaticResource MaterialDesignFloatingHintComboBox}"
                                      SelectionChanged="CmbLinkMetric_SelectionChanged" FontSize="12">
                                <ComboBoxItem Content="数据行数"/>
                                <ComboBoxItem Content="数据大小"/>
                            </ComboBox>
                            
                            <!-- 布局方向 -->
                            <ComboBox x:Name="CmbLayoutMode" SelectedIndex="0" Width="80" Margin="0,0,12,0"
                                      materialDesign:HintAssist.Hint="布局方向"
                                      Style="{StaticResource MaterialDesignFloatingHintComboBox}"
                                      SelectionChanged="CmbLayoutMode_SelectionChanged" FontSize="12">
                                <ComboBoxItem Content="水平 (H)"/>
                                <ComboBoxItem Content="垂直 (V)"/>
                            </ComboBox>
                        </StackPanel>
                        
                        <!-- 右侧：操作按钮组 (使用图标按钮代替文字按钮) -->
                        <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
                            <Button Style="{StaticResource MaterialDesignOutlinedButton}" Click="SmartCollapse_Click"
                                    ToolTip="智能折叠: 自动折叠成本 &lt;5% 且无警告的子树" Height="30" Padding="12,0" Margin="0,0,8,0">
                                <StackPanel Orientation="Horizontal">
                                    <materialDesign:PackIcon Kind="CollapseAll" Width="16" Height="16" VerticalAlignment="Center"/>
                                    <TextBlock Text="智能折叠" FontSize="12" Margin="6,0,0,0" VerticalAlignment="Center"/>
                                </StackPanel>
                            </Button>
                            
                            <Button Style="{StaticResource MaterialDesignFlatButton}" Click="ExpandAll_Click"
                                    ToolTip="全部展开" Height="30" Width="36" Padding="0">
                                <materialDesign:PackIcon Kind="ExpandAll" Width="18" Height="18"/>
                            </Button>
                            
                            <Button Style="{StaticResource MaterialDesignFlatButton}" Click="ResetView_Click"
                                    ToolTip="重置视图位置" Height="30" Width="36" Padding="0" Margin="4,0,0,0">
                                <materialDesign:PackIcon Kind="Refresh" Width="18" Height="18"/>
                            </Button>
                        </StackPanel>
                    </Grid>
                </Border>'''

# regex to replace from <!-- 顶部工具提示栏 --> to </Border>
start_idx = c.find('<!-- 顶部工具提示栏 -->')
end_idx = c.find('</Border>', start_idx) + len('</Border>')

if start_idx != -1 and end_idx != -1:
    c = c[:start_idx] + new_toolbar + c[end_idx:]

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'w', encoding='utf-8') as f:
    f.write(c)
