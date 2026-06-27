namespace SqlXmlAnalyzer.Core.Services
{
    public sealed class PlanGraphNodeDisplayService
    {
        public string GetTextVisibility(string? value)
        {
            return string.IsNullOrEmpty(value) ? "Collapsed" : "Visible";
        }

        public string GetBooleanVisibility(bool value)
        {
            return value ? "Visible" : "Collapsed";
        }

        public bool IsFullPartitionScan(
            string? partitioned,
            string? partitionCount,
            string? partitionRange)
        {
            return partitioned == "True"
                && !string.IsNullOrEmpty(partitionCount)
                && (partitionRange == $"1 - {partitionCount}"
                    || partitionRange == $"1-{partitionCount}");
        }

        public string GetPartitionRangeColor(
            string? partitioned,
            string? partitionCount,
            string? partitionRange)
        {
            return IsFullPartitionScan(
                partitioned,
                partitionCount,
                partitionRange)
                ? "#FF0000"
                : "#263238";
        }

        public string GetPartitionLabelColor(
            string? partitioned,
            string? partitionCount,
            string? partitionRange)
        {
            return IsFullPartitionScan(
                partitioned,
                partitionCount,
                partitionRange)
                ? "#FF0000"
                : "#546E7A";
        }

        public string GetPartitionInfoVisibility(string? partitioned)
        {
            return partitioned == "True" ? "Visible" : "Collapsed";
        }

        public string GetNodeSeverityColor(string? nodeSeverity)
        {
            return nodeSeverity switch
            {
                "Critical" => "#D32F2F",
                "Warning" => "#F57C00",
                _ => "Transparent"
            };
        }

        public string GetNodeSeverityBorderThickness(string? nodeSeverity)
        {
            return nodeSeverity == "Info" ? "0" : "2";
        }

        public string GetExtraInfoVisibility(
            bool isParallel,
            string? warnings)
        {
            return isParallel || !string.IsNullOrEmpty(warnings)
                ? "Visible"
                : "Collapsed";
        }
    }
}
