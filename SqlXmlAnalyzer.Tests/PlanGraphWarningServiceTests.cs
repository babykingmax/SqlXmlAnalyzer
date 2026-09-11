using FluentAssertions;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class PlanGraphWarningServiceTests
    {
        private static readonly XNamespace Ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
        private readonly PlanGraphWarningService _service = new();

        [Fact]
        public void BuildWarnings_WhenNoWarningsExist_ReturnsEmptyTextAndInfoSeverity()
        {
            XElement relOp = new(Ns + "RelOp");

            PlanGraphWarningResult result = _service.BuildWarnings(
                relOp,
                Ns,
                CreateContext(),
                Array.Empty<AnalysisResult>());

            result.WarningsText.Should().BeEmpty();
            result.HighestSeverity.Should().Be("Info");
        }

        [Fact]
        public void BuildWarnings_IncludesRelOpWarningDetails()
        {
            XElement relOp = XElement.Parse($"""
                <RelOp xmlns="{Ns}">
                  <Warnings>
                    <PlanAffectingConvert Expression="CONVERT_IMPLICIT(int,[Col],0)" />
                    <HashWarning HashWarningDetail="SpillToTempDb" />
                  </Warnings>
                </RelOp>
                """);

            PlanGraphWarningResult result = _service.BuildWarnings(
                relOp,
                Ns,
                CreateContext(),
                Array.Empty<AnalysisResult>());

            result.WarningsText.Should().Contain("⚠ 操作符警告: PlanAffectingConvert");
            result.WarningsText.Should().Contain("[转换表达式]: CONVERT_IMPLICIT(int,[Col],0)");
            result.WarningsText.Should().Contain("⚠ 操作符警告: HashWarning (SpillToTempDb)");
        }

        [Fact]
        public void BuildWarnings_IncludesImplicitConversionScalars()
        {
            XElement relOp = XElement.Parse($"""
                <RelOp xmlns="{Ns}">
                  <IndexScan>
                    <Predicate>
                      <ScalarOperator ScalarString="CONVERT_IMPLICIT(varchar(10),[Account],0)" />
                    </Predicate>
                  </IndexScan>
                </RelOp>
                """);

            PlanGraphWarningResult result = _service.BuildWarnings(
                relOp,
                Ns,
                CreateContext(),
                Array.Empty<AnalysisResult>());

            result.WarningsText.Should().Contain("隐式类型转换 (CONVERT_IMPLICIT)");
            result.WarningsText.Should().Contain("CONVERT_IMPLICIT(varchar(10),[Account],0)");
        }

        [Fact]
        public void BuildWarnings_WhenRootHasUnusedMemoryGrant_IncludesMemoryWarning()
        {
            XDocument document = XDocument.Parse($"""
                <ShowPlanXML xmlns="{Ns}">
                  <BatchSequence><Batch><Statements><StmtSimple><QueryPlan>
                    <MemoryGrantInfo GrantedMemory="20480" MaxUsedMemory="1024" />
                    <RelOp NodeId="0" />
                  </QueryPlan></StmtSimple></Statements></Batch></BatchSequence>
                </ShowPlanXML>
                """);
            XElement relOp = document.Descendants(Ns + "RelOp").Single();

            PlanGraphWarningResult result = _service.BuildWarnings(
                relOp,
                Ns,
                CreateContext(nodeId: "0"),
                Array.Empty<AnalysisResult>());

            result.WarningsText.Should().Contain("内存预估过度");
            result.WarningsText.Should().Contain("申请 20.0MB");
            result.WarningsText.Should().Contain("仅用 1.0MB");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BuildWarnings_MemoryEvidenceStaysWithinOwningQueryPlan(bool separateStatements)
        {
            XElement Plan(string grant, string used, string? warning) => new(Ns + "QueryPlan",
                new XElement(Ns + "MemoryGrantInfo", new XAttribute("GrantedMemory", grant), new XAttribute("MaxUsedMemory", used)),
                warning == null ? null : new XElement(Ns + "Warnings", new XElement(Ns + "MemoryGrantWarning", new XAttribute("GrantWarningKind", warning))),
                new XElement(Ns + "RelOp", new XAttribute("NodeId", "0")));
            var firstPlan = Plan("20480", "1024", "Excessive Grant");
            var secondPlan = Plan("1024", "1024", null);
            var statements = separateStatements
                ? new XElement(Ns + "Statements", new XElement(Ns + "StmtSimple", firstPlan), new XElement(Ns + "StmtSimple", secondPlan))
                : new XElement(Ns + "Statements", new XElement(Ns + "StmtSimple", firstPlan, secondPlan));
            _ = new XDocument(new XElement(Ns + "ShowPlanXML", new XElement(Ns + "BatchSequence", new XElement(Ns + "Batch", statements))));

            var first = _service.BuildWarnings(firstPlan.Element(Ns + "RelOp")!, Ns, CreateContext(nodeId: "0"), []);
            var second = _service.BuildWarnings(secondPlan.Element(Ns + "RelOp")!, Ns, CreateContext(nodeId: "0"), []);

            first.WarningsText.Should().Contain("申请 20.0MB").And.Contain("内存分配警告: Excessive Grant");
            second.WarningsText.Should().BeEmpty();
        }

        [Fact]
        public void BuildWarnings_MemoryWarningsUseRootAncestryInsteadOfNodeId()
        {
            var child = new XElement(Ns + "RelOp", new XAttribute("NodeId", "0"));
            var root = new XElement(Ns + "RelOp", new XAttribute("NodeId", "42"), new XElement(Ns + "NestedLoops", child));
            _ = new XElement(Ns + "QueryPlan", new XElement(Ns + "MemoryGrantInfo",
                new XAttribute("GrantedMemory", "20480"), new XAttribute("MaxUsedMemory", "1024")), root);

            _service.BuildWarnings(root, Ns, CreateContext(nodeId: "42"), []).WarningsText.Should().Contain("内存预估过度");
            _service.BuildWarnings(child, Ns, CreateContext(nodeId: "0"), []).WarningsText.Should().BeEmpty();
        }

        [Fact]
        public void BuildWarnings_NestedQueryPlanUsesItsOwnRootAndWarnings()
        {
            var nestedRoot = new XElement(Ns + "RelOp", new XAttribute("NodeId", "7"));
            var nestedPlan = new XElement(Ns + "QueryPlan",
                new XElement(Ns + "Warnings", new XElement(Ns + "MemoryGrantWarning", new XAttribute("GrantWarningKind", "Nested Grant"))), nestedRoot);
            var root = new XElement(Ns + "RelOp", new XAttribute("NodeId", "0"),
                new XElement(Ns + "UDF", new XElement(Ns + "Statements", new XElement(Ns + "StmtSimple", nestedPlan))));
            _ = new XDocument(new XElement(Ns + "QueryPlan", new XElement(Ns + "MemoryGrantInfo",
                new XAttribute("GrantedMemory", "20480"), new XAttribute("MaxUsedMemory", "1024")), root));

            string warnings = _service.BuildWarnings(nestedRoot, Ns, CreateContext(nodeId: "7"), []).WarningsText;

            warnings.Should().Contain("内存分配警告: Nested Grant").And.NotContain("内存预估过度");
        }

        [Fact]
        public void BuildWarnings_DetachedOperatorDoesNotBorrowDocumentMemoryEvidence()
        {
            var root = new XElement(Ns + "RelOp", new XAttribute("NodeId", "0"));
            _ = new XDocument(new XElement(Ns + "ShowPlanXML", new XElement(Ns + "MemoryGrantInfo",
                new XAttribute("GrantedMemory", "20480"), new XAttribute("MaxUsedMemory", "1024")), root));

            _service.BuildWarnings(root, Ns, CreateContext(nodeId: "0"), []).WarningsText.Should().BeEmpty();
        }

        [Fact]
        public void BuildWarnings_WhenThreadSkewExists_IncludesSkewWarning()
        {
            XElement relOp = new(Ns + "RelOp");

            PlanGraphWarningResult result = _service.BuildWarnings(
                relOp,
                Ns,
                CreateContext(isThreadDataSkewed: true),
                Array.Empty<AnalysisResult>());

            result.WarningsText.Should().Contain("线程数据倾斜 (Thread Data Skew)");
        }

        [Fact]
        public void BuildWarnings_WhenResidualIoThresholdIsExceeded_IncludesResidualIoDetails()
        {
            XElement relOp = new(Ns + "RelOp");

            PlanGraphWarningResult result = _service.BuildWarnings(
                relOp,
                Ns,
                CreateContext(
                    physicalOp: "Index Seek",
                    residualPredicate: "[Status]='Open'",
                    seekPredicate: "[CustomerId]=@p0",
                    hasActual: true,
                    hasActualRead: true,
                    actualRows: 100,
                    actualRowsRead: 5000),
                Array.Empty<AnalysisResult>());

            result.WarningsText.Should().Contain("**残差 I/O 警告**");
            result.WarningsText.Should().Contain("操作符: Index Seek");
            result.WarningsText.Should().Contain("实际读取行数: 5,000");
            result.WarningsText.Should().Contain("读取/返回比: 50.0 : 1");
        }

        [Fact]
        public void BuildWarnings_WhenResidualSeekHasNoRuntimeData_IncludesResidualPredicateWarning()
        {
            XElement relOp = new(Ns + "RelOp");

            PlanGraphWarningResult result = _service.BuildWarnings(
                relOp,
                Ns,
                CreateContext(
                    physicalOp: "Index Seek",
                    residualPredicate: "[Status]='Open'",
                    seekPredicate: "[CustomerId]=@p0"),
                Array.Empty<AnalysisResult>());

            result.WarningsText.Should().Contain("残差谓词寻址 (Residual Predicate)");
        }

        [Fact]
        public void BuildWarnings_AppendsRuleResultsAndTracksHighestSeverity()
        {
            XElement relOp = new(Ns + "RelOp");
            var ruleResults = new[]
            {
                new AnalysisResult
                {
                    Severity = "Warning",
                    Title = "Row Estimate",
                    Message = "Rows differ"
                },
                new AnalysisResult
                {
                    Severity = "Critical",
                    Title = "Memory Spill",
                    Message = "Spill detected"
                }
            };

            PlanGraphWarningResult result = _service.BuildWarnings(
                relOp,
                Ns,
                CreateContext(),
                ruleResults);

            result.WarningsText.Should().Contain("[Warning] Row Estimate: Rows differ");
            result.WarningsText.Should().Contain("[Critical] Memory Spill: Spill detected");
            result.HighestSeverity.Should().Be("Critical");
        }

        private static PlanGraphWarningContext CreateContext(
            string nodeId = "2",
            string physicalOp = "Index Scan",
            string residualPredicate = "",
            string seekPredicate = "",
            bool hasActual = false,
            bool hasActualRead = false,
            double actualRows = 0,
            double actualRowsRead = 0,
            bool isThreadDataSkewed = false)
        {
            return new PlanGraphWarningContext(
                nodeId,
                physicalOp,
                residualPredicate,
                seekPredicate,
                hasActual,
                hasActualRead,
                actualRows,
                actualRowsRead,
                isThreadDataSkewed,
                10.0,
                1000);
        }
    }
}
