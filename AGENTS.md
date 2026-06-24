# Repository Guidelines

不要递归执行 ListDir。先使用 git ls-files 获取源码清单，

 忽略 .git、bin、obj、.vs、publish-*、backups 和 .tmp.*。

# CRITICAL DIRECTIVE FOR AGENT

You are working in a repository with thousands of ignored/binary files. Due to a known CLI bug, ignore files are currently failing. 

**STRICT TOOL RULES:**
1. **NEVER** use `ListDir` recursively on the root directory. Doing so will crash your context window (exceeding 128k tokens).
2. To understand the project structure, you MUST use the `RunCommand` tool to execute: `git ls-files`
3. If you must use `ListDir` on a specific subdirectory, explicitly ignore checking `.git`, `bin`, `obj`, `.vs`, `publish-*`, `backups`, and `.tmp.*`.

Failure to follow these rules will result in immediate termination.


## Project Structure & Module Organization

- `SqlXmlAnalyzer.csproj` is the .NET 8 WPF desktop application. Views are under `Views/`; UI state and commands are in `Core/ViewModels/` and `ViewModels/`.
- `SqlXmlAnalyzer.Core/` contains XML parsing, deadlock modeling, execution-plan rules, scoring, simulation, and shared abstractions.
- `src/SqlXmlAnalyzer.Analysis/`, `src/SqlXmlAnalyzer.Refactoring/`, and `src/SqlXmlAnalyzer.Application/` provide the newer layered analysis, SQL rewrite, and orchestration components.
- `SqlXmlAnalyzer.CLI/` exposes plan scanning and refactoring for automation.
- `SqlXmlAnalyzer.Tests/` contains xUnit tests and sample `.sqlplan`/`.xdl` files in `TestData/` and `Resources/`.
- `ssms_icons/` contains operator assets. Treat `publish-*` directories as generated artifacts, not source.

## Build, Test, and Development Commands

```powershell
dotnet restore
dotnet build SqlXmlAnalyzer.sln
dotnet test SqlXmlAnalyzer.Tests\SqlXmlAnalyzer.Tests.csproj
dotnet run --project SqlXmlAnalyzer.csproj
dotnet run --project SqlXmlAnalyzer.CLI -- --help
.\publish.ps1
```

Run the full build and test suite before submitting changes. `publish.ps1` creates a self-contained Windows x64 build under `publish\win-x64`.

## Coding Style & Naming Conventions

Use four-space indentation and nullable reference types. Follow standard C# naming: `PascalCase` for types, methods, and public properties; `_camelCase` for private fields; `camelCase` for parameters and locals. Keep analysis logic out of WPF code-behind when possible. New execution-plan checks should implement `IPlanAnalyzerRule`, use a stable `RULE_*` identifier, and include focused tests. Parse XML through `SafeXmlHelper`; do not introduce direct unsafe XML loading.

No repository-wide formatter configuration is currently present. Match surrounding style and use `dotnet format` only when its changes remain narrowly scoped.

## Testing Guidelines

Tests use xUnit and FluentAssertions. Name test classes after the subject, such as `RowEstimateMismatchRuleTests`, and test methods by behavior, such as `Analyze_WhenEstimateDiffers_ReturnsWarning`. Add representative plan or deadlock fixtures for parser and rule changes. Do not claim coverage improvements without a generated coverage report.

## Commit & Pull Request Guidelines

Recent history follows Conventional Commit-style prefixes: `feat:`, `feat(refactor):`, `fix:`, `refactor:`, and `docs:`. Keep commits focused and written in the imperative mood.

Pull requests should include a concise problem statement, implementation summary, test results, and linked issue when applicable. Include screenshots for WPF visual changes and sample CLI output for reporting or command-line changes. Call out rule behavior changes that may alter warning severity or produce new diagnostics.

## Security & Configuration

Treat `.sqlplan`, `.xdl`, `.xel`, and SQL text as untrusted input. Avoid logging sensitive SQL or identifiers unnecessarily. Keep `RuleConfiguration.json` changes backward-compatible and document any new rule IDs or severity defaults.
