using System;

namespace SqlXmlAnalyzer.Core.Models
{
    public class DeadlockEvent
    {
        public int StepNumber { get; set; }
        public string Type { get; set; } = ""; // "Grant", "Request", "Victim"
        public string Spid { get; set; } = "";
        public string ProcessId { get; set; } = "";
        public string ResourceId { get; set; } = "";
        public string LockMode { get; set; } = "";
        public string Description { get; set; } = "";
        
        // UI Helpers
        public bool IsInCycle { get; set; }
        public bool IsVictim { get; set; }
    }

    public class DeadlockNodeInfo
    {
        public string Id { get; set; } = "";
        public string Spid { get; set; } = "";
        public bool IsVictim { get; set; }
        public bool IsInCycle { get; set; }
    }

    public class DeadlockResourceInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsInCycle { get; set; }
    }
}
