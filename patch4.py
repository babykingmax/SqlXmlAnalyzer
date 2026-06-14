import sys

with open("PlanGraphControl.xaml.cs", "r", encoding="utf-8") as f:
    content = f.read()

# Fix 1: Optimize UpdateGraphVisibility
old_update = """        private void UpdateGraphVisibility()
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

            var visibleNodeVms = new HashSet<PlanNodeViewModel>();
            var visibleConnVms = new HashSet<ConnectionViewModel>();

            // Traverse and calculate which nodes/connections should be visible
            void Traverse(XElement el, bool isVisible)
            {
                if (nodeMap.TryGetValue(el, out var vm))
                {
                    if (isVisible) visibleNodeVms.Add(vm);
                    bool childrenVisible = isVisible && !vm.IsCollapsed;

                    var children = PlanDiagnosticAnalyzer.GetDirectChildRelOps(el, _currentNs).ToList();
                    
                    foreach (var child in children)
                    {
                        if (nodeMap.TryGetValue(child, out var childVm))
                        {
                            var conn = _masterConnections.FirstOrDefault(c => c.Source == childVm && c.Target == vm);
                            if (conn != null)
                            {
                                if (childrenVisible) visibleConnVms.Add(conn);
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

            // Apply visibility EXACTLY ONCE to avoid duplicate PropertyChanged events and layout thrashing
            foreach(var n in _masterNodes)
            {
                n.IsVisible = visibleNodeVms.Contains(n);
            }
            foreach(var c in _masterConnections)
            {
                c.IsVisible = visibleConnVms.Contains(c);
            }

            // Ensure Nodes and Connections are populated
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

# Fix 2: Optimize IsVisible properties
old_isvis_node = """        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                _isVisible = value;
                OnPropertyChanged(nameof(IsVisible));
            }
        }"""

new_isvis_node = """        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible == value) return;
                _isVisible = value;
                OnPropertyChanged(nameof(IsVisible));
            }
        }"""

content = content.replace(old_isvis_node, new_isvis_node)

old_isvis_conn = """        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                _isVisible = value;
                OnPropertyChanged(nameof(IsVisible));
            }
        }"""

content = content.replace(old_isvis_conn, new_isvis_node)

# Fix 3: Recursive expand on [+] click
old_toggle = """        private void ToggleCollapse_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PlanNodeViewModel node)
            {
                node.IsCollapsed = !node.IsCollapsed;
                UpdateGraphVisibility();
                ReapplyLayout();
            }
        }"""

new_toggle = """        private void ToggleCollapse_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PlanNodeViewModel node)
            {
                if (node.IsCollapsed)
                {
                    // If manually expanding, forcibly expand ALL descendants
                    if (node.RawElement != null && _currentNs != null)
                    {
                        var nodeMap = new Dictionary<XElement, PlanNodeViewModel>();
                        foreach (var n in _masterNodes)
                        {
                            if (n.RawElement != null) nodeMap[n.RawElement] = n;
                        }

                        void ExpandAllDescendants(XElement el)
                        {
                            if (nodeMap.TryGetValue(el, out var vm))
                            {
                                vm.IsCollapsed = false;
                            }
                            var children = PlanDiagnosticAnalyzer.GetDirectChildRelOps(el, _currentNs).ToList();
                            foreach (var child in children)
                            {
                                ExpandAllDescendants(child);
                            }
                        }
                        ExpandAllDescendants(node.RawElement);
                    }
                    else
                    {
                        node.IsCollapsed = false;
                    }
                }
                else
                {
                    node.IsCollapsed = true;
                }

                UpdateGraphVisibility();
                ReapplyLayout();
            }
        }"""

content = content.replace(old_toggle, new_toggle)

with open("PlanGraphControl.xaml.cs", "w", encoding="utf-8") as f:
    f.write(content)
print("Patch 4 applied.")
