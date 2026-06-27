namespace SqlXmlAnalyzer.Core.Services
{
    public sealed class PlanGraphOperatorTypeService
    {
        public string DetectOperatorType(
            string? physicalOp,
            string? logicalOp)
        {
            string text = $"{physicalOp} {logicalOp}".ToLowerInvariant();

            if (text.Contains("scan"))
            {
                return "Scan";
            }

            if (text.Contains("seek") || text.Contains("bookmark"))
            {
                return "Seek";
            }

            if (text.Contains("join")
                || text.Contains("hash")
                || text.Contains("merge")
                || text.Contains("nested"))
            {
                return "Join";
            }

            if (text.Contains("parallelism")
                || text.Contains("exchange")
                || text.Contains("distribute")
                || text.Contains("gather"))
            {
                return "Parallelism";
            }

            if (text.Contains("sort") || text.Contains("top"))
            {
                return "Sort";
            }

            if (text.Contains("spool") || text.Contains("table spool"))
            {
                return "Spool";
            }

            if (text.Contains("compute")
                || text.Contains("scalar")
                || text.Contains("assign"))
            {
                return "Compute";
            }

            return "Other";
        }
    }
}
