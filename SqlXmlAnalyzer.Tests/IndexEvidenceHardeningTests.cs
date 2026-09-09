using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Scoring;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests;

public sealed class IndexEvidenceHardeningTests
{
    private static readonly XNamespace Ns = InputRecognitionService.ShowPlanNamespace;

    [Fact]
    public void Sandbox_ParametersAndInternalValuesCannotBecomeIndexColumns()
    {
        var doc = Plan();
        var scan = doc.Descendants(Ns + "IndexScan").Single();
        scan.Add(new XElement(Ns + "DefinedValues", new XElement(Ns + "ColumnReference", new XAttribute("Column", "Bmk1000")),
            new XElement(Ns + "ColumnReference", new XAttribute("Column", "Expr1000"))));
        var suggestion = Bind(doc);
        var vm = new IndexSandboxViewModel(suggestion, doc);
        vm.AvailableColumns.Should().Equal("[V]");
        string before = vm.CreateIndexStatement;
        foreach (string name in new[] { "[@p]", "[Bmk1000]", "[Expr1000]" })
        {
            vm.AddIncludeColumnCommand.Execute(name);
            vm.AddKeyColumnCommand.Execute(name);
        }
        vm.CreateIndexStatement.Should().Be(before);
        vm.AddIncludeColumnCommand.Execute("[V]");
        vm.CreateIndexStatement.Should().Contain("INCLUDE ([V])");
    }

    [Theory]
    [InlineData("@p")]
    [InlineData("Expr1000")]
    [InlineData("Bmk1000")]
    [InlineData("R]ange")]
    [InlineData("[literal]")]
    [InlineData("[unclosed")]
    [InlineData("\"literal\"")]
    public void Sandbox_RealColumnsAreIdentifiedByOwnershipNotNamePrefixes(string name)
    {
        var doc = Plan();
        doc.Descendants(Ns + "IndexScan").Single().Add(Column(name));
        var vm = new IndexSandboxViewModel(Bind(doc), doc);
        string quoted = SqlObjectIdentity.Quote(name);
        vm.AvailableColumns.Should().Contain(quoted);
        vm.AddIncludeColumnCommand.Execute(quoted);
        vm.CreateIndexStatement.Should().Contain($"INCLUDE ({quoted})");
    }

    [Theory]
    [InlineData("Database", null)]
    [InlineData("Schema", null)]
    [InlineData("Table", null)]
    [InlineData("Database", "[OtherDb]")]
    [InlineData("Schema", "[audit]")]
    [InlineData("Table", "[OtherTable]")]
    [InlineData("Server", "[OtherServer]")]
    [InlineData("Column", "")]
    [InlineData("ParameterDataType", "int")]
    [InlineData("ParameterCompiledValue", "(1)")]
    public void Sandbox_UnprovenOrConflictingColumnIdentityIsExcluded(string attribute, string? value)
    {
        var doc = Plan();
        var column = Column("Unproven");
        column.SetAttributeValue(attribute, value);
        doc.Descendants(Ns + "IndexScan").Single().Add(column);
        var suggestion = Bind(doc);
        IndexTargetResolver.FindColumns(suggestion, doc, Ns).Should().NotContain(column);
        new IndexSandboxViewModel(suggestion, doc).AvailableColumns.Should().Equal("[V]");
    }

    [Fact]
    public void Coverage_ParametersAndInternalOutputDoNotDiluteEntityCoverage()
    {
        var doc = Plan();
        doc.Descendants(Ns + "OutputList").Single().Add(new XElement(Ns + "ColumnReference", new XAttribute("Column", "Bmk1000")));
        var suggestion = Bind(doc);
        IndexScoringCalculator.CalculateScore(suggestion, doc, Ns);
        suggestion.Score.Should().Be(85);
        new IndexSandboxViewModel(suggestion, doc).IsCoveredIndex.Should().BeTrue();
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("0")]
    [InlineData("false")]
    public void Score_SortWithCompleteIdentityContributesWithoutAnObjectElement(string ascending)
    {
        var doc = Plan();
        doc.Descendants(Ns + "OrderByColumn").Single().SetAttributeValue("Ascending", ascending);
        var suggestion = Bind(doc);
        IndexScoringCalculator.CalculateScore(suggestion, doc, Ns);
        suggestion.Score.Should().Be(85);
    }

    [Theory]
    [InlineData("Database", "[OtherDb]")]
    [InlineData("Schema", "[audit]")]
    [InlineData("Table", "[OtherTable]")]
    [InlineData("Database", null)]
    [InlineData("Server", "[OtherServer]")]
    public void Score_SortDoesNotBorrowEvidenceFromUnprovenTargets(string attribute, string? value)
    {
        var doc = Plan();
        doc.Descendants(Ns + "OrderByColumn").Single().Element(Ns + "ColumnReference")!.SetAttributeValue(attribute, value);
        var suggestion = Bind(doc);
        IndexScoringCalculator.CalculateScore(suggestion, doc, Ns);
        suggestion.Score.Should().Be(70);
    }

