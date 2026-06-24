# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - 2026-06-24

### Fixed
- **Core Rules**: Reduced duplicate warnings in parameter sniffing rule (`ParameterSniffingRule.cs`) by restricting execution strictly to `NodeId = 0` (Plan level).
- **Core Rules**: Eliminated false positives in parameter sensitivity detection (`QueryRewriteRule.cs`) by removing the assumption that statistics usage alone implies parameter sniffing; compile-time and runtime parameter value differences are now required.
- **Core Rules**: Filtered out healthy statistics info alerts (with `Info` severity) from `StatsUsageRule.cs`, raising warnings/critical flags only for actual issues (e.g. stale stats, low sampling, high modifications).

### Added
- **SQL Refactoring**: Added `TryRewriteSelectedSubquery` to `ScalarSubqueryToJoinRule.cs` to enable targeted, single-expression scalar subquery-to-join rewrites by offset and length.
- **WPF UI**: Introduced inline quick-fixes for rewriteable subqueries within the original SQL diff viewer. Clicking the lightbulb icon launches a side-by-side comparison in `QuickFixWindow` and allows applying the rewrite localized to that subquery.
- **WPF UI**: Added SQL tokenization and syntax highlighting in the quick-fix and diff viewer (comments, strings, standard keywords, and generated aliases `t_sub_*` and `agg_*`).
- **Tests**: Added suite of unit tests for parameter sensitivity rules (`ParameterSensitivityRuleTests.cs`) and targeted scalar subquery rewrites (`ScalarSubqueryQuickFixTests.cs`).
