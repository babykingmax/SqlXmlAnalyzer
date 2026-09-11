using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer;

public sealed class AccessiblePlanNode : ContentControl
{
    public AccessiblePlanNode()
    {
        Focusable = true;
        SetResourceReference(FocusVisualStyleProperty, SystemParameters.FocusVisualStyleKey);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Once);
        GotKeyboardFocus += (_, e) =>
        {
            if (ReferenceEquals(e.NewFocus, this) && DataContext is PlanNodeViewModel node) Graph()?.SelectKeyboardNode(node);
        };
    }
    internal PlanGraphControl? Graph()
    {
        DependencyObject? element = this;
        while (element != null && element is not PlanGraphControl) element = VisualTreeHelper.GetParent(element);
        return element as PlanGraphControl;
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new NodePeer(this);
    private sealed class NodePeer(AccessiblePlanNode owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override string GetClassNameCore() => nameof(AccessiblePlanNode);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
        protected override string GetNameCore() => owner.DataContext is PlanNodeViewModel node
            ? $"{node.Identity?.QueryPlan.Statement}，Node {node.NodeId}，{node.PhysicalOp}，诊断 {node.Diagnostics?.Diagnostics.Count ?? 0} 项" : "执行计划节点";
        protected override string GetHelpTextCore() => "方向键选择节点，Enter 查看节点详情，F8 定位问题，Ctrl+Shift+E 查看原始证据。";
        public override object? GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);
        public void Invoke()
        {
            if (!owner.IsEnabled) throw new ElementNotEnabledException();
            owner.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (owner.DataContext is PlanNodeViewModel node && owner.Graph() is { } graph)
                    { graph.SelectKeyboardNode(node); owner.Focus(); graph.OpenSelectedNode(); }
                }
                catch (Exception exception) { Services.WorkspaceAccessibility.Report(exception, "NodeAutomation.Invoke", owner); }
            });
        }
    }
}
