using System;
using System.Collections.Generic;

namespace SqlXmlAnalyzer.Core.Rules
{
    public enum RuleScope
    {
        Plan,
        Statement,
        Operator
    }

    public enum RuleCategory
    {
        MissingIndex,
        Cardinality,
        ImplicitConversion,
        HighCost,
        KeyLookup,
        Memory,
        Parallelism,
        ResidualPredicate,
        ParameterSensitivity,
        TableScan,
        UdfAndTableVariable,
        WaitStats,
        OptimizerAbort,
        AntiPattern,
        QueryRewrite,
        ResourceSemaphore,
        CacheAndRecompile
    }

    public sealed record RuleMetadata(
        string RuleId,
        RuleCategory Category,
        RuleScope Scope,
        string DefaultSeverity,
        string Description);

    public static class RuleMetadataCatalog
    {
        private sealed record Definition(
            RuleCategory Category,
            RuleScope Scope,
            string DefaultSeverity);

        private static readonly IReadOnlyDictionary<string, Definition> Definitions =
            new Dictionary<string, Definition>(StringComparer.Ordinal)
            {
                ["RULE_001_IMPLICIT_CONV"] = new(RuleCategory.ImplicitConversion, RuleScope.Operator, "Warning"),
                ["RULE_002_KEY_LOOKUP"] = new(RuleCategory.KeyLookup, RuleScope.Operator, "Warning"),
                ["RULE_003_PARAM_SNIFFING"] = new(RuleCategory.ParameterSensitivity, RuleScope.Plan, "Warning"),
                ["RULE_004_ESTIMATE_MISMATCH"] = new(RuleCategory.Cardinality, RuleScope.Operator, "Warning"),
                ["RULE_006_RESIDUAL_PREDICATE"] = new(RuleCategory.ResidualPredicate, RuleScope.Operator, "Info"),
                ["RULE_008_SPILL_DETECTION"] = new(RuleCategory.Memory, RuleScope.Operator, "Warning"),
                ["RULE_009_PARALLEL_SKEW"] = new(RuleCategory.Parallelism, RuleScope.Operator, "Warning"),
                ["RULE_011_UDF_TVF"] = new(RuleCategory.UdfAndTableVariable, RuleScope.Operator, "Warning"),
                ["RULE_012_NESTED_LOOPS_HIGH_EXEC"] = new(RuleCategory.AntiPattern, RuleScope.Operator, "Critical"),
                ["RULE_013_ANTI_PATTERN"] = new(RuleCategory.AntiPattern, RuleScope.Operator, "Warning"),
                ["RULE_014_SERIAL_PLAN_REASON"] = new(RuleCategory.AntiPattern, RuleScope.Plan, "Info"),
                ["RULE_015_LOCAL_VARIABLES"] = new(RuleCategory.AntiPattern, RuleScope.Statement, "Warning"),
                ["RULE_016_ZERO_ROW_ACTUALS"] = new(RuleCategory.Cardinality, RuleScope.Operator, "Warning"),
                ["RULE_016_WAIT_STATS"] = new(RuleCategory.WaitStats, RuleScope.Plan, "Warning"),
                ["RULE_017_LARGE_MEMORY_GRANT"] = new(RuleCategory.Memory, RuleScope.Plan, "Warning"),
                ["RULE_017_RESOURCE_SEMAPHORE"] = new(RuleCategory.ResourceSemaphore, RuleScope.Plan, "Critical"),
                ["RULE_018_OPTIMIZER_ABORT"] = new(RuleCategory.OptimizerAbort, RuleScope.Statement, "Critical"),
                ["RULE_019_CACHE_RECOMPILE"] = new(RuleCategory.CacheAndRecompile, RuleScope.Plan, "Warning"),
                ["RULE_020_MISSING_INDEX"] = new(RuleCategory.MissingIndex, RuleScope.Plan, "Warning"),
                ["RULE_021_TABLE_SCAN"] = new(RuleCategory.TableScan, RuleScope.Operator, "Warning"),
                ["RULE_022_HIGH_COST_OP"] = new(RuleCategory.HighCost, RuleScope.Plan, "Warning"),
                ["RULE_023_RUNNING_TOTAL_PATTERN"] = new(RuleCategory.AntiPattern, RuleScope.Statement, "Critical"),
                ["RULE_024_SCALAR_SUBQUERY_PATTERN"] = new(RuleCategory.AntiPattern, RuleScope.Statement, "Warning"),
                ["RULE_025_QUERY_REWRITE"] = new(RuleCategory.QueryRewrite, RuleScope.Plan, "Warning"),
                ["RULE_026_IMPLICIT_CONV_DOC"] = new(RuleCategory.ImplicitConversion, RuleScope.Plan, "Warning"),
                ["RULE_027_PARAM_SNIFFING_DOC"] = new(RuleCategory.ParameterSensitivity, RuleScope.Plan, "Warning"),
                ["RULE_028_STATS_USAGE"] = new(RuleCategory.ParameterSensitivity, RuleScope.Plan, "Warning"),
                ["RULE_029_MEMORY_GRANT_DOC"] = new(RuleCategory.Memory, RuleScope.Plan, "Warning"),
                ["RULE_030_CARDINALITY_ERROR"] = new(RuleCategory.Cardinality, RuleScope.Operator, "Warning"),
                ["RULE_031_KEY_LOOKUP_OP"] = new(RuleCategory.KeyLookup, RuleScope.Operator, "Warning"),
                ["RULE_032_MEMORY_SPILL"] = new(RuleCategory.Memory, RuleScope.Operator, "Warning"),
                ["RULE_033_THREAD_SKEW"] = new(RuleCategory.Parallelism, RuleScope.Operator, "Warning"),
                ["RULE_034_RESIDUAL_PRED_OP"] = new(RuleCategory.ResidualPredicate, RuleScope.Operator, "Warning"),
                ["RULE_035_SARGABLE_INDEX_RECOMMENDATION"] = new(RuleCategory.MissingIndex, RuleScope.Operator, "Warning")
            };

