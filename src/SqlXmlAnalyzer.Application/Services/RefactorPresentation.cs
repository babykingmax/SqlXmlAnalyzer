using SqlXmlAnalyzer.Application.Models;

namespace SqlXmlAnalyzer.Application.Services;

/// <summary>Separates user-facing safety notices from SQL so copying SQL never embeds diagnostic comments.</summary>
public sealed record RefactorPresentation(string Sql, string Notices)
{
    public Core.Models.RewriteReview? Review { get; init; }
    public bool Failed { get; init; }
    public static RefactorPresentation FromResult(OrchestratorResult result, string originalSql)
    {
        var notices = new List<string>();
        if (!result.IsSuccess || result.Result == null)
        {
            notices.Add("T-SQL 重构失败，已保留原 SQL。");
            if (!string.IsNullOrWhiteSpace(result.ErrorMessage)) notices.Add(result.ErrorMessage);
            if (result.Result != null)
            {
                notices.AddRange(result.Result.Errors);
                notices.AddRange(result.Result.Context.Warnings);
            }
        }
        else
        {
            if (result.Result.Review is { } review)
                notices.Add(RewriteReviewFormatter.Format(review, includeSql: true));
            notices.Add(result.Result.Context.RefactorChanges.Count > 0
                ? "已生成候选 SQL，尚未应用。"
                : "分析完成，未产生 SQL 改写。");
            notices.AddRange(result.Result.Context.Warnings);
        }
        notices.AddRange(result.Warnings);
        return new(result.IsSuccess && result.Result != null ? result.Result.Review?.PreviewSql ?? originalSql : originalSql,
            string.Join(Environment.NewLine, notices.Distinct(StringComparer.Ordinal)))
            { Review = result.Result?.Review, Failed = !result.IsSuccess || result.Result == null };
    }
}
