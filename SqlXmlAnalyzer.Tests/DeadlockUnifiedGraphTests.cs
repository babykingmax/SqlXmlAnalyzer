using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Parsers;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests;

public class DeadlockUnifiedGraphTests
{
    internal static XDocument Document(params (string From, string To, string Resource)[] edges)
    {
        var ids = edges.SelectMany(e => new[] { e.From, e.To }).Distinct().ToArray();
        var processes = new XElement("process-list", ids.Select((id, i) => new XElement("process", new XAttribute("id", id),
                new XAttribute("spid", 51 + i), new XAttribute("ecid", "0"),
                new XElement("inputbuf", "SELECT synthetic"))));
        var resources = new XElement("resource-list", edges.Select(e => new XElement("keylock", new XAttribute("id", e.Resource),
                new XAttribute("objectname", "test-table"),
                new XElement("owner-list", new XElement("owner", new XAttribute("id", e.To), new XAttribute("mode", "X"))),
                new XElement("waiter-list", new XElement("waiter", new XAttribute("id", e.From), new XAttribute("mode", "X"), new XAttribute("requestType", "wait"))))));
        return new XDocument(new XElement("deadlock",
            new XElement("victim-list", new XElement("victimProcess", new XAttribute("id", ids[0]))), processes, resources));
    }

    internal static XDocument Overlap() => Document(("a", "b", "ab"), ("b", "a", "ba"), ("a", "c", "ac"), ("c", "b", "cb"));

    [Fact]
    public void Fixture_PreservesMixedParallelAndRangeEvidenceForAllCycleMembers()
    {
        var document = SafeXmlHelper.ParseSafe(Utilities.EmbeddedResourceHelper.GetResourceContent("imp13_overlapping_deadlock.xdl"));
        var analysis = new DeadlockAnalysisService().Analyze(document);
        analysis.Graph.CycleAnalysis.ProcessIds.Should().HaveCount(3);
        analysis.Graph.Cycles.Should().HaveCount(2);
        analysis.Graph.VictimProcessIds.Should().HaveCount(2);
        analysis.Patterns.Should().Contain(p => p.RuleId == "RULE_DEADLOCK_PARALLEL").And.Contain(p => p.RuleId == "RULE_DEADLOCK_RANGE");
        analysis.Graph.ResourceLinks.Should().Contain(l => l.RawFields.ContainsKey("waiterActivity"));
    }

