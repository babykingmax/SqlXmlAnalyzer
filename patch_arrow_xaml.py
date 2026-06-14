import re

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'r', encoding='utf-8') as f:
    c = f.read()

# Add the explicit Arrow Polygon to the ConnectionTemplate Grid
old_grid_end = """                                        <TextBlock Text="{Binding LabelText}" FontSize="10" FontWeight="Bold" Foreground="{DynamicResource TextBrush}" VerticalAlignment="Center"/>
                                    </StackPanel>
                                </Border>
                            </Grid>
                        </DataTemplate>
                    </nodify:NodifyEditor.ConnectionTemplate>"""

new_grid_end = """                                        <TextBlock Text="{Binding LabelText}" FontSize="10" FontWeight="Bold" Foreground="{DynamicResource TextBrush}" VerticalAlignment="Center"/>
                                    </StackPanel>
                                </Border>
                                
                                <!-- 5. 绝对置顶的真实独立箭头层 -->
                                <Polygon Points="{Binding ArrowPoints}" Fill="{Binding StrokeBrush}" 
                                         HorizontalAlignment="Left" VerticalAlignment="Top"
                                         IsHitTestVisible="False" Panel.ZIndex="20">
                                    <Polygon.RenderTransform>
                                        <TranslateTransform X="{Binding ArrowTransformX}" Y="{Binding ArrowTransformY}"/>
                                    </Polygon.RenderTransform>
                                </Polygon>
                            </Grid>
                        </DataTemplate>
                    </nodify:NodifyEditor.ConnectionTemplate>"""

c = c.replace(old_grid_end, new_grid_end)

# Remove the ArrowEnds and ArrowSize from nodify:LineConnection since we are drawing our own!
c = c.replace('ArrowEnds="End"', '')
c = c.replace('ArrowSize="18,18"', '')

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'w', encoding='utf-8') as f:
    f.write(c)

print("XAML Polygon arrow added.")
