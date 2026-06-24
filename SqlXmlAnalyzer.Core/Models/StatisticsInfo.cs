using System;

namespace SqlXmlAnalyzer.Core.Models
{
    public class StatisticsInfo
    {
        public string Database { get; set; } = string.Empty;
        public string Schema { get; set; } = string.Empty;
        public string Table { get; set; } = string.Empty;
        public string Statistics { get; set; } = string.Empty;
        public DateTime? LastUpdate { get; set; }
        public long ModificationCount { get; set; }
        public double SamplingPercent { get; set; }

        public int AgeInDays => LastUpdate.HasValue ? (DateTime.Now - LastUpdate.Value).Days : 0; // default 0 if not updated yet or unknown

        public bool IsStale => AgeInDays > 30;

        public bool IsLowSampling => SamplingPercent > 0 && SamplingPercent < 20;

        public string StatusText
        {
            get
            {
                if (AgeInDays > 90) return "严重过时";
                if (AgeInDays > 30) return "已过时";
                if (ModificationCount > 10000) return "超高变动";
                if (ModificationCount > 1000) return "频繁变动";
                if (SamplingPercent > 0 && SamplingPercent < 5) return "极低采样";
                if (IsLowSampling) return "低采样率";
                return "正常";
            }
        }

        public string Severity
        {
            get
            {
                if (AgeInDays > 90 || ModificationCount > 10000 || (SamplingPercent > 0 && SamplingPercent < 5))
                    return "Critical";
                if (AgeInDays > 30 || ModificationCount > 1000 || IsLowSampling)
                    return "Warning";
                return "Info";
            }
        }
    }
}
