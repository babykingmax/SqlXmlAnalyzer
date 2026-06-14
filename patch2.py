import sys
import re

with open("PlanGraphControl.xaml.cs", "r", encoding="utf-8") as f:
    content = f.read()

old_sync = """            var nodesToRemove = Nodes.Where(n => !visibleNodes.Contains(n)).ToList();
            foreach (var n in nodesToRemove) Nodes.Remove(n);

            var nodesToAdd = visibleNodes.Where(n => !Nodes.Contains(n)).ToList();
            foreach (var n in nodesToAdd) Nodes.Add(n);

            var connToRemove = Connections.Where(c => !visibleConnections.Contains(c)).ToList();
            foreach (var c in connToRemove) Connections.Remove(c);

            var connToAdd = visibleConnections.Where(c => !Connections.Contains(c)).ToList();
            foreach (var c in connToAdd) Connections.Add(c);"""

new_sync = """            // 修复：必须先移除 Connection，再移除 Node，以防止图形引擎抛出孤立连接异常
            var connToRemove = Connections.Where(c => !visibleConnections.Contains(c)).ToList();
            foreach (var c in connToRemove) Connections.Remove(c);

            var nodesToRemove = Nodes.Where(n => !visibleNodes.Contains(n)).ToList();
            foreach (var n in nodesToRemove) Nodes.Remove(n);

            // 添加时必须先添加 Node，再添加 Connection
            var nodesToAdd = visibleNodes.Where(n => !Nodes.Contains(n)).ToList();
            foreach (var n in nodesToAdd) Nodes.Add(n);

            var connToAdd = visibleConnections.Where(c => !Connections.Contains(c)).ToList();
            foreach (var c in connToAdd) Connections.Add(c);"""

content = content.replace(old_sync, new_sync)

with open("PlanGraphControl.xaml.cs", "w", encoding="utf-8") as f:
    f.write(content)
print("Patch 2 applied.")
