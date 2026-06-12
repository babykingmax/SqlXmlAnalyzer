using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core;
using Xunit;

namespace SqlXmlAnalyzer.Tests
{
    public class DeadlockPatternAnalyzerTests
    {
        private DeadlockProcess CreateProcess(string id, string spid = "50", string ecid = "0", string inputBuf = "", string iso = "read committed", string priority = "0")
        {
            return new DeadlockProcess(
                Id: id,
                Spid: spid,
                Loginname: "user",
                Hostname: "host",
                Isolationlevel: iso,
                Status: "suspended",
                Inputbuf: inputBuf,
                ExecutionStack: new List<ExecutionFrame>(),
                Ecid: ecid,
                DeadlockPriority: priority
            );
        }

        private LockResource CreateResource(string id, string lockType, string indexName = "", string objectName = "", List<LockWaiter> waiters = null)
        {
            return new LockResource(
                LockType: lockType,
                ObjectName: objectName,
                IndexName: indexName,
                Hobtid: "",
                Dbid: "",
                Owners: new List<LockOwner>(),
                Waiters: waiters ?? new List<LockWaiter>(),
                Id: id
            );
        }

        [Fact]
        public void IdentifyPatterns_NullGraph_ReturnsEmpty()
        {
            var patterns = DeadlockPatternAnalyzer.IdentifyPatterns(null);
            patterns.Should().BeEmpty();
        }

        [Fact]
        public void IdentifyPatterns_ParallelDeadlock_ReturnsPattern()
        {
            var graph = new DeadlockGraph();
            graph.Processes.Add(CreateProcess("p1", spid: "55", ecid: "1"));
            graph.Processes.Add(CreateProcess("p2", spid: "55", ecid: "2"));

            var patterns = DeadlockPatternAnalyzer.IdentifyPatterns(graph);

            patterns.Should().ContainSingle();
            patterns[0].TypeName.Should().Contain("Parallel Intra-Query Deadlock");
        }

        [Fact]
        public void IdentifyPatterns_ParallelDeadlock_WithOriginalDoc_ReturnsPattern()
        {
            var graph = new DeadlockGraph();
            graph.Processes.Add(CreateProcess("p1", spid: "55", ecid: "0"));
            
            var doc = System.Xml.Linq.XDocument.Parse("<ShowPlanXML xmlns=\"http://schemas.microsoft.com/sqlserver/2004/07/showplan\"><exchange/><parallelism/></ShowPlanXML>");
            var patterns = DeadlockPatternAnalyzer.IdentifyPatterns(graph, doc);

            patterns.Should().ContainSingle();
            patterns[0].TypeName.Should().Contain("Parallel Intra-Query Deadlock");
        }

        [Fact]
        public void IdentifyPatterns_BookmarkLookup_ReturnsPattern()
        {
            var graph = new DeadlockGraph();
            graph.Processes.Add(CreateProcess("p1", inputBuf: "SELECT * FROM Users WHERE Name = 'A'"));
            graph.Processes.Add(CreateProcess("p2", inputBuf: "UPDATE Users SET Age = 30 WHERE Id = 1"));
            
            graph.Resources.Add(CreateResource("r1", "keylock", indexName: "IX_Name", objectName: "dbo.Users"));
            graph.Resources.Add(CreateResource("r2", "pagelock", indexName: "PK_Users", objectName: "dbo.Users"));
            graph.Resources.Add(CreateResource("r3", "keylock", indexName: "IX_Other", objectName: "dbo.Users"));

            var patterns = DeadlockPatternAnalyzer.IdentifyPatterns(graph);

            patterns.Should().Contain(p => p.TypeName.Contains("Key Lookup"));
        }

        [Fact]
        public void IdentifyPatterns_BookmarkLookupFromInputBuf_ReturnsPattern()
        {
            var graph = new DeadlockGraph();
            graph.Processes.Add(CreateProcess("p1", inputBuf: "select * from A where B=1 update A set B=2"));
            var patterns = DeadlockPatternAnalyzer.IdentifyPatterns(graph);
            patterns.Should().Contain(p => p.TypeName.Contains("Key Lookup"));
        }

        [Fact]
        public void IdentifyPatterns_ConversionDeadlock_ReturnsPattern()
        {
            var graph = new DeadlockGraph();
            graph.Processes.Add(CreateProcess("p1"));
            graph.Processes.Add(CreateProcess("p2"));
            
            graph.Edges.Add(new WaitForEdge { FromProcessId = "p1", HeldMode = "S", RequestedMode = "X" });

            var patterns = DeadlockPatternAnalyzer.IdentifyPatterns(graph);

            patterns.Should().Contain(p => p.TypeName.Contains("Conversion Deadlock"));
        }

