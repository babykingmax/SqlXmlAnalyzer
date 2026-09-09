using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Simulation;

public sealed record SimulationCandidateResult(string CandidateId, SqlObjectIdentity Target, PlanLocation? Location,
    IReadOnlyList<string> Keys, IReadOnlyList<string> Includes, PlanMetric<double> RelatedOwnCost,
    IReadOnlyList<PlanOperatorKey> RelatedOperators, string EvidenceCode);

public sealed record CostImpactResult(string? DocumentId, int QueryPlanCount, int OperatorCount,
    IReadOnlyList<SimulationCandidateResult> Candidates, PlanMetric<double> TotalOwnCost, PlanMetric<double> RelatedOwnCost,
    string EvidenceCode, string Description)
{
    public const string Version = "captured-cost-exposure/2.0.0";
    public string ModelVersion => Version;
    public string InputScope => "当前文档中已识别 QueryPlan 的全部算子；候选仅关联各自捕获 QueryPlan 的完整目标。";
    public bool IsCalibrated => false;
    // Null remains distinct from a measured zero improvement in the legacy JSON field.
    public int? ReductionPercent => null;
    public double? RelatedCostSharePercent => TotalOwnCost.IsAvailable && RelatedOwnCost.IsAvailable && TotalOwnCost.Value > 0
        ? Math.Round(RelatedOwnCost.Value!.Value / TotalOwnCost.Value!.Value * 100, 4) : null;
    public string CostBasis => "先汇总算子自身估算成本（子树减直接子树），不叠加语句/父子子树成本；不是实测耗时。";
    public string CombinationPolicy => "候选逐项独立；多个候选按算子身份取并集，同一算子只计一次，不相加评分或假设收益。";
    public IReadOnlyList<string> Limitations { get; } = Array.AsReadOnly(new[]
    {
        "模型尚未校准；原计划相关成本占比不是可节省的成本、运行时间或收益百分比。",
        "不模拟优化器，不断言 Scan 转 Seek 或消除 Lookup/Sort；缺少成本/目标证据时数值保持未知。",
        "未验证索引目录、列类型、写入代价、参数分布、并发及整个工作负载；必须核对实际计划。"
    });
}
