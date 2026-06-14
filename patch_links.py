import re

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'r', encoding='utf-8') as f:
    content = f.read()

# Replace ToolTip
tooltip_pattern = re.compile(r'<Border\.ToolTip>.*?</Border\.ToolTip>', re.DOTALL)
new_tooltip = '''<Border.ToolTip>
                                        <ToolTip Background="Transparent" BorderThickness="0" Padding="0" Placement="Right">
                                            <Border Background="{StaticResource MaterialDesignPaper}" 
                                                    BorderBrush="{StaticResource PrimaryHueMidBrush}" BorderThickness="3,0,0,0"
                                                    CornerRadius="8" ClipToBounds="True" Margin="8">
                                                <Border.Effect>
                                                    <DropShadowEffect Color="#000000" BlurRadius="15" ShadowDepth="4" Opacity="0.15"/>
                                                </Border.Effect>
                                                <StackPanel Width="420">
                                                    <!-- 头部区域 (无缝深色背景) -->
                                                    <Border Background="{StaticResource PrimaryHueMidBrush}" Padding="16,12">
                                                        <Grid>
                                                            <Grid.ColumnDefinitions>
                                                                <ColumnDefinition Width="*"/>
                                                                <ColumnDefinition Width="Auto"/>
                                                            </Grid.ColumnDefinitions>
                                                            <StackPanel Grid.Column="0">
                                                                <TextBlock Text="{Binding PhysicalOp}" FontWeight="Bold" FontSize="15" Foreground="{StaticResource PrimaryHueMidForegroundBrush}" />
                                                                <TextBlock Text="{Binding LogicalOpSuffix}" FontSize="12" FontStyle="Italic" Foreground="{StaticResource PrimaryHueLightForegroundBrush}" Margin="0,2,0,0" Visibility="{Binding HasExtraInfo}"/>
                                                            </StackPanel>
                                                            <Border Grid.Column="1" Background="#33000000" CornerRadius="4" Padding="6,2" VerticalAlignment="Top">
                                                                <TextBlock Text="{Binding NodeId, StringFormat='Node {0}'}" FontSize="11" Foreground="{StaticResource PrimaryHueMidForegroundBrush}"/>
                                                            </Border>
                                                        </Grid>
                                                    </Border>
                                                    <!-- 主体数据区域 -->
                                                    <Border Padding="16">
                                                        <StackPanel>
                                                            <!-- 执行与行数信息 (两列网格排版) -->
                                                            <Grid Margin="0,0,0,12">
                                                                <Grid.ColumnDefinitions>
                                                                    <ColumnDefinition Width="140"/>
                                                                    <ColumnDefinition Width="*"/>
                                                                </Grid.ColumnDefinitions>
                                                                <Grid.RowDefinitions>
                                                                    <RowDefinition Height="Auto"/>
                                                                    <RowDefinition Height="Auto"/>
                                                                    <RowDefinition Height="Auto"/>
                                                                    <RowDefinition Height="Auto"/>
                                                                </Grid.RowDefinitions>
                                                                
                                                                <TextBlock Text="Executions (Act/Est):" Grid.Row="0" Grid.Column="0" FontSize="12" Foreground="{StaticResource SecondaryTextBrush}" Margin="0,3"/>
                                                                <StackPanel Orientation="Horizontal" Grid.Row="0" Grid.Column="1" Margin="0,3">
                                                                    <TextBlock Text="{Binding ActualExecutions}" FontSize="12" Foreground="{StaticResource TextBrush}" FontWeight="SemiBold"/>
                                                                    <TextBlock Text=" / " FontSize="12" Foreground="{StaticResource SecondaryTextBrush}"/>
                                                                    <TextBlock Text="{Binding EstimatedExecutions}" FontSize="12" Foreground="{StaticResource TextBrush}"/>
                                                                </StackPanel>

                                                                <TextBlock Text="Rows (Act/Est):" Grid.Row="1" Grid.Column="0" FontSize="12" Foreground="{StaticResource SecondaryTextBrush}" Margin="0,3"/>
                                                                <StackPanel Orientation="Horizontal" Grid.Row="1" Grid.Column="1" Margin="0,3">
                                                                    <TextBlock Text="{Binding ActualRows}" FontSize="12" Foreground="{StaticResource TextBrush}" FontWeight="SemiBold"/>
                                                                    <TextBlock Text=" / " FontSize="12" Foreground="{StaticResource SecondaryTextBrush}"/>
                                                                    <TextBlock Text="{Binding EstRows}" FontSize="12" Foreground="{StaticResource TextBrush}"/>
                                                                </StackPanel>

                                                                <TextBlock Text="Data Size (Est):" Grid.Row="2" Grid.Column="0" FontSize="12" Foreground="{StaticResource SecondaryTextBrush}" Margin="0,3"/>
                                                                <TextBlock Text="{Binding EstimatedDataSize}" Grid.Row="2" Grid.Column="1" FontSize="12" Foreground="{StaticResource TextBrush}" Margin="0,3"/>

                                                                <TextBlock Text="Execution Mode:" Grid.Row="3" Grid.Column="0" FontSize="12" Foreground="{StaticResource SecondaryTextBrush}" Margin="0,3"/>
                                                                <TextBlock Text="{Binding ExecutionMode}" Grid.Row="3" Grid.Column="1" FontSize="12" Foreground="{StaticResource TextBrush}" Margin="0,3"/>
                                                            </Grid>

                                                            <Separator Style="{StaticResource MaterialDesignLightSeparator}" Margin="0,0,0,12"/>

                                                            <!-- 成本信息 -->
                                                            <Grid Margin="0,0,0,12">
                                                                <Grid.ColumnDefinitions>
                                                                    <ColumnDefinition Width="140"/>
                                                                    <ColumnDefinition Width="*"/>
                                                                </Grid.ColumnDefinitions>
                                                                <Grid.RowDefinitions>
                                                                    <RowDefinition Height="Auto"/>
                                                                    <RowDefinition Height="Auto"/>
                                                                    <RowDefinition Height="Auto"/>
                                                                    <RowDefinition Height="Auto"/>
                                                                </Grid.RowDefinitions>
                                                                
                                                                <TextBlock Text="Operator Cost:" Grid.Row="0" Grid.Column="0" FontSize="12" Foreground="{StaticResource SecondaryTextBrush}" Margin="0,3"/>
                                                                <TextBlock Text="{Binding EstimatedOperatorCost}" Grid.Row="0" Grid.Column="1" FontSize="12" Foreground="{StaticResource TextBrush}" Margin="0,3"/>

                                                                <TextBlock Text="I/O Cost:" Grid.Row="1" Grid.Column="0" FontSize="12" Foreground="{StaticResource SecondaryTextBrush}" Margin="0,3"/>
                                                                <TextBlock Text="{Binding EstimatedIOCost}" Grid.Row="1" Grid.Column="1" FontSize="12" Foreground="{StaticResource TextBrush}" Margin="0,3"/>

                                                                <TextBlock Text="CPU Cost:" Grid.Row="2" Grid.Column="0" FontSize="12" Foreground="{StaticResource SecondaryTextBrush}" Margin="0,3"/>
                                                                <TextBlock Text="{Binding EstimatedCPUCost}" Grid.Row="2" Grid.Column="1" FontSize="12" Foreground="{StaticResource TextBrush}" Margin="0,3"/>

                                                                <TextBlock Text="Subtree Cost:" Grid.Row="3" Grid.Column="0" FontSize="12" Foreground="{StaticResource SecondaryTextBrush}" Margin="0,3"/>
                                                                <TextBlock Text="{Binding EstimatedSubtreeCostStr}" Grid.Row="3" Grid.Column="1" FontSize="12" Foreground="{StaticResource PrimaryHueMidBrush}" FontWeight="Bold" Margin="0,3"/>
                                                            </Grid>

                                                            <!-- 数据库对象与详细属性 -->
                                                            <StackPanel Visibility="{Binding HasObjectDetails}" Margin="0,4,0,0">
                                                                <Separator Style="{StaticResource MaterialDesignLightSeparator}" Margin="0,0,0,8"/>
                                                                <TextBlock Text="Object Details" FontSize="11" FontWeight="Bold" Foreground="{StaticResource PrimaryHueMidBrush}" Margin="0,0,0,4"/>
                                                                <TextBlock Text="{Binding ObjectDetails}" FontSize="12" Foreground="{StaticResource TextBrush}" TextWrapping="Wrap" Margin="0,0,0,8"/>
                                                            </StackPanel>

                                                            <StackPanel Visibility="{Binding HasPartitionInfo}">
                                                                <Separator Style="{StaticResource MaterialDesignLightSeparator}" Margin="0,0,0,8"/>
                                                                <TextBlock Text="Partitioning" FontSize="11" FontWeight="Bold" Foreground="{StaticResource PrimaryHueMidBrush}" Margin="0,0,0,4"/>
                                                                <StackPanel Orientation="Horizontal" Margin="0,0,0,2">
                                                                    <TextBlock Text="Partition Count: " FontSize="12" Foreground="{StaticResource SecondaryTextBrush}"/>
                                                                    <TextBlock Text="{Binding PartitionCount}" FontSize="12" Foreground="{StaticResource TextBrush}" FontWeight="SemiBold"/>
                                                                </StackPanel>
                                                                <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                                                                    <TextBlock Text="Partition Range: " FontSize="12" Foreground="{Binding PartitionLabelColor}"/>
                                                                    <TextBlock Text="{Binding PartitionRange}" FontSize="12" Foreground="{Binding PartitionRangeColor}" FontWeight="SemiBold"/>
                                                                </StackPanel>
                                                            </StackPanel>

                                                            <StackPanel Visibility="{Binding HasOutputList}">
                                                                <Separator Style="{StaticResource MaterialDesignLightSeparator}" Margin="0,0,0,8"/>
                                                                <TextBlock Text="Output List" FontSize="11" FontWeight="Bold" Foreground="{StaticResource PrimaryHueMidBrush}" Margin="0,0,0,4"/>
                                                                <TextBlock Text="{Binding OutputList}" FontSize="12" Foreground="{StaticResource TextBrush}" TextWrapping="Wrap" MaxHeight="80" Margin="0,0,0,8" TextTrimming="CharacterEllipsis"/>
                                                            </StackPanel>

                                                            <StackPanel Visibility="{Binding HasSeekPredicates}">
                                                                <Separator Style="{StaticResource MaterialDesignLightSeparator}" Margin="0,0,0,8"/>
                                                                <TextBlock Text="Seek Predicates" FontSize="11" FontWeight="Bold" Foreground="{StaticResource PrimaryHueMidBrush}" Margin="0,0,0,4"/>
                                                                <TextBlock Text="{Binding SeekPredicates}" FontSize="12" Foreground="{StaticResource SecondaryHueMidBrush}" TextWrapping="Wrap" MaxHeight="80" Margin="0,0,0,8" TextTrimming="CharacterEllipsis"/>
                                                            </StackPanel>

                                                            <StackPanel Visibility="{Binding HasPredicate}">
                                                                <Separator Style="{StaticResource MaterialDesignLightSeparator}" Margin="0,0,0,8"/>
                                                                <TextBlock Text="Residual Predicate" FontSize="11" FontWeight="Bold" Foreground="{StaticResource PrimaryHueMidBrush}" Margin="0,0,0,4"/>
                                                                <TextBlock Text="{Binding Predicate}" FontSize="12" Foreground="{StaticResource SecondaryHueMidBrush}" TextWrapping="Wrap" MaxHeight="80" Margin="0,0,0,8" TextTrimming="CharacterEllipsis"/>
                                                            </StackPanel>

                                                            <!-- 警告信息 -->
                                                            <StackPanel Visibility="{Binding HasWarningVisible}" Margin="0,8,0,0">
                                                                <Border Background="#FFF3E0" BorderBrush="#FFB74D" BorderThickness="1" CornerRadius="6" Padding="12">
                                                                    <StackPanel>
                                                                        <StackPanel Orientation="Horizontal" Margin="0,0,0,6">
                                                                            <materialDesign:PackIcon Kind="Alert" Foreground="#F57C00" Width="16" Height="16" VerticalAlignment="Center"/>
                                                                            <TextBlock Text="性能警告 (Warnings)" FontSize="12" FontWeight="Bold" Foreground="#F57C00" Margin="4,0,0,0" VerticalAlignment="Center"/>
                                                                        </StackPanel>
                                                                        <TextBlock Text="{Binding Warnings}" FontSize="12" Foreground="#D84315" TextWrapping="Wrap"/>
                                                                    </StackPanel>
                                                                </Border>
                                                            </StackPanel>
                                                        </StackPanel>
                                                    </Border>
                                                </StackPanel>
                                            </Border>
                                        </ToolTip>
                                    </Border.ToolTip>'''