        public static RuleMetadata Get(string ruleId, string description)
        {
            if (!Definitions.TryGetValue(ruleId, out Definition? definition))
            {
                throw new InvalidOperationException($"Rule metadata is not registered for '{ruleId}'.");
            }

            return new RuleMetadata(
                ruleId,
                definition.Category,
                definition.Scope,
                definition.DefaultSeverity,
                description);
        }

        public static string GetCategoryTitle(RuleCategory category)
        {
            return category switch
            {
                RuleCategory.MissingIndex => "1. 缺失索引建议与 DDL (Missing Indexes)",
                RuleCategory.Cardinality => "2. 基数估计误差与根因 (Cardinality Error)",
                RuleCategory.ImplicitConversion => "3. 隐式转换风险 (Implicit Conv)",
                RuleCategory.HighCost => "4. 高开销硬件算子 Top 5 (High Cost)",
                RuleCategory.KeyLookup => "5. 键查找与回表开销 (Key Lookup)",
                RuleCategory.Memory => "6. 内存预估与溢出落盘 (Memory Spills)",
                RuleCategory.Parallelism => "7. 并行数据倾斜瓶颈 (Thread Skew)",
                RuleCategory.ResidualPredicate => "8. 寻址残差谓词漏洞 (Residual Predicates)",
                RuleCategory.ParameterSensitivity => "9. 参数嗅探反模式 (Parameter Sniffing)",
                RuleCategory.TableScan => "10. 宽表全扫描风险 (Table Scan)",
                RuleCategory.UdfAndTableVariable => "11. 表变量与 TVF 黑洞 (UDF Bombs)",
                RuleCategory.WaitStats => "12. 引擎资源等待统计 (Wait Stats)",
                RuleCategory.OptimizerAbort => "13. 优化器提前中止 (Optimizer Abort)",
                RuleCategory.AntiPattern => "14. 🧩 经典 SQL 反模式深潜 (Pattern Recognition)",
                RuleCategory.QueryRewrite => "15. 💡 T-SQL 智能改写多维代码块处方 (Query Rewrite Blocks)",
                RuleCategory.ResourceSemaphore => "16. 🚦 内存资源准入等待 (Resource Semaphore)",
                RuleCategory.CacheAndRecompile => "17. ♻️ 缓存命中与重编译开销 (Cache Hit & Recompile)",
                _ => category.ToString()
            };
        }
    }
}
