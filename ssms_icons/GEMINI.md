# SqlXmlAnalyzer - SQL Server 死锁与执行计划深度分析工具

Welcome to the `SqlXmlAnalyzer` workspace. This document acts as an architectural index and instructional handbook for development, maintenance, and future enhancements of the system.

---

## 1. Project Overview

`SqlXmlAnalyzer` is a professional, high-performance diagnostic analyzer and interactive visualization suite designed for SQL Server Database Administrators (DBAs) and performance optimization experts. It specializes in dissecting SQL Server Deadlock files (`.xml`/`.xdl`) and Execution Plan files (`.sqlplan`/`.xml`), detecting performance hazards, and providing clear, actionable Chinese optimization recommendations.

### 1.1 Core Architecture

The system consists of a primary modern Windows desktop application (WPF) and secondary python-based analytical utility scripts:

```
                  +----------------------------------------------+
                  |               SqlXmlAnalyzer                 |
                  +----------------------+-----------------------+
                                         |
             +---------------------------+---------------------------+
             |                                                       |
+------------v-------------+                               +---------v-----------+
|    Presentation Layer    |                               |  Analytical Engine  |
+------------+-------------+                               +---------+-----------+
| - WPF (net8.0-windows)   |                               | - LINQ to XML (C#)  |
| - Nodify (Node Editor)   |                               | - SargAnalyzer (C#) |
| - MainWindow UI          |                               | - Diagnostic Engine |
+------------+-------------+                               +---------+-----------+
             |                                                       |
             +---------------------------+---------------------------+
                                         |
                               +---------v-----------+
                               |    Report Engine    |
                               +---------+-----------+
                               | - HtmlReportGenerator
                               | - Mermaid.js flow   |
                               +---------------------+
```

*   **WPF Native Presentation Layer (`net8.0-windows`):** Built with native Windows Presentation Foundation. High-performance, interactive, and modern graphical nodes are powered by **Nodify (v6.0.0)**, a lightweight node-editor graph framework. This entirely replaces the older WebView2 browser-based rendering, enabling instantaneous startup times and fluid rendering of massive execution graphs.
*   **High-Performance Parsing Layer (`System.Xml.Linq`):** Employs lightweight, robust LINQ-to-XML engines. Specially optimized for memory safety, enabling the streaming and traversal of highly nested XML configurations (up to several gigabytes) in UTF-8 or UTF-16 without causing Out-of-Memory (OOM) situations.
*   **Diagnostic Engines:**
    *   `PlanDiagnosticAnalyzer.cs`: Analyzes `.sqlplan` schemas to uncover performance bottlenecks such as missing indexes, cardinality estimation errors, implicit conversions, index scans, key lookups, memory spills, parallel thread skew, residual predicates, parameter sniffing, and table-valued function "bombs".
    *   `DeadlockGraph.cs` & `SargAnalyzer`: Parses deadlock reports to construct full transaction wait-for dependency graphs, identifies victims, and screens raw SQL queries within transactional boundaries for SARGability (identifying non-SARGable operators like front-wildcard `LIKE` queries or scalar functions applied to key columns).
*   **Report Generation (`HtmlReportGenerator.cs`):** Facilitates the export of standalone, self-contained HTML reports with modern styles and interactive **Mermaid.js** diagrams (rendered via browser-side CDN integration) for easy offline sharing and offline review.
*   **Python Helper Scripts (Legacy / Co-existing):**
    *   `plan_analyzer.py`: A Python Tkinter-based graphical execution plan analyzer.
    *   `DEADLOCK.py`: A command-line deadlock extractor and SARG analyzer with `sqlparse` stream parsing.

---

## 2. Building, Running, and Testing

The C# project targets **.NET 8.0 Windows** and can be compiled and published via the standard .NET CLI or Visual Studio.

### 2.1 Key Commands

*   **Build the Project:**
    ```powershell
    dotnet build
    ```
*   **Run the Desktop Application (WPF):**
    ```powershell
    dotnet run
    ```
*   **Clean Build Artifacts:**
    ```powershell
    dotnet clean
    ```
*   **Publish Single-File Executable:**
    ```powershell
    dotnet publish -c Release -r win-x64 --self-contained
    ```
*   **Execute Legacy Python deadlock script:**
    ```bash
    python DEADLOCK.py --file "LOCK.XDL"
    ```
*   **Execute Legacy Python execution plan script:**
    ```bash
    python plan_analyzer.py
    ```

### 2.2 Manual Testing & Verification

For verifying performance rules or visualizer accuracy:
- **Execution Plan Sample:** Load the `DEMO.sqlplan` file in the root directory.
- **Deadlock Sample:** Load the `LOCK.XDL` file in the root directory.

---

## 3. Development Conventions & Architectural Guidelines

When extending or refactoring `SqlXmlAnalyzer`, strictly adhere to these design principles and patterns to preserve technical integrity:

### 3.1 Coding Standards and Type Safety
*   **Nullable Context:** `<Nullable>enable</Nullable>` is enforced. Handle potential null references defensively. In XML parsing, attributes (`Attribute()`) and child elements (`Element()`) can return null; always use null-conditional operators (`?.Value`) or provide fallback defaults (e.g. `?? ""`, `?? "0"`).
*   **Language Version:** Utilizing C# latest syntax (records, pattern matching, collection expressions).
*   **Composition over Inheritance:** Leverage clean models (like records defined in `DeadlockGraph.cs`) and lightweight helper methods. Do not create deeply-nested class inheritance trees.

### 3.2 XML Schema & Namespace Conventions
*   **Namespace Management:** SQL Server Execution Plans rely on the official XML schema namespace:
    ```csharp
    private static readonly XNamespace ShowplanNs = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
    ```
    Always combine elements with the proper namespace prefix when querying (`doc.Descendants(ShowplanNs + "RelOp")`).
*   **Case Sensitivity:** XML elements and attributes are case-sensitive. Always verify target element names (e.g., `RelOp`, `MissingIndex`, `ParameterList`, `WaitStats`) from the official SQL Server schemas.

### 3.3 Desktop Visualizer (WPF + Nodify) Guidelines
*   **Visualizer Controls:** Interactive nodes and connections are handled by `PlanGraphControl.xaml`.
*   **MVVM Binding:** Node models inherit from `INotifyPropertyChanged` (or use properties with getters/setters notifying state updates).
*   **Coordinate Layouts:** Coordinate layouts during tree visualization are generated using layered heuristics (e.g., in `ApplyLayeredLayout`). Ensure new nodes or modified relationships update coordinate mappings dynamically so elements do not overlap.

### 3.4 Extending Diagnostic Rules
*   **Adding ShowPlan Diagnostics:** Introduce new rules within `PlanDiagnosticAnalyzer.cs`. Assign a distinct Chinese constant string (e.g., `R_NEW_RULE`) and add list holders into the dictionary. Document:
    1. The underlying performance risk.
    2. The exact trigger conditions.
    3. Suggested action items (DDL or query refactoring).
*   **Adding Deadlock / SARG Diagnostics:** Update `DeadlockGraph.cs` / `SargAnalyzer`. Ensure regular expressions are optimized and fail-safe, and comments are stripped before scanning SQL statements to prevent false positives.

### 3.5 Global Logging & Exception Handling
*   **UI Thread Protection:** All main actions in WPF are guarded by try-catch bounds. Critical failures must be passed to `Logger.Critical` and shown to the user using the dispatcher-safe friendly error wrapper (`ShowFriendlyError` in `App.xaml.cs`).
*   **Logging Output:** Log files are saved locally under the relative `log/` folder as structured diagnostic lines, which must not be committed to source control.