    [Fact]
    public void AllThreeNodeDirectedGraphs_MatchIndependentReachabilityOracle()
    {
        var candidates = (from a in Enumerable.Range(0, 3) from b in Enumerable.Range(0, 3) where a != b select (a, b)).ToArray();
        for (int mask = 1; mask < 64; mask++)
        {
            var selected = candidates.Where((_, bit) => (mask & (1 << bit)) != 0).ToArray();
            var reach = new bool[3, 3];
            foreach (var (a, b) in selected) reach[a, b] = true;
            for (int k = 0; k < 3; k++) for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) reach[i, j] |= reach[i, k] && reach[k, j];
            var graph = new DeadlockAnalysisService().Analyze(Document(selected.Select(e => ($"p{e.a}", $"p{e.b}", $"r{e.a}{e.b}")).ToArray())).Graph;
            graph.CycleAnalysis.ProcessIds.Should().BeEquivalentTo(Enumerable.Range(0, 3).Where(i => reach[i, i]).Select(i => $"p{i}"), $"graph mask {mask}");
            graph.Cycles.Select(c => c.CycleId).Distinct().Count().Should().Be(graph.Cycles.Count);
            graph.Cycles.SelectMany(c => c.ProcessIds).Distinct().Should().BeEquivalentTo(graph.CycleAnalysis.ProcessIds);
        }
    }

    [Fact]
    public void OwnerOrder_DoesNotReassignStableEvidenceIdsToDifferentActivities()
    {
        var document = Overlap();
        var list = document.Descendants("owner-list").First();
        list.Elements().First().SetAttributeValue("ownerActivity", "first");
        var second = new XElement(list.Elements().First()); second.SetAttributeValue("ownerActivity", "second"); list.Add(second);
        var first = new DeadlockAnalysisService().Analyze(document).Graph;
        var reverse = list.Elements().Reverse().ToArray(); list.RemoveNodes(); list.Add(reverse);
        var reordered = new DeadlockAnalysisService().Analyze(document).Graph;
        var expected = first.ResourceLinks.Where(l => l.RawFields.ContainsKey("ownerActivity")).ToDictionary(l => l.RawFields["ownerActivity"], l => l.LinkId);
        foreach (var link in reordered.ResourceLinks.Where(l => l.RawFields.ContainsKey("ownerActivity")))
            link.LinkId.Should().Be(expected[link.RawFields["ownerActivity"]]);
    }

    [Fact]
    public void OverlappingCycles_AllPresentationsUseCompleteSccMembership()
    {
        var analysis = new DeadlockAnalysisService().Analyze(Overlap());
        var graph = analysis.Graph;
        graph.CycleAnalysis.ProcessIds.Should().BeEquivalentTo(new[] { "a", "b", "c" });
        graph.Cycles.Should().HaveCount(2);
        graph.Cycles.Select(c => c.Length).Should().BeEquivalentTo(new[] { 2, 3 });
        analysis.Timeline.Graph.Should().BeSameAs(graph);
        analysis.Timeline.Processes.Values.Should().OnlyContain(p => p.IsInCycle);
        analysis.Timeline.Resources.Values.Should().OnlyContain(r => r.IsInCycle);
        analysis.Timeline.Events.Should().OnlyContain(e => e.IsInCycle);
        var layout = new DeadlockGraphLayoutService().BuildLayout(graph);
        layout.Processes.Should().OnlyContain(p => p.IsInCycle);
        layout.Resources.Should().OnlyContain(r => r.IsInCycle);
        var edges = new DeadlockGraphEdgeService().BuildEdges(layout.Resources);
        edges.Should().HaveCount(graph.ResourceLinks.Count).And.OnlyContain(e => e.IsInCycle);
        edges.Select(e => e.EvidenceId).Should().BeEquivalentTo(graph.ResourceLinks.Select(l => l.LinkId));
        analysis.Mermaid.Split("==>").Should().HaveCount(9);
        DeadlockGraphBuilder.GenerateAsciiCycle(graph).Should().Contain("3 个环成员");
        new DeadlockPlaybackViewModel(analysis.Timeline).InferenceNotice.Should().Contain(graph.CycleAnalysis.Summary);
    }

    [Fact]
    public void SourceOrdering_DoesNotChangeMembersEdgeIdentitiesOrNormalizedCycles()
    {
        var document = Overlap();
        var before = new DeadlockAnalysisService().Analyze(document).Graph;
        foreach (string list in new[] { "process-list", "resource-list" })
        {
            var element = document.Root!.Element(list)!;
            var reversed = element.Elements().Reverse().ToArray();
            element.RemoveNodes(); element.Add(reversed);
        }
        var after = new DeadlockAnalysisService().Analyze(document).Graph;
        after.CycleAnalysis.ProcessIds.Should().BeEquivalentTo(before.CycleAnalysis.ProcessIds);
        after.Edges.Select(e => e.EdgeId).Should().BeEquivalentTo(before.Edges.Select(e => e.EdgeId));
        after.Cycles.Select(c => c.CycleId).Should().Equal(before.Cycles.Select(c => c.CycleId));
    }

    [Fact]
    public void TwoNodeCycle_IsCountedOnceAndRemovesRepeatedClosingNode()
    {
        var graph = new DeadlockAnalysisService().Analyze(Document(("a", "b", "ab"), ("b", "a", "ba"))).Graph;
        var cycle = graph.Cycles.Should().ContainSingle().Subject;
        cycle.ProcessIds.Should().Equal("a", "b");
        var rotated = DeadlockGraphBuilder.NormalizeCycle(new[] { "b", "a", "b" }, cycle.EdgesInCycle.AsEnumerable().Reverse());
        rotated.CycleId.Should().Be(cycle.CycleId);
        rotated.ProcessIds.Should().Equal("a", "b");
        rotated.GetCycleDescription().Should().Contain("a → b → a");
    }

    [Fact]
    public void ParallelResources_PreserveDifferentEdgesForTheSameProcessPath()
    {
        var graph = new DeadlockAnalysisService().Analyze(Document(("a", "b", "first"), ("a", "b", "second"), ("b", "a", "return"))).Graph;
        graph.Cycles.Should().HaveCount(2);
        graph.Cycles.Select(c => c.CycleId).Distinct().Should().HaveCount(2);
        graph.Cycles.Should().OnlyContain(c => c.ProcessIds.SequenceEqual(new[] { "a", "b" }));
    }

    [Fact]
    public void BridgeBetweenCyclicComponents_IsNotMarkedAsCycleEvidence()
    {
        var analysis = new DeadlockAnalysisService().Analyze(Document(("a", "b", "ab"), ("b", "a", "ba"),
            ("c", "d", "cd"), ("d", "c", "dc"), ("b", "c", "bridge")));
        var bridge = analysis.Resources.Single(r => r.SourceId == "bridge");
        analysis.Graph.CycleAnalysis.ProcessIds.Should().HaveCount(4);
        analysis.Timeline.Resources[bridge.Id].IsInCycle.Should().BeFalse();
        analysis.Timeline.Events.Where(e => e.ResourceId == bridge.Id).Should().OnlyContain(e => !e.IsInCycle);
        new DeadlockGraphLayoutService().BuildLayout(analysis.Graph).Resources.Single(r => r.RawResources[0] == bridge).IsInCycle.Should().BeFalse();
        analysis.Graph.CycleAnalysis.EdgeIds.Should().HaveCount(4);
    }

    [Theory]
    [InlineData("paths")]
    [InlineData("length")]
    [InlineData("steps")]
    public void ExplanationBudgets_TruncatePathsWithoutChangingCycleMembership(string budget)
    {
        var options = budget switch {
            "paths" => new DeadlockGraphOptions { MaxCyclePaths = 1 },
            "length" => new DeadlockGraphOptions { MaxCycleLength = 1 },
            _ => new DeadlockGraphOptions { MaxCycleSearchSteps = 1 }
        };
        var analysis = new DeadlockAnalysisService(options: options).Analyze(Overlap());
        analysis.Graph.CycleAnalysis.ProcessIds.Should().BeEquivalentTo(new[] { "a", "b", "c" });
        analysis.Graph.CycleAnalysis.PathsTruncated.Should().BeTrue();
        analysis.Graph.CycleAnalysis.SearchSteps.Should().BeLessThanOrEqualTo(options.MaxCycleSearchSteps);
        analysis.Graph.Cycles.Count.Should().BeLessThanOrEqualTo(options.MaxCyclePaths);
        analysis.Graph.Cycles.Should().OnlyContain(c => c.Length <= options.MaxCycleLength);
        analysis.Warnings.Should().Contain(w => w.Contains("截断"));
        analysis.Mermaid.Should().Contain("截断");
        new DeadlockPlaybackViewModel(analysis.Timeline).InferenceNotice.Should().Contain("截断");
        DeadlockGraphBuilder.GenerateAsciiCycle(analysis.Graph).Should().Contain("3 个环成员").And.Contain("截断");
    }

    [Fact]
    public void LongCycle_UsesIterativeTraversalAndKeepsAllMembersWhenPathsAreCutOff()
    {
        const int count = 10000;
        var processes = Enumerable.Range(0, count).Select(i => new DeadlockProcess($"p{i:D5}", i.ToString(), "", "", "", "", "", [])).ToList();
        var resources = Enumerable.Range(0, count).Select(i => new LockResource("keylock", "", "", "", "",
            [new($"p{(i + 1) % count:D5}", "X")], [new($"p{i:D5}", "X", "wait")], $"r{i}")).ToList();
        var graph = DeadlockGraphBuilder.Build(processes, resources, "p00000", new() { MaxCycleLength = 4, MaxCycleSearchSteps = 50 });
        graph.CycleAnalysis.ProcessIds.Should().HaveCount(count);
        graph.CycleAnalysis.PathsTruncated.Should().BeTrue();
        graph.Cycles.Should().BeEmpty();
    }

    [Fact]
    public void AllVictims_ArePreservedAndRevealedAtTheirOwnSteps()
    {
        var document = Overlap();
        document.Root!.Element("victim-list")!.Add(new XElement("victimProcess", new XAttribute("id", "c")));
        var analysis = new DeadlockAnalysisService().Analyze(document);
        analysis.Graph.VictimProcessIds.Should().BeEquivalentTo(new[] { "a", "c" });
        var victims = analysis.Timeline.Events.Where(e => e.IsVictim).ToArray();
        victims.Should().HaveCount(2);
        var layout = new DeadlockGraphLayoutService().BuildLayout(analysis.Graph);
        new DeadlockGraphPlacementService().PlaceNodes(layout, analysis.Graph.VictimProcessId, 800, 600)
            .Processes.Where(p => p.IsVictim).Select(p => p.Process.PrimaryId).Should().BeEquivalentTo(new[] { "a", "c" });
        var state = new DeadlockPlaybackStateService().BuildState(analysis.Timeline, victims[0].StepNumber, false,
            ["proc_id_a", "proc_id_c"], []);
        state.Nodes["proc_id_a"].IsVictimRevealed.Should().BeTrue();
        state.Nodes["proc_id_c"].IsVictimRevealed.Should().BeFalse();
        var visualService = new DeadlockGraphVisualStateService();
        visualService.CreatePlaybackNodeState(state.Nodes["proc_id_c"]).IsInCycle.Should().BeTrue();
        var reset = visualService.CreateResetNodeState(isVictim: true, isInCycle: true);
        reset.IsVictim.Should().BeTrue(); reset.IsVictimRevealed.Should().BeTrue(); reset.IsInCycle.Should().BeTrue();
        analysis.Mermaid.Split(":::victim").Should().HaveCount(3);
    }

    [Theory]
    [InlineData("exchangeEvent")]
    [InlineData("SyncPoint")]
    public void ParallelResources_RetainOriginalFieldsAndSources(string resourceType)
    {
        var document = Overlap();
        var resource = document.Root!.Element("resource-list")!.Elements().First();
        resource.Name = resourceType;
        resource.SetAttributeValue("WaitType", "e_waitPortOpen");
        resource.SetAttributeValue("nodeId", "7");
        resource.Element("owner-list")!.Elements().First().SetAttributeValue("ownerActivity", "notYetOpened");
        resource.Element("waiter-list")!.Elements().First().SetAttributeValue("waiterActivity", "waitForAllOwnersToOpen");
        var process = document.Root.Element("process-list")!.Elements().First();
        process.Add(new XElement("executionStack", new XElement("frame", new XAttribute("sqlhandle", "synthetic"), "unknown")));
        var output = new DeadlockAnalysisService().Analyze(document);
        var fact = output.Resources[0];
        fact.Id.Should().Be("res_0"); fact.SourceId.Should().Be("ab");
        fact.RawFields["WaitType"].Should().Be("e_waitPortOpen");
        fact.Owners[0].RawFields["ownerActivity"].Should().Be("notYetOpened");
        fact.Waiters[0].RawFields["waiterActivity"].Should().Be("waitForAllOwnersToOpen");
        fact.RawXml.Should().Contain("nodeId=\"7\"");
        fact.Source.XmlPath.Should().Contain(resourceType);
        output.Processes[0].ExecutionStack[0].RawXml.Should().Contain("unknown");
        output.Processes[0].RawXml.Should().Contain("sqlhandle");
        var pattern = output.Patterns.Single(p => p.RuleId == "RULE_DEADLOCK_PARALLEL");
        pattern.Evidence.Should().Contain(e => e.ResourceId == fact.Id && e.Source != null);
        pattern.LikelyCause.Should().Contain("不能");
        pattern.SourceFingerprint.Should().Be(output.Graph.SourceFingerprint).And.NotBeEmpty();
        Action mutate = () => ((IDictionary<string, string>)fact.RawFields).Add("new", "value");
        mutate.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void RepeatedResourceSourceIds_AreKeptAsSeparateOccurrences()
    {
        var document = Document(("a", "b", "same"), ("a", "b", "same"), ("b", "a", "return"));
        var graph = new DeadlockAnalysisService().Analyze(document).Graph;
        graph.Resources.Should().HaveCount(3);
        graph.Resources.Select(r => r.ResourceKey).Distinct().Should().HaveCount(3);
        graph.Cycles.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("RangeS-S", true)]
    [InlineData("RangeI-N", true)]
    [InlineData("RangeX-U", true)]
    [InlineData("RangeBogus", false)]
    [InlineData("X", false)]
    public void RangeDiagnosis_RequiresObservedKnownModeAndAttachesItsSource(string mode, bool hit)
    {
        var document = Overlap();
        document.Descendants("process").First().SetAttributeValue("isolationlevel", "serializable");
        document.Descendants("waiter").Last().SetAttributeValue("mode", mode);
        var analysis = new DeadlockAnalysisService().Analyze(document);
        var patterns = analysis.Patterns.Where(p => p.RuleId == "RULE_DEADLOCK_RANGE").ToArray();
        patterns.Length.Should().Be(hit ? 1 : 0);
        if (hit)
        {
            patterns[0].Evidence.Should().Contain(e => e.Value.Contains(mode) && e.Source != null && e.ResourceId != null);
            patterns[0].LikelyCause.Should().Contain("不能");
        }
    }

    [Fact]
    public void RangeDiagnosis_AlsoAcceptsCapturedResourceMode()
    {
        var document = Overlap();
        document.Descendants("keylock").First().SetAttributeValue("mode", "RangeX-X");
        var analysis = new DeadlockAnalysisService().Analyze(document);
        analysis.Patterns.Single(p => p.RuleId == "RULE_DEADLOCK_RANGE").Evidence.Should().Contain(e => e.Name == "mode" && e.Value == "RangeX-X");
    }

    [Theory]
    [InlineData("nonnumeric")]
    [InlineData("-1")]
    [InlineData("0")]
    public void InvalidOrZeroEcid_DoesNotEstablishParallelEvidence(string ecid)
    {
        var document = Overlap();
        document.Descendants("process").First().SetAttributeValue("ecid", ecid);
        new DeadlockAnalysisService().Analyze(document).Patterns.Should().NotContain(p => p.RuleId == "RULE_DEADLOCK_PARALLEL");
    }

    [Fact]
    public void DanglingReferences_AreWarnedAndDoNotCreatePhantomCycles()
    {
        var document = Overlap();
        document.Descendants("owner").First().SetAttributeValue("id", "missing");
        document.Descendants("victimProcess").First().SetAttributeValue("id", "missingVictim");
        var analysis = new DeadlockAnalysisService().Analyze(document);
        analysis.Warnings.Should().Contain(w => w.Contains("missing"));
        analysis.Graph.Edges.Should().NotContain(e => e.ToProcessId == "missing");
        analysis.Graph.VictimProcessIds.Should().Contain("missingVictim");
        analysis.Timeline.Events.Should().NotContain(e => e.ProcessId.StartsWith("missing"));
    }

    [Fact]
    public void DuplicateProcessIds_AreRejectedInsteadOfSilentlyMergingEvidence()
    {
        var document = Overlap();
        document.Descendants("process").Last().SetAttributeValue("id", "a");
        DeadlockXmlParser.TryParseDeadlockXml(document).Errors.Should().Contain(e => e.Contains("DEADLOCK_PROCESS_ID_INVALID"));
    }

    [Fact]
    public void SelfReference_IsPreservedAsRawLinksWithoutInventingASelfDeadlock()
    {
        var analysis = new DeadlockAnalysisService().Analyze(Document(("a", "a", "self")));
        analysis.Graph.ResourceLinks.Should().HaveCount(2);
        analysis.Graph.Edges.Should().BeEmpty();
        analysis.Graph.CycleAnalysis.ProcessIds.Should().BeEmpty();
        analysis.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    public void EvidenceDisplayBudget_IsVisibleAndDoesNotDropGraphFacts()
    {
        var document = Document(Enumerable.Range(0, 80).Select(i => ($"p{i}", $"p{i + 1}", $"r{i}")).ToArray());
        var analysis = new DeadlockAnalysisService().Analyze(document);
        var chain = analysis.Patterns.Single(p => p.RuleId == "RULE_DEADLOCK_CHAIN");
        chain.Evidence.Should().HaveCount(64);
        chain.EvidenceTruncated.Should().BeTrue();
        DeadlockDiagnosticFormatter.FormatEvidence(chain).Should().Contain("截断");
        analysis.Graph.Edges.Should().HaveCount(80);
    }

    [Fact]
    public void HtmlAndSelectedDetails_ExposeTheSameEvidenceAndSummary()
    {
        var document = Overlap();
        var analysis = new DeadlockAnalysisService().Analyze(document);
        var report = new AnalysisReportController().BuildDeadlockHtmlReport(document, "synthetic.xdl", "");
        report.MermaidCode.Should().Be(analysis.Mermaid);
        report.SummaryText.Should().Contain(analysis.Graph.CycleAnalysis.Summary);
        var pattern = analysis.Patterns.Single(p => p.RuleId == "RULE_DEADLOCK_CHAIN");
        var details = new DeadlockSelectionDetailService().BuildPatternDetail(pattern);
        details.Should().Contain(pattern.SourceFingerprint).And.Contain(pattern.Evidence[0].EdgeId!);
        new DeadlockSelectionDetailService().BuildResourceDetail(analysis.Resources[0]).Should().Contain("原始资源 ID: ab").And.Contain("owner-list");
    }

    [Fact]
    public void HtmlReport_ReusesCompletedAnalysisIncludingItsCustomBudget()
    {
        var document = Overlap();
        var analysis = new DeadlockAnalysisService(options: new() { MaxCycleLength = 1 }).Analyze(document);
        var report = new HtmlReportActionService().BuildReport(0, document, "synthetic.xdl", "", null, null, XNamespace.None,
            deadlockAnalysis: analysis).Report!;
        report.SummaryText.Should().Contain("截断").And.Contain("3 个环成员");
        report.MermaidCode.Should().Be(analysis.Mermaid);
    }
}
