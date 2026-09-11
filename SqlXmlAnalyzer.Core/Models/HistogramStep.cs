namespace SqlXmlAnalyzer.Core.Models
{
    public enum HistogramKeyType
    {
        Numeric,
        DateTime,
        String
    }

    public class HistogramStep
    {
        public string RangeHiKey { get; set; } = "";
        public bool IsNull => string.Equals(RangeHiKey, "NULL", System.StringComparison.Ordinal);
        public double RangeRows { get; set; }
        public double EqRows { get; set; }
        public double DistinctRangeRows { get; set; }
        public double AvgRangeRows { get; set; }
        public double RangeHiKeyNumeric { get; set; }
    }
}
