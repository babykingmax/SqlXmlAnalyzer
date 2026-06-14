import sys
import re

with open("PlanGraphControl.xaml.cs", "r", encoding="utf-8") as f:
    content = f.read()

# 1. Add _masterNodes and _masterConnections
content = content.replace(
    "private XNamespace? _currentNs;",
    "private XNamespace? _currentNs;\n        private List<PlanNodeViewModel> _masterNodes = new();\n        private List<ConnectionViewModel> _masterConnections = new();"
)

# 2. Update LoadFromExecutionPlan
old_load_end = """            // 添加到集?(Nodify 会自动响?
            foreach (var n in allNodes) Nodes.Add(n);"""
new_load_end = """            _masterNodes = allNodes;
            _masterConnections = Connections.ToList();

            // 添加到集?(Nodify 会自动响?
            foreach (var n in allNodes) Nodes.Add(n);"""
content = content.replace(old_load_end, new_load_end)

# Fallback for LoadFromExecutionPlan if the comment is mangled
if "_masterNodes = allNodes;" not in content:
    content = content.replace(
        "foreach (var n in allNodes) Nodes.Add(n);",
        "_masterNodes = allNodes;\n            _masterConnections = Connections.ToList();\n            foreach (var n in allNodes) Nodes.Add(n);"
    )

# 3. Update ExpandAll_Click
old_expand = """        private void ExpandAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var n in Nodes)
            {
                n.IsCollapsed = false;
            }
            UpdateGraphVisibility();
            ReapplyLayout();
        }"""
new_expand = """        private void ExpandAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var n in _masterNodes)
            {
                n.IsCollapsed = false;
            }
            UpdateGraphVisibility();
            ReapplyLayout();
        }"""
content = content.replace(old_expand, new_expand)

# 4. Update SmartCollapse_Click
content = content.replace(
    "if (_currentDoc == null || _currentNs == null || Nodes.Count == 0) return;",
    "if (_currentDoc == null || _currentNs == null || _masterNodes.Count == 0) return;",
    1 # only first match in case ReapplyLayout has it
)
content = content.replace(
    "double maxSubtreeCost = Nodes.Max(n => n.SubtreeCost);",
    "double maxSubtreeCost = _masterNodes.Max(n => n.SubtreeCost);"
)
content = content.replace(
    "foreach (var node in Nodes)\n            {\n                if (node.RawElement != null) nodeMap[node.RawElement] = node;\n                node.IsCollapsed = false; // 先全部展开\n            }",
    "foreach (var node in _masterNodes)\n            {\n                if (node.RawElement != null) nodeMap[node.RawElement] = node;\n                node.IsCollapsed = false; // 先全部展开\n            }"
)
content = content.replace(
    "foreach (var n in Nodes)\n            {\n                if (n.HasChildren && n.RawElement != null)",
    "foreach (var n in _masterNodes)\n            {\n                if (n.HasChildren && n.RawElement != null)"
)

# 5. Update UpdateGraphVisibility
old_update = """        private void UpdateGraphVisibility()
        {
            if (_currentDoc == null || _currentNs == null || Nodes.Count == 0) return;

            var relOps = _currentDoc.Descendants(_currentNs + "RelOp").ToList();
            var roots = relOps.Where(r => !relOps.Any(p => PlanDiagnosticAnalyzer.GetDirectChildRelOps(p, _currentNs).Contains(r))).ToList();

            var nodeMap = new Dictionary<XElement, PlanNodeViewModel>();
            foreach (var node in Nodes)
            {
                if (node.RawElement != null) nodeMap[node.RawElement] = node;
            }

            // By default, hide all
            foreach (var n in Nodes) n.IsVisible = false;
            foreach (var c in Connections) c.IsVisible = false;

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
                            var conn = Connections.FirstOrDefault(c => c.Source == childVm && c.Target == vm);
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

            var nodesToRemove = Nodes.Where(n => !visibleNodes.Contains(n)).ToList();
            foreach (var n in nodesToRemove) Nodes.Remove(n);

            var nodesToAdd = visibleNodes.Where(n => !Nodes.Contains(n)).ToList();
            foreach (var n in nodesToAdd) Nodes.Add(n);

            var connToRemove = Connections.Where(c => !visibleConnections.Contains(c)).ToList();
            foreach (var c in connToRemove) Connections.Remove(c);

            var connToAdd = visibleConnections.Where(c => !Connections.Contains(c)).ToList();
            foreach (var c in connToAdd) Connections.Add(c);
        }"""

content = content.replace(old_update, new_update)

# 6. Update ReapplyLayout
old_reapply = """        private void ReapplyLayout()
        {
            if (_currentDoc == null || _currentNs == null || Nodes.Count == 0) return;

            var relOps = _currentDoc.Descendants(_currentNs + "RelOp").ToList();
            if (relOps.Count == 0) return;

            var nodeMap = new Dictionary<XElement, PlanNodeViewModel>();
            foreach (var node in Nodes)
            {
                if (node.RawElement != null)
                {
                    nodeMap[node.RawElement] = node;
                }
            }

            ApplyLayeredLayout(Nodes.ToList(), nodeMap, relOps, _currentNs);

            foreach (var conn in Connections)
            {
                conn.LayoutMode = LayoutMode;
            }
        }"""

new_reapply = """        private void ReapplyLayout()
        {
            if (_currentDoc == null || _currentNs == null || _masterNodes.Count == 0) return;

            var relOps = _currentDoc.Descendants(_currentNs + "RelOp").ToList();
            if (relOps.Count == 0) return;

            var nodeMap = new Dictionary<XElement, PlanNodeViewModel>();
            foreach (var node in _masterNodes)
            {
                if (node.RawElement != null)
                {
                    nodeMap[node.RawElement] = node;
                }
            }

            ApplyLayeredLayout(_masterNodes, nodeMap, relOps, _currentNs);

            foreach (var conn in _masterConnections)
            {
                conn.LayoutMode = LayoutMode;
            }
        }"""

content = content.replace(old_reapply, new_reapply)

with open("PlanGraphControl.xaml.cs", "w", encoding="utf-8") as f:
    f.write(content)
print("Patch applied.")
