# Phase 0 Remediation Summary

Date: 2026-06-26

Commit implemented before this note: `46d7413 fix: establish phase 0 engineering baseline`

## Background

This remediation implements Phase 0 of the SqlXmlAnalyzer improvement plan. The goal of Phase 0 is not to add large user-facing features yet. It establishes a cleaner engineering baseline so later UI, execution plan, deadlock, index, statistics, and session features can be built on reliable tests, predictable configuration, and safer automation behavior.

The changes address concrete issues found during the code, UI, and feature review:

- A test wrote to a hard-coded absolute path and polluted the working tree.
- Rule configuration accepted unknown rule IDs silently.
- Several rule IDs reused the same numeric prefix, making rule governance confusing.
- CLI directory scanning recursively traversed generated directories such as `bin`, `obj`, `.vs`, and `publish-*`.
- Unused centrally pinned packages added dependency-governance noise.
- A tracked `1.1.0` file appeared to be a tool handshake artifact rather than source.

## Implemented Fixes

### 1. Removed test side effects

File changed:

- `SqlXmlAnalyzer.Tests/UnitTest1.cs`

Previous behavior:

- `TestDummy` reflected ScriptDom types and wrote output to `E:\SqlXmlAnalyzer\table_hints.txt`.
- The assertion was `Assert.True(true)`, so the test did not validate useful behavior.
- The hard-coded path could break CI or create local untracked files after every test run.

New behavior:

- The test is renamed to `ScriptDomExpressions_ExposeExpectedPublicProperties`.
- It now verifies that selected ScriptDom expression types expose declared public instance properties.
- It no longer writes any file.

Impact:

- Test runs no longer create `table_hints.txt`.
- CI portability is improved.

### 2. Tightened rule configuration validation

Files changed:

- `SqlXmlAnalyzer.Core/Configuration/RuleConfigurationLoader.cs`
- `SqlXmlAnalyzer.Tests/RuleConfigurationLoaderTests.cs`

Previous behavior:

- Empty RuleId, duplicate RuleId, and invalid severity were validated.
- Unknown RuleId values were not rejected, so a misspelled rule ID could silently do nothing.
- This is risky for commercial-tool behavior because rule governance and production configuration must fail visibly when invalid.

New behavior:

- Rule configuration now validates every configured RuleId against the registered rule metadata catalog.
- Unknown RuleId values return validation errors.
- Deprecated RuleId aliases are normalized and reported as warnings.
- Severity override normalization is preserved.
- Missing explicit configuration files still return failure.

Compatibility:

- The missing-configuration message retains the existing Chinese text expected by older tests while also adding a clearer English message.

New test coverage:

- Unknown RuleId returns an error.
- Deprecated RuleId alias is normalized and returns a warning.
- Duplicate RuleId, invalid severity, empty RuleId, missing file, invalid JSON, and valid configuration paths remain covered.

### 3. Fixed duplicate rule-number governance

Files changed:

- `SqlXmlAnalyzer.Core/Rules/RuleMetadata.cs`
- `SqlXmlAnalyzer.Core/Rules/WaitStatsRule.cs`
- `SqlXmlAnalyzer.Core/Rules/ResourceSemaphoreRule.cs`
- `SqlXmlAnalyzer.Tests/RuleMetadataAndScopeTests.cs`

Previous rule IDs:

- `RULE_016_ZERO_ROW_ACTUALS`
- `RULE_016_WAIT_STATS`
- `RULE_017_LARGE_MEMORY_GRANT`
- `RULE_017_RESOURCE_SEMAPHORE`

Problem:

- The full IDs were unique, but the numeric prefixes were reused.
- This makes documentation, configuration, support notes, and release notes harder to maintain.

New rule IDs:

- `RULE_036_WAIT_STATS`
- `RULE_037_RESOURCE_SEMAPHORE`

Backward compatibility:

- `RULE_016_WAIT_STATS` is accepted as a deprecated alias for `RULE_036_WAIT_STATS`.
- `RULE_017_RESOURCE_SEMAPHORE` is accepted as a deprecated alias for `RULE_037_RESOURCE_SEMAPHORE`.
- Loading a deprecated alias emits a warning so users can migrate configuration files deliberately.

New test coverage:

- Registered default rule IDs must be unique.
- Registered default rule numeric prefixes must be unique.
- Default configuration IDs must resolve to registered rules.

### 4. Made CLI plan scanning safer

Files changed:

