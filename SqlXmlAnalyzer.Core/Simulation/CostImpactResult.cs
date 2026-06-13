namespace SqlXmlAnalyzer.Core.Simulation
{
    public class CostImpactResult
    {
        public int ReductionPercent { get; }
        public string Description { get; }

        public CostImpactResult(int reductionPercent, string description)
        {
            ReductionPercent = reductionPercent;
            Description = description;
        }
    }
}
