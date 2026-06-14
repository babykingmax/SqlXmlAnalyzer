import re

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'r', encoding='utf-8') as f:
    c = f.read()

# 1. Add Arrow PackIcon inside the badge (replacing just the TextBlock)
old_badge_content = '<TextBlock Text="{Binding LabelText}" FontSize="10" FontWeight="Bold" Foreground="{DynamicResource TextBrush}"/>'
new_badge_content = """<StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                                        <materialDesign:PackIcon Kind="ArrowRight" Foreground="{Binding StrokeBrush}" Width="14" Height="14" VerticalAlignment="Center" Margin="0,0,4,0" RenderTransformOrigin="0.5,0.5">
                                            <materialDesign:PackIcon.RenderTransform>
                                                <RotateTransform Angle="{Binding ArrowAngle}"/>
                                            </materialDesign:PackIcon.RenderTransform>
                                        </materialDesign:PackIcon>
                                        <TextBlock Text="{Binding LabelText}" FontSize="10" FontWeight="Bold" Foreground="{DynamicResource TextBrush}" VerticalAlignment="Center"/>
                                    </StackPanel>"""
c = c.replace(old_badge_content, new_badge_content)

# 2. Also style the ContextMenu a bit to make it look nicer natively
old_menu = """<ContextMenu>
                                        <MenuItem Header="📋 复制节点所有信息 (Copy All Details)" Click="CopyNodeInfo_Click"/>
                                    </ContextMenu>"""
new_menu = """<ContextMenu Background="#FAFAFA" BorderBrush="#CFD8DC" BorderThickness="1">
                                        <MenuItem Header="📋 复制节点所有信息 (Copy All Details)" Click="CopyNodeInfo_Click" FontSize="13" Padding="8,4"/>
                                    </ContextMenu>"""
c = c.replace(old_menu, new_menu)

# 3. Add Toast Notification Popup at the end of the root Grid
popup_xaml = """
        <!-- 漂亮的 Toast 提示窗 -->
        <Popup x:Name="ToastPopup" Placement="Center" AllowsTransparency="True" StaysOpen="False" IsHitTestVisible="False">
            <Border Background="#E8F5E9" BorderBrush="#4CAF50" BorderThickness="1" CornerRadius="8" Padding="16,12" Margin="10">
                <Border.Effect>
                    <DropShadowEffect Color="#000000" BlurRadius="10" ShadowDepth="3" Opacity="0.15"/>
                </Border.Effect>
                <StackPanel Orientation="Horizontal">
                    <materialDesign:PackIcon Kind="CheckCircleOutline" Foreground="#4CAF50" Width="20" Height="20" VerticalAlignment="Center" Margin="0,0,8,0"/>
                    <TextBlock Text="节点详细信息已成功复制！" Foreground="#2E7D32" FontSize="14" FontWeight="SemiBold" VerticalAlignment="Center"/>
                </StackPanel>
            </Border>
        </Popup>
    </Grid>
</UserControl>"""

# Replace the last `</Grid>\n</UserControl>` with the popup
c = re.sub(r'</Grid>\s*</UserControl>', popup_xaml, c)

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'w', encoding='utf-8') as f:
    f.write(c)

print("XAML updated.")
