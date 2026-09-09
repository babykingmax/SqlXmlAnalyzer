namespace SqlXmlAnalyzer.Core.Services;

public static class AnalysisDisplayText
{
    public const string DeadlockInferenceNotice = "依赖推演：步骤由死锁快照中的持有与等待关系合成，不代表锁事件真实发生顺序；播放间隔不是事件耗时。";
    public const string SandboxNotice = "工具假设：模型尚未校准，不提供精确收益预测。规则评分和参考区间不是实测结果；是否采用索引及选择 Seek/Scan，需核对优化器生成的计划。";
    public const string ComparisonNotice = "先匹配语句再匹配算子；待确认或未匹配项保留。差值仅使用两侧完整且口径一致的捕获指标，零基准不计算百分比。估算成本不是耗时；硬件、缓存与负载未验证。悬停语句/算子查看依据。";
}
