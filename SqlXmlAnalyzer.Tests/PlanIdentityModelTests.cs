using System.IO;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Analysis;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanIdentityModelTests : IDisposable
{
    internal static readonly XNamespace Ns = InputRecognitionService.ShowPlanNamespace;
    internal static string Fixture => InputRecognitionTests.Fixture("imp10_scoped_identities.sqlplan");
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"SqlXmlAnalyzer-Imp10-{Guid.NewGuid():N}");
    public PlanIdentityModelTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Read_RepeatedSourceIdsRemainDistinctAcrossEveryHierarchyLevel()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Fixture));
        var input = await new InputRecognitionService().ReadAsync(stream, sourceName: "capture.sqlplan");
        var plan = PlanIdentityAdapter.GetDocument(input.Document)!;
        plan.Envelope.Should().BeSameAs(input.Envelope);
        plan.Batches.Should().HaveCount(2);
        plan.Statements.Should().HaveCount(4);
        plan.QueryPlans.Should().HaveCount(4);
        plan.Operators.Should().HaveCount(6);
        plan.Operators.Select(op => op.Key).Should().OnlyHaveUniqueItems();
        plan.Operators.Select(op => op.Key.ToString()).Should().OnlyHaveUniqueItems();
        plan.Statements[0].StatementId.Should().Be(plan.Statements[1].StatementId);
        plan.Statements[0].QueryHash.Should().Be(plan.Statements[1].QueryHash);
        plan.Statements[0].Key.Should().NotBe(plan.Statements[1].Key);
        plan.Statements[2].Kind.Should().Be("StmtUseDb");
        plan.Statements[2].QueryPlans.Should().BeEmpty();
        plan.Statements[3].QueryPlans.Select(q => q.Key.QueryPlanOrdinal).Should().Equal(1, 2);
        plan.Operators.Should().OnlyContain(op => op.Key.QueryPlan.Statement.Batch.DocumentId == input.Envelope!.DocumentId);
        using var sameBytes = new MemoryStream(Encoding.UTF8.GetBytes(Fixture));
        var again = await new InputRecognitionService().ReadAsync(sameBytes, sourceName: "renamed.xml");
        PlanIdentityAdapter.GetDocument(again.Document)!.Operators.Select(op => op.Key).Should().Equal(plan.Operators.Select(op => op.Key));
    }

    [Fact]
    public void Objects_AreOwnedByTheNearestOperatorAndRetainAliasAndRawAttributes()
    {
        var input = new InputRecognitionService().Parse(Fixture);
        var plan = PlanIdentityAdapter.GetDocument(input.Document)!;
        plan.Operators[0].Objects.Should().BeEmpty("the join cannot inherit its child's object");
        var sales = plan.Operators[1].Objects.Single();
        var audit = plan.Operators[3].Objects.Single();
        sales.Identity.Should().Be(new SqlObjectIdentity(null, "dbA", "sales", "T"));
        audit.Identity.Should().Be(new SqlObjectIdentity(null, "dbA", "audit", "T"));
        sales.Alias.Should().Be("s");
        sales.SourceAttributes["Alias"].Should().Be("[s]");
        sales.SourceAttributes["FutureField"].Should().Be("retained");
        sales.Location.Source.Line.Should().BeGreaterThan(0);
        sales.Location.Source.XmlPath.Should().Contain("{http://schemas.microsoft.com/sqlserver/2004/07/showplan}");
        plan.Operators[^1].Objects.Single().Identity.Object.Should().Be("T.part]x");
        plan.Operators[^1].Objects.Single().Identity.Server.Should().Be("remote");
    }

    [Fact]
    public void NestedStatements_KeepParentsWithoutAbsorbingChildQueryPlansOrExtensionNodes()
    {
        string xml = Wrap("<StmtCond StatementId='1'><Condition><QueryPlan><RelOp NodeId='0'/></QueryPlan></Condition>" +
            "<Then><Statements><StmtSimple StatementId='1'><QueryPlan><RelOp NodeId='0'/></QueryPlan>" +
            "<UDF><Statements><StmtSimple StatementId='1'/></Statements></UDF></StmtSimple></Statements></Then></StmtCond>" +
            "<StmtSimple><QueryPlan><InternalInfo><Statements><StmtSimple><QueryPlan><RelOp NodeId='fake'/></QueryPlan></StmtSimple></Statements></InternalInfo></QueryPlan></StmtSimple>");
        var plan = PlanIdentityAdapter.GetDocument(SafeXmlHelper.ParseSafe(xml))!;
        plan.Statements.Should().HaveCount(4);
        plan.Statements[1].Parent.Should().Be(plan.Statements[0].Key);
        plan.Statements[2].Parent.Should().Be(plan.Statements[1].Key);
        plan.Statements[3].Parent.Should().BeNull();
        plan.Statements[0].QueryPlans.Should().ContainSingle();
        plan.Statements[1].QueryPlans.Should().ContainSingle();
        plan.Operators.Should().HaveCount(2);
        plan.Operators.Should().NotContain(op => op.Key.NodeId == "fake");
    }

    [Theory]
    [InlineData("", "PLAN_NODE_ID_MISSING")]
    [InlineData("NodeId='7'", "PLAN_NODE_ID_DUPLICATE")]
    public void MissingOrDuplicateNodeIds_KeepEveryOperatorWithExplicitDiagnostics(string attribute, string code)
    {
        var plan = PlanIdentityAdapter.GetDocument(SafeXmlHelper.ParseSafe(Wrap(
            $"<StmtSimple><QueryPlan><RelOp {attribute}><NestedLoops><RelOp {attribute}/></NestedLoops></RelOp></QueryPlan></StmtSimple>")))!;
        plan.Operators.Should().HaveCount(2);
        plan.Operators.Select(op => op.Key).Should().OnlyHaveUniqueItems();
        plan.Operators[1].Parent.Should().Be(plan.Operators[0].Key);
        plan.Diagnostics.Should().OnlyContain(d => d.Code == code);
        plan.Diagnostics.Should().NotBeEmpty();
        plan.GetOperatorSource(plan.Operators[0].Key).Should().NotBeSameAs(plan.GetOperatorSource(plan.Operators[1].Key));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("[]", null)]
    [InlineData("[T.part]", "T.part")]
    [InlineData("[a]]b]", "a]b")]
    [InlineData("\"a\"\"b\"", "a\"b")]
    [InlineData("[ ]", " ")]
    [InlineData("MixedCase", "MixedCase")]
    public void IdentifierDecoding_PreservesOnePartWithoutSplittingDotsOrFoldingCase(string? raw, string? expected)
    {
        SqlObjectIdentity.DecodeIdentifier(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("Server")]
    [InlineData("Database")]
    [InlineData("Schema")]
    [InlineData("Object")]
    public void ObjectIdentity_DifferentKnownPartsAndCaseRemainDistinct(string part)
    {
        var first = new SqlObjectIdentity("Server", "Database", "Schema", "Object");
        var other = part switch
        {
            "Server" => first with { Server = "server" },
            "Database" => first with { Database = "database" },
            "Schema" => first with { Schema = "schema" },
            _ => first with { Object = "object" }
        };
        first.IsSameKnownObject(other).Should().BeFalse();
        first.IsSameKnownObject(first with { }).Should().BeTrue();
    }

    [Fact]
    public void UnknownIdentity_DoesNotInferDatabaseFromUseDbOrMergeIncompleteObjects()
    {
        var identity = new SqlObjectIdentity(null, "db", "dbo", "T");
        identity.IsSameKnownObject(identity with { }).Should().BeFalse();
        var plan = PlanIdentityAdapter.GetDocument(SafeXmlHelper.ParseSafe(Wrap(
            "<StmtUseDb Database='db'/><StmtSimple><QueryPlan><RelOp NodeId='0'><TableScan><Object Table='T' Alias='x'/></TableScan></RelOp></QueryPlan></StmtSimple>")))!;
        var reference = plan.Operators.Single().Objects.Single();
        reference.Identity.Database.Should().BeNull();
        reference.Identity.Schema.Should().BeNull();
        reference.Identity.Object.Should().Be("T");
        reference.Alias.Should().Be("x");
    }

    [Fact]
    public async Task SourceMutation_InvalidatesLocationAccessAndRebuildsWithoutOldHash()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Fixture));
        var input = await new InputRecognitionService().ReadAsync(stream);
        var before = PlanIdentityAdapter.GetDocument(input.Document)!;
        PlanIdentityAdapter.GetDocument(input.Document).Should().BeSameAs(before);
        input.Document!.Descendants(Ns + "StmtSimple").First().SetAttributeValue("StatementText", "SELECT changed;");
        Action staleLookup = () => before.GetOperatorSource(before.Operators[0].Key);
        staleLookup.Should().Throw<InvalidDataException>().WithMessage("PLAN_SOURCE_CHANGED*");
        var after = PlanIdentityAdapter.GetDocument(input.Document)!;
        after.Envelope.SourceHash.Should().BeNull();
        after.Envelope.DocumentId.Should().NotBe(before.Envelope.DocumentId);
        before.Statements[0].Text.Should().Be("SELECT Id FROM sales.T;");
        after.Statements[0].Text.Should().Be("SELECT changed;");
    }

    [Fact]
    public void QueryPlanSelection_RequiresExplicitMembershipForMultiplePlans()
    {
        var first = PlanIdentityAdapter.GetDocument(SafeXmlHelper.ParseSafe(Fixture))!;
        var other = PlanIdentityAdapter.GetDocument(SafeXmlHelper.ParseSafe(Fixture))!;
        Action unspecified = () => PlanIdentityAdapter.SelectQueryPlan(first, null);
        Action foreign = () => PlanIdentityAdapter.SelectQueryPlan(first, other.QueryPlans[0].Key);
        unspecified.Should().Throw<InvalidDataException>().WithMessage("PLAN_SELECTION_REQUIRED*");
        foreign.Should().Throw<InvalidDataException>().WithMessage("PLAN_SELECTION_NOT_FOUND*");
        PlanIdentityAdapter.SelectQueryPlan(first, first.QueryPlans[3].Key).Should().BeSameAs(first.QueryPlans[3]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KnownModelFailureAndCancellation_DoNotGenerateDump(bool cancel)
    {
        var reporter = new Reporter();
        var engine = new SqlXmlAnalysisEngine(unexpectedErrors: reporter,
            planBuilder: new FailingBuilder(cancel ? new OperationCanceledException() : new InvalidDataException("synthetic invalid model")));
        var result = engine.Analyze(Fixture);
        result.InputStatus.Should().Be(cancel ? InputStatus.Cancelled : InputStatus.Invalid);
        result.IsSuccess.Should().BeFalse();
        reporter.Calls.Should().Be(0);
    }

    [Fact]
    public void CancelledBuilder_DoesNotPublishAModelOrDump()
    {
        var reporter = new Reporter();
        var input = new InputRecognitionService().Parse(Fixture);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action build = () => new PlanDocumentBuilder(reporter).Build(input.Document!, input.Envelope!, cancellation.Token);
        build.Should().Throw<OperationCanceledException>();
        reporter.Calls.Should().Be(0);
    }

    [Fact]
    public void UnknownModelFailure_ProducesValidatedDumpAndBuildAppropriateLogs()
    {
        Logger.Shutdown();
        string log = Path.Combine(_directory, "identity.log");
        Logger.Initialize(customLogFilePath: log);
        var reporter = new UnexpectedErrorReporter(new WindowsMiniDumpWriter(), Path.Combine(_directory, "dumps"));
        var error = new InvalidOperationException("IMP10 synthetic identity construction fault");
        var result = new SqlXmlAnalysisEngine(unexpectedErrors: reporter, planBuilder: new FailingBuilder(error)).Analyze(Fixture);
        result.IsSuccess.Should().BeFalse();
        result.InputStatus.Should().Be(InputStatus.UnexpectedError);
        result.InputErrorMessage.Should().Contain("DUMP");
        var incident = reporter.Report(error, "already-captured");
        incident.DumpCreated.Should().BeTrue(incident.Failure);
        MinidumpValidator.Validate(incident.DumpPath!);
        File.ReadAllText(incident.MetadataPath!).Should().Contain("SqlXmlAnalysisEngine.Analyze");
        PlanIdentityAdapter.GetDocument(SafeXmlHelper.ParseSafe(Wrap("<StmtSimple><QueryPlan><RelOp/></QueryPlan></StmtSimple>")));
        new InputRecognitionService().Parse("<unrelated/>");
        Logger.Shutdown();
        string text = File.ReadAllText(log);
        text.Should().Contain("[ERROR]").And.Contain("[CRITICAL]");
#if DEBUG
        text.Should().Contain("[DEBUG]").And.Contain("[WARN]");
#else
        text.Should().NotContain("[DEBUG]").And.NotContain("[WARN]").And.NotContain("[INFO]");
#endif
        text.Should().NotContain("SELECT Id FROM sales.T");
    }

    [Fact]
    public void DiagnosticProviderFailure_PreservesModelFailureResult()
    {
        var result = new SqlXmlAnalysisEngine(unexpectedErrors: new BrokenReporter(),
            planBuilder: new FailingBuilder(new InvalidOperationException("synthetic"))).Analyze(Fixture);
        result.IsSuccess.Should().BeFalse();
        result.InputErrorMessage.Should().Contain("DUMP 生成失败");
    }

    internal static string Wrap(string statements) => $"<ShowPlanXML xmlns='{Ns}'><BatchSequence><Batch><Statements>{statements}</Statements></Batch></BatchSequence></ShowPlanXML>";
    private sealed class FailingBuilder(Exception failure) : IPlanDocumentBuilder
    {
        public PlanDocument Build(XDocument document, DocumentEnvelope envelope, CancellationToken cancellationToken = default) => throw failure;
    }
    private sealed class Reporter : IUnexpectedErrorReporter
    {
        public int Calls { get; private set; }
        public UnexpectedErrorReport Report(Exception exception, string operation) { Calls++; return new(null, null, "test"); }
    }
    private sealed class BrokenReporter : IUnexpectedErrorReporter
    {
        public UnexpectedErrorReport Report(Exception exception, string operation) => throw new IOException("diagnostic sink unavailable");
    }
    public void Dispose()
    {
        Logger.Shutdown();
        string path = Path.GetFullPath(_directory);
        if (!path.StartsWith(Path.Combine(Path.GetFullPath(Path.GetTempPath()), "SqlXmlAnalyzer-Imp10-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected cleanup target");
        Directory.Delete(path, recursive: true);
    }
}
