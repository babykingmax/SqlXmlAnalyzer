using System.Text;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Application.Services;

/// <summary>Shared GUI/CLI review wording, kept separate from copyable SQL.</summary>
public static class RewriteReviewFormatter
{
    public static string Format(RewriteReview review, bool includeSql = false, string? applicationStatus = null)
    {
        var text = new StringBuilder();
        text.AppendLine(applicationStatus == null
            ? "SQL 改写审核提案：选择仅生成预览，尚未应用；等价性未证明。"
            : $"SQL 改写审核提案：{applicationStatus}；等价性未证明。");
        text.AppendLine($"源 hash (UTF-8 SQL text)：{review.SourceHash}");
        text.AppendLine($"提案 {review.Proposals.Length} 项；已选择 {review.Proposals.Count(p => p.IsSelected)} 项；可应用：否");
        foreach (var proposal in review.Proposals)
        {
            text.AppendLine($"[{(proposal.IsSelected ? "已选择" : "未选择")}] {proposal.Id}");
            text.AppendLine($"规则：{proposal.RuleId} / {proposal.RuleVersion}；{proposal.Description}");
            text.AppendLine($"语法有效：{proposal.Validation.SyntaxValid}；语义等价性：未证明");
            if (!proposal.DependsOn.IsEmpty) text.AppendLine("依赖：" + string.Join(", ", proposal.DependsOn));
            foreach (string value in proposal.Preconditions) text.AppendLine("前提：" + value);
            foreach (string value in proposal.Risks) text.AppendLine("风险：" + value);
            foreach (string value in proposal.Evidence) text.AppendLine("证据：" + value);
            foreach (string value in proposal.Validation.CheckedProperties) text.AppendLine("已检查：" + value);
            foreach (string value in proposal.Validation.UnprovenProperties) text.AppendLine("未证明：" + value);
            if (includeSql)
            {
                text.AppendLine($"SQL diff (步骤输入 offset {proposal.Diff.StartOffset}，基线 {proposal.BaseSqlHash})：");
                text.AppendLine("--- 原范围"); text.AppendLine(proposal.Diff.OriginalText);
                text.AppendLine("+++ 替换范围"); text.AppendLine(proposal.Diff.ReplacementText);
            }
        }
        foreach (string value in review.Warnings) text.AppendLine("警告：" + value);
        return text.ToString();
    }
}
