using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Core.Scoring;

public sealed record IndexScoreResult(int Score, int EqualityPoints, int InequalityPoints, int SortPoints, int CoveragePoints,
    int PenaltyPoints, int KnownOutputColumns, int CoveredOutputColumns, string EvidenceSource, PlanLocation? Location)
{
    public const string Version = "index-evidence-score/2.0.1";
    public string ModelVersion => Version;
    public bool IsCalibrated => false;
    public string EvidenceDescription => EvidenceSource switch
    {
        "CAPTURED_PREDICATES" => "计划中的结构化比较谓词",
        "SCALAR_TEXT_SYNTAX" => "计划谓词文本的语法分析（较低可信度）",
        "DEFINITION_ROLES_ONLY" => "仅候选声明的列角色（无计划证据）",
        "MISSING_TARGET_EVIDENCE" => "缺少目标关联证据",
        "MISSING_PREDICATE_EVIDENCE" => "未识别到可评分的谓词证据",
        _ => "缺少有效计划命名空间"
    };
    public string InputScope => "候选所属捕获 QueryPlan 与完整对象；无计划时仅使用声明的列角色。";
    public string Weights => "连续等值前导键每列 30；首个不等值键 15；一个兼容排序 15；已核验输出列覆盖比例 × 40；键超过 4/INCLUDE 超过 8 时每列扣 2；总分 0–100。";
    public string Limitations => "工具假设，非 SQL Server 成本或收益预测；未知覆盖不给分，标识符不推断排序规则；不保证 Seek 或消除 Lookup/Sort。";
    public double? KnownOutputCoverage => KnownOutputColumns == 0 ? null : (double)CoveredOutputColumns / KnownOutputColumns;
    public string Breakdown => $"等值 {EqualityPoints} + 范围 {InequalityPoints} + 排序 {SortPoints} + 已知覆盖 {CoveragePoints} − 宽度惩罚 {PenaltyPoints} → {Score}/100（限制到 0–100）";
}