- `SqlXmlAnalyzer.CLI/Program.cs`
- `SqlXmlAnalyzer.Tests/Application/CliScanFileCollectionTests.cs`

Previous behavior:

- Directory scan used:

```csharp
Directory.GetFiles(path, "*.sqlplan", SearchOption.AllDirectories)
```

Problem:

- This blindly traversed generated or ignored directories.
- Large `bin`, `obj`, `.vs`, publish, backup, or temporary directories could slow automation or scan irrelevant plans.

New behavior:

- CLI directory scanning now uses `CollectPlanFiles`.
- It scans with explicit directory traversal and skips these directories by default:

```text
.git
bin
obj
.vs
publish-*
backups
.tmp.*
```

- It also supports additional exclude patterns through `--exclude` / `-x`.
- Inaccessible or unreadable directories are skipped with warnings instead of crashing the whole scan.
- Results are sorted for deterministic automation output.

New test coverage:

- Default generated directories are skipped.
- Additional exclude patterns are applied.

Known follow-up:

- CLI help text still contains legacy mojibake in some strings. The `--exclude` feature is implemented and tested, but a broader CLI localization cleanup should be done separately to avoid mixing encoding cleanup with functional changes.

### 5. Cleaned dependency governance

File changed:

- `Directory.Packages.props`

Removed unused central package pins:

- `Azure.Identity`
- `Microsoft.Identity.Client`
- `Microsoft.Identity.Client.Extensions.Msal`
- `System.Formats.Asn1`

Reason:

- No project file referenced these packages.
- Removing unused pins reduces dependency audit noise and avoids confusing vulnerability review results.

### 6. Removed non-source artifact

File removed:

- `1.1.0`

Reason:

- The file content looked like a JSON-RPC/toolbox handshake result, not application source.
- Keeping it tracked polluted repository history and release review.

## Validation

Commands executed:

```powershell
dotnet build SqlXmlAnalyzer.sln
dotnet test SqlXmlAnalyzer.Tests\SqlXmlAnalyzer.Tests.csproj --no-restore
dotnet build SqlXmlAnalyzer.sln --no-restore
```

Results:

- `dotnet build SqlXmlAnalyzer.sln`: passed, 0 warnings, 0 errors.
- `dotnet test SqlXmlAnalyzer.Tests\SqlXmlAnalyzer.Tests.csproj --no-restore`: 334 passed, 0 failed, 0 skipped.
- `dotnet build SqlXmlAnalyzer.sln --no-restore`: passed, 0 warnings, 0 errors.

Working-tree hygiene:

- `table_hints.txt` no longer appears after test execution.
- The only remaining untracked local artifact after the Phase 0 commit was the generated Word review report:

```text
SqlXmlAnalyzer_Review_Report_2026-06-26.docx
```

It was intentionally not included in the engineering commit because it is a binary local deliverable, not source.

## Risk And Compatibility Notes

### RuleId changes

The two renamed rules are backward-compatible through aliases:

- `RULE_016_WAIT_STATS` -> `RULE_036_WAIT_STATS`
- `RULE_017_RESOURCE_SEMAPHORE` -> `RULE_037_RESOURCE_SEMAPHORE`

Users should update their `RuleConfiguration.json` to the new IDs when convenient. Deprecated aliases currently load successfully with warnings.

### CLI scanning behavior

The CLI now skips common generated directories by default. This is the safer behavior for automation and CI.

If a user intentionally stores `.sqlplan` files under a skipped directory such as `backups` or `publish-*`, those files will no longer be scanned unless they are moved or scanned directly as individual files.

### Configuration validation

Invalid or misspelled RuleId entries now fail configuration loading instead of being ignored. This is intentional because silent config failures can mislead DBA users and CI pipelines.

## Phase 0 Acceptance Status

Completed:

- Test side effects removed.
- Rule configuration validates unknown RuleId.
- Duplicate numeric RuleId prefixes fixed.
- CLI generated-directory scan exclusions implemented.
- Unused dependency pins removed.
- Non-source artifact removed.
- Build and test suite pass.
- Phase implementation plan saved as `SqlXmlAnalyzer_Phased_Implementation_Plan_2026-06-26.txt`.

Remaining for later phases:

- CLI and WPF mojibake/localization cleanup.
- UI layout and code-behind restructuring.
- Plan Explorer-style statements tree, graph filtering, diagnostics grid, and deadlock investigation workflow.
- Runtime capture, session history, report templates, and SSMS integration.