    [Fact]
    public void Score_OneMatchingColumnCannotValidateAnEntireMixedTargetSort()
    {
        var doc = Plan();
        var foreign = Column("V");
        foreign.SetAttributeValue("Schema", "[audit]");
        doc.Descendants(Ns + "OrderBy").Single().Add(new XElement(Ns + "OrderByColumn", new XAttribute("Ascending", "1"), foreign));
        var suggestion = Bind(doc);
        IndexScoringCalculator.CalculateScore(suggestion, doc, Ns);
        suggestion.Score.Should().Be(70);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    public void Score_MissingOrInvalidDirectionDoesNotEarnSortPoints(string? ascending)
    {
        var doc = Plan();
        doc.Descendants(Ns + "OrderByColumn").Single().SetAttributeValue("Ascending", ascending);
        var suggestion = Bind(doc);
        IndexScoringCalculator.CalculateScore(suggestion, doc, Ns);
        suggestion.Score.Should().Be(70);
    }

    [Fact]
    public void Score_MixedDirectionsCannotBeSatisfiedByAllAscendingIndexKeys()
    {
        var doc = Plan();
        doc.Descendants(Ns + "OrderBy").Single().Add(new XElement(Ns + "OrderByColumn", new XAttribute("Ascending", "0"), Column("V")));
        var suggestion = Bind(doc);
        suggestion.KeyColumns.Add(new IndexColumn { Name = "[V]", Usage = "INEQUALITY" });
        IndexScoringCalculator.CalculateScore(suggestion, doc, Ns);
        suggestion.Score.Should().Be(70);
    }

    [Theory]
    [InlineData("statement")]
    [InlineData("queryPlan")]
    [InlineData("internal")]
    [InlineData("foreign")]
    public void Score_SortCannotCrossCapturedScopeOrOpaqueBoundaries(string placement)
    {
        var doc = Plan();
        var rootOp = doc.Descendants(Ns + "RelOp").First();
        var sort = rootOp.Element(Ns + "Sort")!;
        var copy = new XElement(rootOp);
        var scan = sort.Element(Ns + "RelOp")!;
        scan.Remove();
        rootOp.ReplaceWith(scan);
        var statement = doc.Descendants(Ns + "StmtSimple").Single();
        if (placement == "statement") statement.Parent!.Add(new XElement(Ns + "StmtSimple", new XElement(Ns + "QueryPlan", copy)));
        else if (placement == "queryPlan") statement.Add(new XElement(Ns + "QueryPlan", copy));
        else if (placement == "internal") scan.Add(new XElement(Ns + "InternalInfo", copy));
        else scan.Add(new XElement(XName.Get("Other", "urn:foreign"), copy));
        var suggestion = Bind(doc);
        IndexScoringCalculator.CalculateScore(suggestion, doc, Ns);
        suggestion.Score.Should().Be(70);
    }

    [Fact]
    public void Score_MultipleSortsAreIndependentAndDoNotStackOrConcatenate()
    {
        var doc = Plan();
        var root = doc.Descendants(Ns + "RelOp").First();
        var extra = new XElement(Ns + "RelOp", new XAttribute("NodeId", "2"), new XAttribute("PhysicalOp", "Sort"),
            new XElement(Ns + "Sort", new XElement(Ns + "OrderBy", new XElement(Ns + "OrderByColumn", new XAttribute("Ascending", "1"), Column("V")))));
        root.ReplaceWith(extra);
        extra.Element(Ns + "Sort")!.Add(root);
        var suggestion = Bind(doc);
        IndexScoringCalculator.CalculateScore(suggestion, doc, Ns);
        suggestion.Score.Should().Be(85);
    }

    [Theory]
    [InlineData("R]ange")]
    [InlineData("Order.Detail")]
    [InlineData("[literal]")]
    public void Score_SortAndKeyUseDecodedSingleIdentifierNames(string name)
    {
        var doc = Plan();
        string quoted = SqlObjectIdentity.Quote(name);
        doc.Descendants(Ns + "ColumnReference").Where(c => (string?)c.Attribute("Column") == "R")
            .ToList().ForEach(c => c.SetAttributeValue("Column", name));
        var suggestion = Bind(doc);
        suggestion.KeyColumns[1].Name = quoted;
        IndexScoringCalculator.CalculateScore(suggestion, doc, Ns);
        suggestion.Score.Should().Be(85);
    }

    [Theory]
    [InlineData("wrongOperator")]
    [InlineData("caseDifference")]
    [InlineData("foreignColumn")]
    [InlineData("internalColumn")]
    [InlineData("extraColumn")]
    public void Score_InvalidOrAmbiguousSortPayloadDoesNotEarnPoints(string kind)
    {
        var doc = Plan();
        var entry = doc.Descendants(Ns + "OrderByColumn").Single();
        var column = entry.Element(Ns + "ColumnReference")!;
        if (kind == "wrongOperator") doc.Descendants(Ns + "RelOp").First().SetAttributeValue("PhysicalOp", "Index Scan");
        else if (kind == "caseDifference") column.SetAttributeValue("Column", "r");
        else if (kind == "extraColumn") entry.Add(Column("R"));
        else
        {
            column.Remove();
            entry.Add(new XElement(kind == "foreignColumn" ? XName.Get("Extension", "urn:foreign") : Ns + "InternalInfo", column));
        }
        var suggestion = Bind(doc);
        IndexScoringCalculator.CalculateScore(suggestion, doc, Ns);
        suggestion.Score.Should().Be(70);
    }

    [Fact]
    public void CapturedSqlServerPlan_PreservesLiteralNamesAndScoresOnlyProvenSortEvidence()
    {
        var doc = SafeXmlHelper.ParseSafe(EmbeddedResourceHelper.GetResourceContent("imp15_review_actual.sqlplan"));
        var suggestion = Bind(doc);
        suggestion.KeyColumns.RemoveAt(0); // R-leading candidate isolates the Sort contribution.
        var vm = new IndexSandboxViewModel(suggestion, doc);
        string[] expected = ["[@real]", "[Bmk1000]", "[Expr1000]", "[K]", "[R]]ange]", "[[literal]]]", "[V]"];
        vm.AvailableColumns.Should().BeEquivalentTo(expected).And.NotContain("[@p]");
        var order = IndexTargetResolver.FindSortOrders(suggestion, doc, Ns).Should().ContainSingle().Subject;
        order.Should().ContainSingle().Which.Should().Be(new IndexTargetResolver.SortColumn("R", true));
        foreach (string name in expected) vm.AddIncludeColumnCommand.Execute(name);
        vm.CurrentScore.Should().Be(55); // 40 coverage + 15 Sort, independent of predicate heuristics.
        vm.IsCoveredIndex.Should().BeTrue();
        vm.CreateIndexStatement.Should().Contain("[[literal]]]").And.NotContain("[@p]");
    }

    private static MissingIndexSuggestion Bind(XDocument doc)
    {
        var model = PlanIdentityAdapter.GetDocument(doc)!;
        var op = model.Operators.First(o => o.Objects.Count == 1);
        return new MissingIndexSuggestion
        {
            ObjectIdentity = op.Objects.Single().Identity, Location = op.Location with { Operator = null },
            KeyColumns = [new() { Name = "[K]", Usage = "EQUALITY" }, new() { Name = "[R]", Usage = "INEQUALITY" }]
        };
    }

    private static XElement Column(string name) => new(Ns + "ColumnReference", new XAttribute("Database", "[TestDb]"),
        new XAttribute("Schema", "[dbo]"), new XAttribute("Table", "[T]"), new XAttribute("Column", name));

    private static XDocument Plan() => SafeXmlHelper.ParseSafe("""
        <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan"><BatchSequence><Batch><Statements>
          <StmtSimple StatementText="SELECT K,R FROM TestDb.dbo.T WHERE K=@p ORDER BY R"><QueryPlan>
            <RelOp NodeId="0" PhysicalOp="Sort"><Sort Distinct="false"><OrderBy><OrderByColumn Ascending="1">
              <ColumnReference Database="[TestDb]" Schema="[dbo]" Table="[T]" Column="R"/>
            </OrderByColumn></OrderBy><RelOp NodeId="1" PhysicalOp="Index Scan">
              <OutputList><ColumnReference Database="[TestDb]" Schema="[dbo]" Table="[T]" Column="K"/>
                <ColumnReference Database="[TestDb]" Schema="[dbo]" Table="[T]" Column="R"/></OutputList>
              <IndexScan><Object Database="[TestDb]" Schema="[dbo]" Table="[T]"/>
                <DefinedValues><DefinedValue><ColumnReference Database="[TestDb]" Schema="[dbo]" Table="[T]" Column="V"/></DefinedValue></DefinedValues>
                <Predicate><ScalarOperator ScalarString="[K] = [@p]"><Compare CompareOp="EQ">
                  <ScalarOperator><Identifier><ColumnReference Database="[TestDb]" Schema="[dbo]" Table="[T]" Column="K"/></Identifier></ScalarOperator>
                  <ScalarOperator><Identifier><ColumnReference Column="@p"/></Identifier></ScalarOperator>
                </Compare></ScalarOperator></Predicate>
              </IndexScan>
            </RelOp></Sort></RelOp>
          </QueryPlan></StmtSimple>
        </Statements></Batch></BatchSequence></ShowPlanXML>
        """);
}
