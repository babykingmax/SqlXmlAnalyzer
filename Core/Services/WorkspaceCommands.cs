using System.Windows.Input;

namespace SqlXmlAnalyzer.Core.Services;

public static class WorkspaceCommands
{
    private static RoutedUICommand Create(string name, string text, Key key = Key.None, ModifierKeys modifiers = ModifierKeys.None) =>
        new(text, name, typeof(WorkspaceCommands), key == Key.None ? new InputGestureCollection() : new InputGestureCollection { new KeyGesture(key, modifiers) });
    public static RoutedUICommand OpenPlan { get; } = Create(nameof(OpenPlan), "打开执行计划", Key.O, ModifierKeys.Control);
    public static RoutedUICommand OpenDeadlock { get; } = Create(nameof(OpenDeadlock), "打开死锁", Key.O, ModifierKeys.Control | ModifierKeys.Shift);
    public static RoutedUICommand ExportHtml { get; } = Create(nameof(ExportHtml), "导出 HTML", Key.R, ModifierKeys.Control);
    public static RoutedUICommand ExportWord { get; } = Create(nameof(ExportWord), "导出 Word");
    public static RoutedUICommand ExportPdf { get; } = Create(nameof(ExportPdf), "导出 PDF");
    public static RoutedUICommand ExportPlan { get; } = Create(nameof(ExportPlan), "导出脱敏计划");
    public static RoutedUICommand CopyResults { get; } = Create(nameof(CopyResults), "复制结果", Key.C, ModifierKeys.Control | ModifierKeys.Shift);
    public static RoutedUICommand Clear { get; } = Create(nameof(Clear), "清空结果", Key.L, ModifierKeys.Control);
    public static RoutedUICommand About { get; } = Create(nameof(About), "关于", Key.F1);
    public static RoutedUICommand Help { get; } = Create(nameof(Help), "快捷键与显示选项", Key.F1, ModifierKeys.Control);
    public static RoutedUICommand Configuration { get; } = Create(nameof(Configuration), "规则配置", Key.OemComma, ModifierKeys.Control);
    public static RoutedUICommand ReanalyzePlan { get; } = Create(nameof(ReanalyzePlan), "重新分析当前计划");
    public static RoutedUICommand DiagnosticPackage { get; } = Create(nameof(DiagnosticPackage), "本地诊断数据包");
    public static RoutedUICommand NextIssue { get; } = Create(nameof(NextIssue), "下一个问题", Key.F8);
    public static RoutedUICommand PreviousIssue { get; } = Create(nameof(PreviousIssue), "上一个问题", Key.F8, ModifierKeys.Shift);
    public static RoutedUICommand Evidence { get; } = Create(nameof(Evidence), "查看证据", Key.E, ModifierKeys.Control | ModifierKeys.Shift);
    public static RoutedUICommand Graph { get; } = Create(nameof(Graph), "聚焦图节点", Key.G, ModifierKeys.Control | ModifierKeys.Shift);
    public static RoutedUICommand Compare { get; } = Create(nameof(Compare), "比较工作区", Key.D3, ModifierKeys.Alt);
    public static RoutedUICommand Plan { get; } = Create(nameof(Plan), "执行计划工作区", Key.D2, ModifierKeys.Alt);
    public static RoutedUICommand Deadlock { get; } = Create(nameof(Deadlock), "死锁工作区", Key.D1, ModifierKeys.Alt);
}
