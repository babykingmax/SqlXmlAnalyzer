import re

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'r', encoding='utf-8') as f:
    c = f.read()

# 1. Redesign ContextMenu
old_menu_pattern = r'<ContextMenu Background="#FAFAFA" BorderBrush="#CFD8DC" BorderThickness="1">\s*<MenuItem Header="📋 复制节点所有信息 \(Copy All Details\)" Click="CopyNodeInfo_Click" FontSize="13" Padding="8,4"/>\s*</ContextMenu>'
new_menu = """<ContextMenu Style="{DynamicResource MaterialDesignContextMenu}">
                                        <MenuItem Header="复制节点全部信息" Click="CopyNodeInfo_Click" FontSize="13" Padding="12,6">
                                            <MenuItem.Icon>
                                                <materialDesign:PackIcon Kind="ContentCopy" Width="16" Height="16" Foreground="{DynamicResource AccentBrush}"/>
                                            </MenuItem.Icon>
                                        </MenuItem>
                                    </ContextMenu>"""
c = re.sub(old_menu_pattern, new_menu, c)

# 2. Redesign ToastPopup to be a sleek dark pill at the bottom
old_popup_pattern = r'<Popup x:Name="ToastPopup".*?</Popup>'
new_popup = """<Popup x:Name="ToastPopup" Placement="Bottom" VerticalOffset="-80" HorizontalOffset="0" AllowsTransparency="True" StaysOpen="False" IsHitTestVisible="False" PopupAnimation="Slide">
            <Border Background="#2C2C2C" CornerRadius="6" Padding="16,10" Margin="10">
                <Border.Effect>
                    <DropShadowEffect Color="#000000" BlurRadius="12" ShadowDepth="4" Opacity="0.4"/>
                </Border.Effect>
                <StackPanel Orientation="Horizontal">
                    <materialDesign:PackIcon Kind="CheckCircle" Foreground="#4CAF50" Width="18" Height="18" VerticalAlignment="Center" Margin="0,0,8,0"/>
                    <TextBlock Text="节点信息已复制到剪贴板" Foreground="#FAFAFA" FontSize="13" FontWeight="Normal" VerticalAlignment="Center" Margin="0,0,4,0"/>
                </StackPanel>
            </Border>
        </Popup>"""
c = re.sub(old_popup_pattern, new_popup, c, flags=re.DOTALL)

with open('E:/SqlXmlAnalyzer/PlanGraphControl.xaml', 'w', encoding='utf-8') as f:
    f.write(c)

print("Redesigned UI applied.")