content = tooltip_pattern.sub(new_tooltip, content)

# Replace ConnectionTemplate
conn_pattern = re.compile(r'<nodify:NodifyEditor\.ConnectionTemplate>.*?</nodify:NodifyEditor\.ConnectionTemplate>', re.DOTALL)
new_conn = '''<nodify:NodifyEditor.ConnectionTemplate>
                        <DataTemplate DataType="{x:Type local:ConnectionViewModel}">
                            <Grid Opacity="{Binding Opacity}" Visibility="{Binding IsVisible, Converter={StaticResource BoolToVis}}">
                                <!-- 1. 背景管道连接线 (展示行数对应的粗细，以及估算偏差颜色) -->
                                <nodify:Connection Source="{Binding SourceLocation}" 
                                                   Target="{Binding TargetLocation}"
                                                   Stroke="{Binding StrokeBrush}"
                                                   StrokeThickness="{Binding ThicknessValue}"
                                                   ArrowEnds="End"
                                                   ArrowSize="12,12"
                                                   ToolTip="{Binding ToolTipText}"/>

                                <!-- 2. 前景动画虚线 (实现类似商业 SQL Sentry Plan Explorer 的动态数据流流向效果) -->
                                <nodify:Connection Source="{Binding SourceLocation}" 
                                                   Target="{Binding TargetLocation}"
                                                   Stroke="{StaticResource PrimaryHueLightBrush}"
                                                   StrokeThickness="1.5"
                                                   StrokeDashArray="4,4"
                                                   ArrowEnds="None"
                                                   IsHitTestVisible="False"
                                                   Opacity="0.9">
                                    <nodify:Connection.Style>
                                        <Style TargetType="{x:Type nodify:Connection}">
                                            <Style.Triggers>
                                                <EventTrigger RoutedEvent="Loaded">
                                                    <BeginStoryboard>
                                                        <Storyboard>
                                                            <DoubleAnimation Storyboard.TargetProperty="StrokeDashOffset"
                                                                             From="100" To="0" Duration="0:0:3"
                                                                             RepeatBehavior="Forever"/>
                                                        </Storyboard>
                                                    </BeginStoryboard>
                                                </EventTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </nodify:Connection.Style>
                                </nodify:Connection>

                                <!-- 3. 数据流行数浮动标签 (精确居中于连接线中点) -->
                                <!-- 采用圆角胶囊样式，更贴合现代 UI -->
                                <Border Background="{StaticResource MaterialDesignPaper}" 
                                        BorderBrush="{Binding StrokeBrush}" 
                                        BorderThickness="1"
                                        CornerRadius="10" Padding="8,3"
                                        HorizontalAlignment="Left" VerticalAlignment="Top"
                                        IsHitTestVisible="False"
                                        Panel.ZIndex="10">
                                    <Border.RenderTransform>
                                        <TranslateTransform X="{Binding MidpointX}" Y="{Binding MidpointY}"/>
                                    </Border.RenderTransform>
                                    <Border.Effect>
                                        <DropShadowEffect Color="#000000" BlurRadius="6" ShadowDepth="2" Opacity="0.25"/>
                                    </Border.Effect>
                                    <TextBlock Text="{Binding LabelText}" FontSize="10" FontWeight="Bold" Foreground="{StaticResource TextBrush}"/>
                                </Border>
                            </Grid>
                        </DataTemplate>
                    </nodify:NodifyEditor.ConnectionTemplate>'''
content = conn_pattern.sub(new_conn, content)

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'w', encoding='utf-8') as f:
    f.write(content)
