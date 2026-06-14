import sys

with open("PlanGraphControl.xaml.cs", "r", encoding="utf-8") as f:
    content = f.read()

old_update = """            // Apply visibility EXACTLY ONCE to avoid duplicate PropertyChanged events and layout thrashing
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

new_update = """            // 彻底的重建策略：不再依赖 WPF 虚拟化或内部状态
            // 由于 WPF 的 Dispatcher 是单线程且批处理渲染的，Clear() 和 Add() 都在同一个同步上下文中执行，不会造成屏幕闪烁。
            // 这种方式能 100% 确保 Nodify 的内部引擎树（容器、连接、坐标）被完美且干净地重建。
            
            // 1. 先清空所有的连线和节点，确保依赖关系断开
            Connections.Clear();
            Nodes.Clear();

            // 2. 仅添加需要显示的节点
            foreach (var n in visibleNodeVms)
            {
                n.IsVisible = true; // 确保可见性属性也保持一致
                Nodes.Add(n);
            }

            // 3. 仅添加需要显示的连线
            foreach (var c in visibleConnVms)
            {
                c.IsVisible = true;
                Connections.Add(c);
            }
        }"""

content = content.replace(old_update, new_update)

with open("PlanGraphControl.xaml.cs", "w", encoding="utf-8") as f:
    f.write(content)
print("Patch 5 applied.")
