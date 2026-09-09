using System;
using System.Windows;
using System.Windows.Input;

namespace SqlXmlAnalyzer
{
    public partial class MainWindow
    {
        /// <summary>
        /// 注册全局键盘快捷键。所有快捷键复用导航栏既有的点击处理器，
        /// 行为与点击对应按钮完全一致。选用的组合键均非 TextBox 默认编辑键，
        /// 避免在文本框输入时被误触发。
        /// </summary>
        private void WireKeyboardShortcuts()
        {
            RegisterShortcut(Key.O, ModifierKeys.Control, () => OpenPlanFile_Click(this, new RoutedEventArgs()));                     // 打开执行计划
            RegisterShortcut(Key.O, ModifierKeys.Control | ModifierKeys.Shift, () => OpenDeadlockFile_Click(this, new RoutedEventArgs())); // 打开死锁文件
            RegisterShortcut(Key.R, ModifierKeys.Control, () => GenerateHtmlReport_Click(this, new RoutedEventArgs()));               // 导出 HTML 报告
            RegisterShortcut(Key.C, ModifierKeys.Control | ModifierKeys.Shift, () => CopyAnalysisResult_Click(this, new RoutedEventArgs())); // 复制分析结果
            RegisterShortcut(Key.L, ModifierKeys.Control, () => ClearResults_Click(this, new RoutedEventArgs()));                     // 清空所有结果
            RegisterShortcut(Key.F1, ModifierKeys.None, () => About_Click(this, new RoutedEventArgs()));                              // 关于
        }

        private void RegisterShortcut(Key key, ModifierKeys modifiers, Action action)
        {
            var command = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(command, (_, _) => action()));
            InputBindings.Add(new KeyBinding(command, key, modifiers));
        }
    }
}
