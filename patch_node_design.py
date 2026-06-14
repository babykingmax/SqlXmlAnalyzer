import re

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'r', encoding='utf-8') as f:
    c = f.read()

if 'xmlns:materialDesign=' not in c:
    c = c.replace('xmlns:local="clr-namespace:SqlXmlAnalyzer"',
                  'xmlns:local="clr-namespace:SqlXmlAnalyzer"\n             xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"')

# The new DataTemplate design
new_template = '''<DataTemplate DataType="{x:Type local:PlanNodeViewModel}">
                            <Grid>
                                <!-- Material Design 风格的节点卡片 -->
                                <Border MinWidth="140" MinHeight="75" 
                                        Background="White" 
                                        BorderBrush="{Binding DynamicBorderBrush}" 
                                        BorderThickness="{Binding DynamicBorderThickness}" 
                                        CornerRadius="6"
                                        SnapsToDevicePixels="True">
                                    <Border.Effect>
                                        <DropShadowEffect Color="#000000" BlurRadius="6" ShadowDepth="2" Opacity="0.12"/>
                                    </Border.Effect>
                                    
                                    <!-- 节点 ToolTip 保留原有详细信息 -->
                                    <Border.ToolTip>
                                        <ToolTip Background="#FFFFFF" BorderBrush="#E0E0E0" BorderThickness="1" Padding="0"
                                                 materialDesign:ShadowAssist.ShadowDepth="Depth2">
                                            <Border CornerRadius="4" ClipToBounds="True">
                                                <StackPanel Width="400">
                                                    <!-- 头部区域 -->
                                                    <Border Background="#1976D2" Padding="12,8">
                                                        <StackPanel>
                                                            <TextBlock Text="{Binding PhysicalOp}" FontWeight="Bold" FontSize="14" Foreground="#FFFFFF" />
                                                            <TextBlock Text="{Binding LogicalOpSuffix}" FontSize="11" FontStyle="Italic" Foreground="#BBDEFB" Margin="0,2,0,0" Visibility="{Binding HasExtraInfo}"/>
                                                            <TextBlock Text="{Binding NodeId, StringFormat='Node ID: {0}'}" FontSize="10" Foreground="#E3F2FD" Margin="0,4,0,0"/>
                                                        </StackPanel>
                                                    </Border>
                                                    <!-- 主体数据区域 -->
                                                    <Border Padding="12">
                                                        <StackPanel>
                                                            <!-- 执行与行数信息 -->
                                                            <Grid Margin="0,0,0,8">
                                                                <Grid.ColumnDefinitions>
                                                                    <ColumnDefinition Width="130"/>
                                                                    <ColumnDefinition Width="*"/>
                                                                </Grid.ColumnDefinitions>
                                                                <Grid.RowDefinitions>
                                                                    <RowDefinition Height="Auto"/>
                                                                    <RowDefinition Height="Auto"/>
                                                                    <RowDefinition Height="Auto"/>
                                                                    <RowDefinition Height="Auto"/>
                                                                </Grid.RowDefinitions>
                                                                
                                                                <TextBlock Text="Executions (Act/Est):" Grid.Row="0" Grid.Column="0" FontSize="11" Foreground="#546E7A" Margin="0,2"/>
                                                                <StackPanel Orientation="Horizontal" Grid.Row="0" Grid.Column="1" Margin="0,2">
                                                                    <TextBlock Text="{Binding ActualExecutions}" FontSize="11" Foreground="#263238" FontWeight="SemiBold"/>
                                                                    <TextBlock Text=" / " FontSize="11" Foreground="#90A4AE"/>
                                                                    <TextBlock Text="{Binding EstimatedExecutions}" FontSize="11" Foreground="#263238"/>
                                                                </StackPanel>

                                                                <TextBlock Text="Rows (Act/Est):" Grid.Row="1" Grid.Column="0" FontSize="11" Foreground="#546E7A" Margin="0,2"/>
                                                                <StackPanel Orientation="Horizontal" Grid.Row="1" Grid.Column="1" Margin="0,2">
                                                                    <TextBlock Text="{Binding ActualRows}" FontSize="11" Foreground="#263238" FontWeight="SemiBold"/>
                                                                    <TextBlock Text=" / " FontSize="11" Foreground="#90A4AE"/>
                                                                    <TextBlock Text="{Binding EstRows}" FontSize="11" Foreground="#263238"/>
                                                                </StackPanel>

                                                                <TextBlock Text="Data Size (Est):" Grid.Row="2" Grid.Column="0" FontSize="11" Foreground="#546E7A" Margin="0,2"/>
                                                                <TextBlock Text="{Binding EstimatedDataSize}" Grid.Row="2" Grid.Column="1" FontSize="11" Foreground="#263238" Margin="0,2"/>

                                                                <TextBlock Text="Execution Mode:" Grid.Row="3" Grid.Column="0" FontSize="11" Foreground="#546E7A" Margin="0,2"/>
                                                                <TextBlock Text="{Binding ExecutionMode}" Grid.Row="3" Grid.Column="1" FontSize="11" Foreground="#263238" Margin="0,2"/>
                                                            </Grid>

                                                            <Separator Background="#ECEFF1" Margin="0,0,0,8"/>

                                                            <!-- 成本信息 -->
                                                            <Grid Margin="0,0,0,8">
                                                                <Grid.ColumnDefinitions>
                                                                    <ColumnDefinition Width="130"/>
                                                                    <ColumnDefinition Width="*"/>
                                                                </Grid.ColumnDefinitions>
                                                                <Grid.RowDefinitions>
                                                                    <RowDefinition Height="Auto"/>
                                                                    <RowDefinition Height="Auto"/>
                                                                    <RowDefinition Height="Auto"/>
                                                                    <RowDefinition Height="Auto"/>
                                                                </Grid.RowDefinitions>
                                                                
                                                                <TextBlock Text="Operator Cost:" Grid.Row="0" Grid.Column="0" FontSize="11" Foreground="#546E7A" Margin="0,2"/>
                                                                <TextBlock Text="{Binding EstimatedOperatorCost}" Grid.Row="0" Grid.Column="1" FontSize="11" Foreground="#263238" Margin="0,2"/>

                                                                <TextBlock Text="I/O Cost:" Grid.Row="1" Grid.Column="0" FontSize="11" Foreground="#546E7A" Margin="0,2"/>
                                                                <TextBlock Text="{Binding EstimatedIOCost}" Grid.Row="1" Grid.Column="1" FontSize="11" Foreground="#263238" Margin="0,2"/>

                                                                <TextBlock Text="CPU Cost:" Grid.Row="2" Grid.Column="0" FontSize="11" Foreground="#546E7A" Margin="0,2"/>
                                                                <TextBlock Text="{Binding EstimatedCPUCost}" Grid.Row="2" Grid.Column="1" FontSize="11" Foreground="#263238" Margin="0,2"/>

                                                                <TextBlock Text="Subtree Cost:" Grid.Row="3" Grid.Column="0" FontSize="11" Foreground="#546E7A" Margin="0,2"/>
                                                                <TextBlock Text="{Binding EstimatedSubtreeCostStr}" Grid.Row="3" Grid.Column="1" FontSize="11" Foreground="#1976D2" FontWeight="SemiBold" Margin="0,2"/>
                                                            </Grid>

                                                            <!-- 数据库对象与详细属性 -->
                                                            <StackPanel Visibility="{Binding HasObjectDetails}" Margin="0,4,0,0">
                                                                <Separator Background="#ECEFF1" Margin="0,0,0,6"/>
                                                                <TextBlock Text="Object Details:" FontSize="10.5" FontWeight="Bold" Foreground="#37474F" Margin="0,0,0,2"/>
                                                                <TextBlock Text="{Binding ObjectDetails}" FontSize="11" Foreground="#004D40" TextWrapping="Wrap" Margin="0,0,0,6"/>
                                                            </StackPanel>

                                                            <StackPanel Visibility="{Binding HasPartitionInfo}">
                                                                <Separator Background="#ECEFF1" Margin="0,0,0,6"/>
                                                                <TextBlock Text="Partitioned: True" FontSize="10.5" FontWeight="Bold" Foreground="#37474F" Margin="0,0,0,2"/>
                                                                <StackPanel Orientation="Horizontal" Margin="0,0,0,2">
                                                                    <TextBlock Text="Partition Count: " FontSize="11" Foreground="#546E7A"/>
                                                                    <TextBlock Text="{Binding PartitionCount}" FontSize="11" Foreground="#263238" FontWeight="SemiBold"/>
                                                                </StackPanel>
                                                                <StackPanel Orientation="Horizontal" Margin="0,0,0,6">
                                                                    <TextBlock Text="Partition Range: " FontSize="11" Foreground="{Binding PartitionLabelColor}"/>
                                                                    <TextBlock Text="{Binding PartitionRange}" FontSize="11" Foreground="{Binding PartitionRangeColor}" FontWeight="SemiBold"/>
                                                                </StackPanel>
                                                            </StackPanel>

                                                            <StackPanel Visibility="{Binding HasOutputList}">
                                                                <Separator Background="#ECEFF1" Margin="0,0,0,6"/>
                                                                <TextBlock Text="Output List:" FontSize="10.5" FontWeight="Bold" Foreground="#37474F" Margin="0,0,0,2"/>
                                                                <TextBlock Text="{Binding OutputList}" FontSize="11" Foreground="#263238" TextWrapping="Wrap" MaxHeight="60" Margin="0,0,0,6"/>
                                                            </StackPanel>

                                                            <StackPanel Visibility="{Binding HasSeekPredicates}">
                                                                <Separator Background="#ECEFF1" Margin="0,0,0,6"/>
                                                                <TextBlock Text="Seek Predicates:" FontSize="10.5" FontWeight="Bold" Foreground="#37474F" Margin="0,0,0,2"/>
                                                                <TextBlock Text="{Binding SeekPredicates}" FontSize="11" Foreground="#E65100" TextWrapping="Wrap" MaxHeight="60" Margin="0,0,0,6"/>
                                                            </StackPanel>

                                                            <StackPanel Visibility="{Binding HasPredicate}">
                                                                <Separator Background="#ECEFF1" Margin="0,0,0,6"/>
                                                                <TextBlock Text="Residual Predicate:" FontSize="10.5" FontWeight="Bold" Foreground="#37474F" Margin="0,0,0,2"/>
                                                                <TextBlock Text="{Binding Predicate}" FontSize="11" Foreground="#D84315" TextWrapping="Wrap" MaxHeight="60" Margin="0,0,0,6"/>
                                                            </StackPanel>

                                                            <!-- 警告信息 -->
                                                            <StackPanel Visibility="{Binding HasWarningVisible}" Margin="0,6,0,0">
                                                                <Border Background="#FFF3E0" BorderBrush="#FFB74D" BorderThickness="1" CornerRadius="4" Padding="8">
                                                                    <StackPanel>
                                                                        <TextBlock Text="⚠ 性能警告 (Warnings):" FontSize="11" FontWeight="Bold" Foreground="#E65100"/>
                                                                        <TextBlock Text="{Binding Warnings}" FontSize="11" Foreground="#BF360C" TextWrapping="Wrap" Margin="0,4,0,0"/>
                                                                    </StackPanel>
                                                                </Border>
                                                            </StackPanel>
                                                        </StackPanel>
                                                    </Border>
                                                </StackPanel>
                                            </Border>
                                        </ToolTip>
                                    </Border.ToolTip>

                                    <!-- 重新设计的内部布局: 顶部色带 (DynamicBackgroundBrush) + 底部主体 -->
                                    <Grid>
                                        <Grid.RowDefinitions>
                                            <RowDefinition Height="4" /> <!-- 顶部状态色条 -->
                                            <RowDefinition Height="Auto" /> <!-- 头部信息 -->
                                            <RowDefinition Height="*" /> <!-- 底部数据 -->
                                        </Grid.RowDefinitions>
                                        
                                        <!-- 状态指示条 (Cost 色或 Warning 色) -->
                                        <Border Grid.Row="0" Background="{Binding DynamicBackgroundBrush}" CornerRadius="6,6,0,0" />
                                        
                                        <!-- 警告与并行标记悬浮在右上角 -->
                                        <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Right" VerticalAlignment="Top" Margin="0,4,4,0" Panel.ZIndex="5">
                                            <Border Width="16" Height="16" CornerRadius="8" Background="{StaticResource CriticalBrush}"
                                                    Visibility="{Binding HasWarningVisible}" ToolTip="{Binding Warnings}">
                                                <TextBlock Text="!" Foreground="White" FontSize="11" FontWeight="Bold" 
                                                           HorizontalAlignment="Center" VerticalAlignment="Center" Margin="0,-1,0,0"/>
                                            </Border>
                                            <materialDesign:PackIcon Kind="LightningBolt" Width="14" Height="14" Foreground="{StaticResource WarningBrush}" Visibility="{Binding IsParallelVisible}" Margin="2,1,0,0"/>
                                        </StackPanel>

                                        <!-- Header 区: 图标与名称 -->
                                        <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="8,8,24,6">
                                            <!-- 图标 -->
                                            <Viewbox Width="20" Height="20" VerticalAlignment="Center">
                                                <Path Data="{Binding IconGeometry}" Fill="{Binding IconBrush}" Stretch="Uniform" />
                                            </Viewbox>
                                            
                                            <!-- 名称 -->
                                            <StackPanel Margin="6,0,0,0" VerticalAlignment="Center">
                                                <TextBlock Text="{Binding PhysicalOp}" FontSize="12" FontWeight="SemiBold" Foreground="{StaticResource TextBrush}" TextTrimming="CharacterEllipsis" MaxWidth="100" ToolTip="{Binding PhysicalOp}"/>
                                                <TextBlock Text="{Binding LogicalOpSuffix}" FontSize="10" Foreground="{StaticResource SecondaryTextBrush}" Visibility="{Binding HasExtraInfo}"/>
                                            </StackPanel>
                                        </StackPanel>
                                        
                                        <!-- 分隔线 -->
                                        <Border Grid.Row="2" BorderBrush="{StaticResource BorderBrush}" BorderThickness="0,1,0,0">
                                            <Grid Background="#FAFAFA" Margin="0">
                                                <Grid.RowDefinitions>
                                                    <RowDefinition Height="Auto"/>
                                                    <RowDefinition Height="Auto"/>
                                                </Grid.RowDefinitions>
                                                <!-- 数据指标: 成本占比 -->
                                                <TextBlock Grid.Row="0" Text="{Binding PrimaryDisplayValue}" FontSize="14" FontWeight="Bold" Foreground="{StaticResource TextBrush}" HorizontalAlignment="Center" Margin="0,6,0,2"/>
                                                <!-- 对象名称 -->
                                                <TextBlock Grid.Row="1" Text="{Binding ObjectDetails}" 
                                                           FontSize="9.5" Foreground="{StaticResource SecondaryTextBrush}" 
                                                           HorizontalAlignment="Center" TextAlignment="Center"
                                                           MaxWidth="130" TextWrapping="Wrap" TextTrimming="WordEllipsis" MaxHeight="30" Margin="4,0,4,6"
                                                           Visibility="{Binding HasObjectDetails}"/>
                                            </Grid>
                                        </Border>
                                    </Grid>
                                </Border>
                                
                                <!-- 折叠/展开按钮 (悬浮在右下角) -->
                                <Button Content="{Binding CollapseButtonText}" 
                                        Visibility="{Binding CollapseButtonVisibility}"
                                        PreviewMouseLeftButtonDown="ToggleCollapse_PreviewMouseDown"
                                        Panel.ZIndex="9999"
                                        Width="20" Height="20" FontSize="16" Padding="0,-2,0,0"
                                        FontWeight="Bold"
                                        HorizontalAlignment="Right" VerticalAlignment="Bottom"
                                        Margin="0,0,-10,-10" 
                                        Background="White" Foreground="{StaticResource InfoBrush}" BorderBrush="{StaticResource BorderBrush}" BorderThickness="1"
                                        Cursor="Hand"
                                        ToolTip="折叠/展开子树">
                                    <Button.Resources>
                                        <Style TargetType="Border">
                                            <Setter Property="CornerRadius" Value="10"/>
                                            <Setter Property="Effect">
                                                <Setter.Value>
                                                    <DropShadowEffect Color="#000000" BlurRadius="4" ShadowDepth="1" Opacity="0.15"/>
                                                </Setter.Value>
                                            </Setter>
                                        </Style>
                                    </Button.Resources>
                                </Button>
                            </Grid>
                        </DataTemplate>'''

start_idx = c.find('<DataTemplate DataType="{x:Type local:PlanNodeViewModel}">')
end_idx = c.find('</DataTemplate>', start_idx) + len('</DataTemplate>')

if start_idx != -1 and end_idx != -1:
    c = c[:start_idx] + new_template + c[end_idx:]
    with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'w', encoding='utf-8') as f:
        f.write(c)
    print("Replaced DataTemplate successfully.")
else:
    print("Could not find DataTemplate")
