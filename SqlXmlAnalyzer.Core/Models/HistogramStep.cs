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
        public double RangeRows { get; set; }
        public double EqRows { get; set; }
        public double DistinctRangeRows { get; set; }
        public double AvgRangeRows { get; set; }
        public double RangeHiKeyNumeric { get; set; }
    }
}
