import sys
import re

# Update C# logic
with open("PlanGraphControl.xaml.cs", "r", encoding="utf-8") as f:
    content = f.read()

old_update = """        private void UpdateGraphVisibility()
        {
            if (_currentDoc == null || _currentNs == null || _masterNodes.Count == 0) return;

            var relOps = _currentDoc.Descendants(_currentNs + "RelOp").ToList();
            var roots = relOps.Where(r => !relOps.Any(p => PlanDiagnosticAnalyzer.GetDirectChildRelOps(p, _currentNs).Contains(r))).ToList();

            var nodeMap = new Dictionary<XElement, PlanNodeViewModel>();
            foreach (var node in _masterNodes)
            {
                if (node.RawElement != null) nodeMap[node.RawElement] = node;
            }

            var visibleNodes = new HashSet<PlanNodeViewModel>();
            var visibleConnections = new HashSet<ConnectionViewModel>();

            // Traverse and show
            void Traverse(XElement el, bool isVisible)
            {
                if (nodeMap.TryGetValue(el, out var vm))
                {
                    vm.IsVisible = isVisible;
                    if (isVisible) visibleNodes.Add(vm);

                    bool childrenVisible = isVisible && !vm.IsCollapsed;

                    var children = PlanDiagnosticAnalyzer.GetDirectChildRelOps(el, _currentNs).ToList();
                    
                    foreach (var child in children)
                    {
                        if (nodeMap.TryGetValue(child, out var childVm))
                        {
                            var conn = _masterConnections.FirstOrDefault(c => c.Source == childVm && c.Target == vm);
                            if (conn != null)
                            {
                                conn.IsVisible = childrenVisible;
                                if (childrenVisible) visibleConnections.Add(conn);
                            }
                        }
                        Traverse(child, childrenVisible);
                    }
                }
            }

            foreach (var root in roots)
            {
                Traverse(root, true);
            }

            // 修复：必须先移除 Connection，再移除 Node，以防止图形引擎抛出孤立连接异常
            var connToRemove = Connections.Where(c => !visibleConnections.Contains(c)).ToList();
            foreach (var c in connToRemove) Connections.Remove(c);

            var nodesToRemove = Nodes.Where(n => !visibleNodes.Contains(n)).ToList();
            foreach (var n in nodesToRemove) Nodes.Remove(n);

            // 添加时必须先添加 Node，再添加 Connection
            var nodesToAdd = visibleNodes.Where(n => !Nodes.Contains(n)).ToList();
            foreach (var n in nodesToAdd) Nodes.Add(n);

            var connToAdd = visibleConnections.Where(c => !Connections.Contains(c)).ToList();
            foreach (var c in connToAdd) Connections.Add(c);
        }"""

new_update = """        private void UpdateGraphVisibility()
        {
            if (_currentDoc == null || _currentNs == null || _masterNodes.Count == 0) return;

            var relOps = _currentDoc.Descendants(_currentNs + "RelOp").ToList();
            var roots = relOps.Where(r => !relOps.Any(p => PlanDiagnosticAnalyzer.GetDirectChildRelOps(p, _currentNs).Contains(r))).ToList();

            var nodeMap = new Dictionary<XElement, PlanNodeViewModel>();
            foreach (var node in _masterNodes)
            {
                if (node.RawElement != null) nodeMap[node.RawElement] = node;
                node.IsVisible = false; // default hide
            }
            foreach (var conn in _masterConnections) conn.IsVisible = false;

            // Traverse and show
            void Traverse(XElement el, bool isVisible)
            {
                if (nodeMap.TryGetValue(el, out var vm))
                {
                    vm.IsVisible = isVisible;
                    bool childrenVisible = isVisible && !vm.IsCollapsed;

                    var children = PlanDiagnosticAnalyzer.GetDirectChildRelOps(el, _currentNs).ToList();
                    
                    foreach (var child in children)
                    {
                        if (nodeMap.TryGetValue(child, out var childVm))
                        {
                            var conn = _masterConnections.FirstOrDefault(c => c.Source == childVm && c.Target == vm);
                            if (conn != null)
                            {
                                conn.IsVisible = childrenVisible;
                            }
                        }
                        Traverse(child, childrenVisible);
                    }
                }
            }

            foreach (var root in roots)
            {
                Traverse(root, true);
            }

            // 恢复最稳定状态：不再执行 Collection 的 Add/Remove，完全依赖 VirtualizingPanel.IsVirtualizing="False" 和 Visibility 绑定！
            // 确保 Nodes 拥有所有的元素
            if (Nodes.Count != _masterNodes.Count)
            {
                Nodes.Clear();
                foreach(var n in _masterNodes) Nodes.Add(n);
            }
            if (Connections.Count != _masterConnections.Count)
            {
                Connections.Clear();
                foreach(var c in _masterConnections) Connections.Add(c);
            }
        }"""

content = content.replace(old_update, new_update)

with open("PlanGraphControl.xaml.cs", "w", encoding="utf-8") as f:
    f.write(content)


# Update XAML
with open("PlanGraphControl.xaml", "r", encoding="utf-8") as f:
    xaml = f.read()

old_nodify = """<nodify:NodifyEditor x:Name="Editor"
                                     ItemsSource="{Binding Nodes}"
                                     Connections="{Binding Connections}\""""

new_nodify = """<nodify:NodifyEditor x:Name="Editor"
                                     VirtualizingPanel.IsVirtualizing="False"
                                     ItemsSource="{Binding Nodes}"
                                     Connections="{Binding Connections}\""""

if 'VirtualizingPanel.IsVirtualizing="False"' not in xaml:
    xaml = xaml.replace(old_nodify, new_nodify)

with open("PlanGraphControl.xaml", "w", encoding="utf-8") as f:
    f.write(xaml)

print("Patch 3 applied.")