        [Fact]
        public void IdentifyPatterns_RangeLock_ReturnsPattern()
        {
            var graph = new DeadlockGraph();
            graph.Processes.Add(CreateProcess("p1", iso: "serializable"));
            graph.Edges.Add(new WaitForEdge { RequestedMode = "RangeS-S", HeldMode = "" });

            var patterns = DeadlockPatternAnalyzer.IdentifyPatterns(graph);

            patterns.Should().Contain(p => p.TypeName.Contains("Range Lock"));
        }

        [Fact]
        public void IdentifyPatterns_RangeLock_FromEdges_ReturnsPattern()
        {
            var graph = new DeadlockGraph();
            graph.Processes.Add(CreateProcess("p1"));
            graph.Edges.Add(new WaitForEdge { RequestedMode = "RangeS-S", HeldMode = "" });

            var patterns = DeadlockPatternAnalyzer.IdentifyPatterns(graph);

            patterns.Should().Contain(p => p.TypeName.Contains("Range Lock"));
        }

        [Fact]
        public void IdentifyPatterns_PageSplit_ReturnsPattern()
        {
            var graph = new DeadlockGraph();
            graph.Processes.Add(CreateProcess("p1", inputBuf: "INSERT INTO T1 VALUES (1)"));
            graph.Resources.Add(CreateResource("r1", "pagelock"));

            var patterns = DeadlockPatternAnalyzer.IdentifyPatterns(graph);

            patterns.Should().Contain(p => p.TypeName.Contains("Page/RID Lock Contention"));
        }

        [Fact]
        public void IdentifyPatterns_ForeignKey_ReturnsPattern()
        {
            var graph = new DeadlockGraph();
            graph.Processes.Add(CreateProcess("p1", inputBuf: "DELETE FROM Orders CASCADE"));

            var patterns = DeadlockPatternAnalyzer.IdentifyPatterns(graph);

            patterns.Should().Contain(p => p.TypeName.Contains("Cascade Deadlock"));
        }

        [Fact]
        public void IdentifyPatterns_HighContention_ReturnsPattern()
        {
            var graph = new DeadlockGraph();
            var waiters = new List<LockWaiter>
            {
                new LockWaiter("w1", "S", "wait"),
                new LockWaiter("w2", "S", "wait")
            };
            graph.Resources.Add(CreateResource("r1", "keylock", waiters: waiters));

            var patterns = DeadlockPatternAnalyzer.IdentifyPatterns(graph);

            patterns.Should().Contain(p => p.TypeName.Contains("Hotspot Resource Contention"));
        }

        [Fact]
        public void IdentifyPatterns_CyclicDeadlock_ReturnsPattern()
        {
            var graph = new DeadlockGraph();

            var patterns = DeadlockPatternAnalyzer.IdentifyPatterns(graph);

            patterns.Should().Contain(p => p.TypeName.Contains("Cyclic Deadlock"));
        }

        [Fact]
        public void IdentifyPatterns_PriorityAnalysis_ReturnsPattern()
        {
            var graph = new DeadlockGraph();
            graph.Processes.Add(CreateProcess("p1", priority: "10"));
            graph.VictimProcessId = "p1";

            var patterns = DeadlockPatternAnalyzer.IdentifyPatterns(graph);

            patterns.Should().Contain(p => p.TypeName.Contains("Deadlock Priority Analysis"));
        }
        
        [Fact]
        public void IdentifyPatterns_BlitzLock_ReturnsPattern()
        {
            var graph = new DeadlockGraph();
            graph.Processes.Add(CreateProcess("p1"));
            graph.Edges.Add(new WaitForEdge 
            { 
                FromProcessId = "p1", 
                ToProcessId = "p2", 
                RequestedMode = "S", 
                HeldMode = "X",
                Resource = CreateResource("r1", "KEY", objectName: "dbo.T1")
            });
            graph.Edges.Add(new WaitForEdge { RequestedMode = "U", HeldMode = "IS", Resource = CreateResource("r2", "PAG") });
            graph.Edges.Add(new WaitForEdge { RequestedMode = "IX", HeldMode = "SIX", Resource = CreateResource("r3", "RID") });
            graph.Edges.Add(new WaitForEdge { RequestedMode = "SCH-S", HeldMode = "SCH-M", Resource = CreateResource("r4", "HOBT") });
            graph.Edges.Add(new WaitForEdge { RequestedMode = "BU", HeldMode = "RangeS-S", Resource = CreateResource("r5", "OBJECT") });
            graph.Edges.Add(new WaitForEdge { RequestedMode = "RangeS-U", HeldMode = "RangeI-N", Resource = CreateResource("r6", "UNKNOWN") });
            graph.Edges.Add(new WaitForEdge { RequestedMode = "RangeX-X", HeldMode = "RangeI-S", Resource = CreateResource("r7", "PAGE") });
            graph.Edges.Add(new WaitForEdge { RequestedMode = "Unknown", HeldMode = "Unknown", Resource = CreateResource("r8", "TAB") });

            var patterns = DeadlockPatternAnalyzer.IdentifyPatterns(graph);

            patterns.Should().Contain(p => p.TypeName.Contains("sp_BlitzLock"));
        }
    }
}
