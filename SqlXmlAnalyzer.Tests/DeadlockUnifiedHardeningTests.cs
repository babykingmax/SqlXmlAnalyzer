using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Parsers;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class DeadlockUnifiedHardeningTests
{
    internal static XDocument DuplicateRelations()
    {
        var document = DeadlockUnifiedGraphTests.Document(("a", "b", "forward"), ("b", "a", "back"));
        var owner = document.Descendants("owner").First();
        owner.SetAttributeValue("ownerActivity", "first");
        var second = new XElement(owner);
        second.SetAttributeValue("ownerActivity", "second");
        owner.AddAfterSelf(second);
        return document;
    }

    [Fact]
    public void ParallelDrawing_KeepsDistinctBoundedPositionsWhenSourceRelationsAreReordered()
    {
        var document = DuplicateRelations();
        var list = document.Descendants("owner-list").First();
        for (int i = 0; i < 7; i++)
        {
            var extra = new XElement(list.Elements().First());
            extra.SetAttributeValue("ownerActivity", "extra" + i);
            list.Add(extra);
        }
        Dictionary<string, double> Positions()
        {
            var graph = new DeadlockAnalysisService().Analyze(document).Graph;
            var layout = new DeadlockGraphLayoutService().BuildLayout(graph);
            return new DeadlockGraphEdgeService().BuildEdges(layout.Resources)
                .Where(e => e.ToId == "proc_id_b").ToDictionary(e => e.EvidenceId, e => e.ParallelOffset);
        }
        var before = Positions();
        before.Should().HaveCount(9);
        before.Values.Distinct().Should().HaveCount(9);
        before.Values.Should().OnlyContain(offset => Math.Abs(offset) < 25, "connections must stay within the smallest node half-height");
        var reversed = list.Elements().Reverse().ToArray(); list.RemoveNodes(); list.Add(reversed);
        Positions().Should().BeEquivalentTo(before);
    }

    [Fact]
    public void ManyVictims_PreserveEveryIdentityAndWarnOnceForEachMissingReference()
    {
        const int count = 10000;
        var document = DeadlockUnifiedGraphTests.Document(("p0", "p1", "single"));
        document.Root!.Element("process-list")!.ReplaceNodes(Enumerable.Range(0, count).Select(i =>
            new XElement("process", new XAttribute("id", $"p{i}"), new XAttribute("spid", i + 50))));
        document.Root.Element("victim-list")!.ReplaceNodes(Enumerable.Range(0, count).Select(i =>
            new XElement("victimProcess", new XAttribute("id", $"p{i}"))));
        document.Root.Element("victim-list")!.Add(new XElement("victimProcess", new XAttribute("id", "missing")),
            new XElement("victimProcess", new XAttribute("id", "missing")), new XElement("victimProcess"),
            new XElement("victimProcess", new XAttribute("id", "P0")));

        var parsed = DeadlockXmlParser.TryParseDeadlockXml(SafeXmlHelper.ParseSafe(document.ToString()));

        parsed.IsSuccess.Should().BeTrue();
        parsed.Value!.VictimIds.Should().HaveCount(count + 2).And.Contain("p9999").And.Contain("P0");
        parsed.Value.VictimId.Should().Be("P0", "victim ordering remains ordinal and case-sensitive");
        parsed.Warnings.Should().HaveCount(2);
        parsed.Warnings.Should().ContainSingle(w => w.Contains("'missing'"));
    }

    [Theory]
    [InlineData("owner", "missing")]
    [InlineData("waiter", "missing")]
    [InlineData("owner", "b")]
    [InlineData("waiter", "a")]
    public void RangeEvidence_UsesActualModeAndSourceEvenWhenTheProcessIsMissing(string role, string process)
    {
        var document = DeadlockUnifiedGraphTests.Document(("a", "b", "real"), ("b", "a", "decoy"));
        var relation = document.Descendants(role).First();
        relation.SetAttributeValue("id", process);
        relation.SetAttributeValue("mode", " \trAnGeS-s\r\n");
        var decoy = document.Descendants("keylock").Last();
        foreach (string field in new[] { "objectname", "indexname", "id" }) decoy.SetAttributeValue(field, "RangeS-S");
        decoy.Descendants("waiter").Single().SetAttributeValue("requestType", "RangeS-S");
        decoy.Descendants("owner").Single().SetAttributeValue("ownerActivity", "RangeS-S");
        var analysis = new DeadlockAnalysisService().Analyze(document);

        var pattern = analysis.Patterns.Single(p => p.RuleId == "RULE_DEADLOCK_RANGE");
        var fact = pattern.Evidence.Should().ContainSingle().Subject;
        fact.Name.Should().Be(role + ".mode");
        fact.Value.Should().Be("RangeS-S");
        fact.ProcessId.Should().Be(process);
        fact.ResourceId.Should().Be("res_0");
        fact.EdgeId.Should().BeNull("raw field evidence must not invent a confirmed dependency");
        fact.Source!.XmlPath.Should().Contain(role + "-list");
        pattern.Description.Should().Contain("RangeS-S");
        if (process == "missing")
        {
            analysis.Graph.ResourceLinks.Should().NotContain(l => l.ProcessId == "missing");
            analysis.Warnings.Should().Contain(w => w.Contains("missing"));
        }
    }

    [Theory]
    [InlineData("RangeS-S bogus")]
    [InlineData("RangeBogus")]
    [InlineData("X")]
    public void NamesActivitiesAndUnknownModes_DoNotEstablishRangeEvidence(string mode)
    {
        var document = DuplicateRelations();
        foreach (var resource in document.Descendants("keylock"))
        {
            resource.SetAttributeValue("objectname", "RangeS-S");
            resource.SetAttributeValue("mode", mode);
            foreach (var owner in resource.Descendants("owner")) owner.SetAttributeValue("ownerActivity", "RangeS-S");
            foreach (var waiter in resource.Descendants("waiter")) waiter.SetAttributeValue("requestType", "RangeS-S");
        }
        new DeadlockAnalysisService().Analyze(document).Patterns.Should().NotContain(p => p.RuleId == "RULE_DEADLOCK_RANGE");
    }

    [Fact]
    public void RangeEvidenceBudget_RemainsVisibleWithoutLosingRawObservations()
    {
        var document = DeadlockUnifiedGraphTests.Document(Enumerable.Range(0, 80).Select(i => ($"p{i}", $"p{i + 1}", $"r{i}")).ToArray());
        foreach (var owner in document.Descendants("owner")) owner.SetAttributeValue("mode", "RangeX-X");
        document.Descendants("waiter").Last().SetAttributeValue("mode", "RangeI-N");
        var analysis = new DeadlockAnalysisService().Analyze(document);
        var pattern = analysis.Patterns.Single(p => p.RuleId == "RULE_DEADLOCK_RANGE");
        pattern.Evidence.Should().HaveCount(64).And.OnlyContain(e => e.Name == "owner.mode");
        pattern.EvidenceTruncated.Should().BeTrue();
        pattern.Description.Should().Contain("RangeI-N").And.Contain("RangeX-X");
        DeadlockDiagnosticFormatter.FormatEvidence(pattern).Should().Contain("截断");
        analysis.Resources.SelectMany(r => r.Owners).Should().HaveCount(80);
    }

    [Fact]
    public void ResourceOnlyRangeMode_HasEvidenceWhenNoRelationCanBeLinked()
    {
        var document = DuplicateRelations();
        foreach (var relation in document.Descendants("owner").Concat(document.Descendants("waiter"))) relation.SetAttributeValue("id", "missing");
        document.Descendants("keylock").First().SetAttributeValue("mode", "\t rangeX-U ");
        var analysis = new DeadlockAnalysisService().Analyze(document);
        analysis.Graph.ResourceLinks.Should().BeEmpty();
        analysis.Patterns.Single(p => p.RuleId == "RULE_DEADLOCK_RANGE").Evidence.Should()
            .ContainSingle(e => e.Name == "mode" && e.Value == "RangeX-U" && e.ResourceId == "res_0" && e.Source != null);
    }

    [Fact]
    public void LegacyEdges_KeepTypedRangeModesAndCorrectEndpointAttribution()
    {
        var graph = new DeadlockGraph();
        graph.Edges.Add(new() { FromProcessId = "waiter", ToProcessId = "owner", EdgeId = "edge",
            RequestedMode = " RangeI-N ", HeldMode = "RangeX-X" });
        var evidence = DeadlockPatternAnalyzer.IdentifyPatterns(graph).Single(p => p.RuleId == "RULE_DEADLOCK_RANGE").Evidence;
        evidence.Should().Contain(e => e.Name == "requested-mode" && e.ProcessId == "waiter" && e.EdgeId == "edge");
        evidence.Should().Contain(e => e.Name == "held-mode" && e.ProcessId == "owner" && e.EdgeId == "edge");
    }

    [Fact]
    public void MermaidAndHtml_RetainBothIndexesOnTheSameTable()
    {
        var document = SafeXmlHelper.ParseSafe(Utilities.EmbeddedResourceHelper.GetResourceContent("deadlock_bookmark_lookup.xdl"));
        var analysis = new DeadlockAnalysisService().Analyze(document);
        var report = new AnalysisReportController().BuildDeadlockHtmlReport(document, "fixture.xdl", "", analysis);
        analysis.Mermaid.Should().Contain("IX_TableA").And.Contain("PK_TableA").And.Contain("lock1").And.Contain("lock2");
        report.MermaidCode.Should().Be(analysis.Mermaid);
    }

    [Fact]
    public void Mermaid_EscapesIndexNamesBeforeEmbeddingInResourceLabels()
    {
        var document = DuplicateRelations();
        document.Descendants("keylock").First().SetAttributeValue("indexname", "IX_\"quoted\"\r\n<script> [x]");
        var mermaid = new DeadlockAnalysisService().Analyze(document).Mermaid;
        mermaid.Should().Contain("IX_").And.NotContain("IX_\"quoted\"").And.NotContain("<script>");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Playback_DistinguishesParallelEvidenceAndKeepsItsOwnBadgeStep(bool focus)
    {
        var timeline = new DeadlockTimelineParser.ParsedDeadlock();
        timeline.Events.AddRange([
            new DeadlockEvent { Type = "Grant", ProcessId = "b", ResourceId = "res_0", EvidenceId = "first", StepNumber = 1, IsInCycle = false },
            new DeadlockEvent { Type = "Grant", ProcessId = "b", ResourceId = "res_0", EvidenceId = "second", StepNumber = 2, IsInCycle = true }
        ]);
        var first = new DeadlockPlaybackEdgeKey("res_single_0", "proc_id_b", "first");
        var second = first with { EvidenceId = "second" };
        var unknown = first with { EvidenceId = "unknown" };
        var legacy = first with { EvidenceId = "" };
        var service = new DeadlockPlaybackStateService();
        var before = service.BuildState(timeline, 1, focus, [], [first, second, unknown, legacy]);
        before.Edges[first].IsActive.Should().Be(!focus);
        before.Edges[first].IsCollapsed.Should().Be(focus);
        before.Edges[second].IsActive.Should().BeFalse();
        before.Edges[unknown].IsActive.Should().BeFalse();
        var after = service.BuildState(timeline, 2, focus, [], [first, second, unknown, legacy]);
        after.Edges[second].BadgeStepNumber.Should().Be(2);
        after.Edges[legacy].BadgeStepNumber.Should().Be(focus ? 2 : 1);
        after.Edges[unknown].BadgeStepNumber.Should().BeNull();
        service.BuildState(timeline, 0, focus, [], [first, second]).Edges.Values.Should().OnlyContain(e => !e.IsActive && e.BadgeStepNumber == null);
    }
}
