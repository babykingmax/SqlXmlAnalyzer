# CLI 审核重放与输入保护

`refactor` 生成提案，`rewrite-validate` 验证选择，`rewrite-apply` 重新验证后应用。三步现在都支持相同的 `--plan`、`--config`、`--max-passes` 和 `--read-options`，避免辅助计划或改写轮数不同导致已审核的提案 ID 无法重建。

## 保持相同的审核上下文

请在三步中使用相同的源 SQL、计划文件、规则配置和选项值。计划诊断会影响提案生成及依赖链；省略计划、修改配置、改变最大轮数或升级规则版本后，应重新生成和审核提案，不能沿用旧 ID。

以下 PowerShell 示例复用同一组选项。先准备 `query.sql`、`query.sqlplan`、`rules.json`、`limits.json` 及语义验证场景 `suite.json`：

```powershell
$replay = @('--plan', 'query.sqlplan', '--config', 'rules.json', '--max-passes', '8', '--read-options', 'limits.json')

# 生成提案；默认均未选择，源 SQL 不变。
SqlXmlAnalyzer.CLI refactor query.sql @replay --format json --show-sql --output proposals.json

# 审核 proposals.json 的规则、风险、diff 和依赖后，填入实际 ID；多个选择重复 --select。
$selection = @('--select', '<审核后的提案ID>')
SqlXmlAnalyzer.CLI rewrite-validate query.sql @replay @selection --scenarios suite.json --show-sql --output validation.json

# 审核验证场景、所选预览及验证结果后，从 validation.json 读取本次 hash。
$review = Get-Content -LiteralPath validation.json -Raw | ConvertFrom-Json
SqlXmlAnalyzer.CLI rewrite-apply query.sql @replay @selection --scenarios suite.json `
  --review-source-hash $review.SourceHash --review-preview-hash $review.PreviewHash `
  --review-suite-hash $review.Validation.SuiteHash --acknowledge-scenarios --output applied.json
```

仅在验证结果允许应用且已人工审核场景和预览后执行最后一步。`rewrite-apply` 会重新读取源文件、重建选择、核对 hash、重新运行数据库验证，再执行带备份的写回。有限场景验证不代表任意输入下语义等价。数据库及场景要求见 [SQL 语义验证与可靠应用](IMP-19SQL语义验证与可靠应用.md)。

选项含义：

| 选项 | 行为 |
| --- | --- |
| `--plan <文件>` | 读取并诊断辅助执行计划；无法完整读取或诊断失败时阻止提案重放。 |
| `--config <文件>` | 用于辅助计划诊断的规则配置。缺省使用应用程序目录的 `RuleConfiguration.json`，与普通扫描一致；不再依赖启动工作目录。显式配置路径相对于当前工作目录解析。 |
| `--max-passes <正整数>` | 提案生成最大轮数，默认 5。生成超过 5 轮的提案时，验证／应用也需传入相同值。 |
| `--read-options <JSON>` | 辅助计划读取预算；字段与 `read` 命令一致。用于审核的三个命令应使用相同预算。 |

`semantic-compare` 直接比较两个 SQL 文件，不接受 `--plan`、`--config` 或 `--max-passes`。未知提案不会自动替换成另一个提案；错误信息会提示检查生成上下文。

## 报告不能覆盖输入

桌面命令行 PDF、Word、文本导出，以及独立 CLI 的改写／语义报告，在生成前和发布前检查输入路径。保护范围包括源 SQL、辅助计划、候选 SQL、场景文件、实际规则配置、读取预算文件及桌面批处理中的其他输入。Windows 下也检查硬链接及路径别名；成功报告和失败报告使用同一保护规则。

报告先在目标目录写入临时文件，完成后重新检查所有输入，再替换目标报告。导出异常或发布前取消会保留已有报告并清理临时文件。因此输出目录需要创建临时文件和替换目标文件的权限；已有的非输入报告仍可被覆盖。显式 SQL 应用继续走独立的审核、验证及备份写回流程。

## 目录扫描

独立 CLI 目录扫描和桌面批处理跳过子目录中的符号链接／junction，避免祖先循环或重复遍历。显式指定的扫描根目录可以是链接；扫描不会继续进入其内部的链接目录。默认排除 `.git`、`bin`、`obj`、`.vs`、`publish-*`、`backups`、`.tmp.*`；独立 CLI 的附加排除模式继续有效。独立 CLI 收集文件期间响应取消，Ctrl+C 返回 130。

自动回归覆盖路径／硬链接冲突、辅助配置保护、导出中途失败与取消、PDF／Word 完整输出、计划及轮数重放、循环 junction 与收集阶段取消。
