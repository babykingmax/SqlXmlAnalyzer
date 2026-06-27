namespace SqlXmlAnalyzer.Core.Services
{
    public sealed class PlanGraphConnectionHighlightService
    {
        public bool ShouldHighlight(
            string? selectedNodeId,
            string? sourceNodeId,
            string? targetNodeId)
        {
            if (string.IsNullOrEmpty(selectedNodeId))
            {
                return true;
            }

            return selectedNodeId == sourceNodeId
                || selectedNodeId == targetNodeId;
        }
    }
}
