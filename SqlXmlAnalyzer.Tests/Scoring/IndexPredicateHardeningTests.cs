using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Scoring;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Tests.Simulation;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests.Scoring;

public sealed class IndexPredicateHardeningTests
{
    private static readonly XNamespace Ns = CostImpactModelTests.Ns;

    [Theory]
    [InlineData("[K] = (1)")]
    [InlineData("([K]) = (((1)))")]
    [InlineData("(-1) = [K]")]
    [InlineData("[K] = -1")]
    [InlineData("[K] = +1.5")]
    [InlineData("[K] = (-1e3)")]
    [InlineData("[K] = (@p)")]
    [InlineData("([K]) = (N'x')")]
    [InlineData("[K] = (0x01)")]
    public void Text_ParenthesesAndSignedConstantsPreserveEqualityEvidence(string predicate)
    {
        var result = Score(Text(predicate));
        result.EqualityPoints.Should().Be(30);
        result.Score.Should().Be(70);
    }

    [Theory]
    [InlineData("([K]) > (-1)")]
    [InlineData("(2) <= ([K])")]
    [InlineData("[K] <> (3)")]
    [InlineData("([K]) IS NOT NULL")]
    public void Text_ParenthesesPreserveRangeEvidence(string predicate) => Score(Text(predicate)).InequalityPoints.Should().Be(15);

    [Theory]
    [InlineData("ABS([K]) = (1)")]
    [InlineData("-[K] = (1)")]
    [InlineData("[K] = (SELECT 1)")]
    [InlineData("[K] = ([V])")]
    [InlineData("[K] = (NULL)")]
    [InlineData("[K] = (1 + 2)")]
    [InlineData("CASE WHEN [K]=(1) THEN 2 ELSE 3 END = 3")]
    public void Text_UnwrappingDoesNotInventDirectColumnEvidence(string predicate)
    {
        var result = Score(Text(predicate));
        result.EqualityPoints.Should().Be(0);
        result.InequalityPoints.Should().Be(0);
    }

    [Theory]
    [InlineData("IS NULL", false, false, 30, 0)]
    [InlineData("IS NOT NULL", false, false, 0, 15)]
    [InlineData("IS", true, false, 30, 0)]
    [InlineData("IS NOT", true, false, 0, 15)]
    [InlineData("IS", true, true, 30, 0)]
    [InlineData("IS NOT", true, true, 0, 15)]
    public void Structured_NullComparisonsMatchTheirBooleanMeaning(string kind, bool binary, bool reverse, int equality, int range)
    {
        string args = !binary ? Column() : reverse ? Constant("NULL") + Column() : Column() + Constant("NULL");
        var scalar = Scalar($"<Compare CompareOp=\"{kind}\">{args}</Compare>");
        ValidateScalar(scalar);
        var result = Score(scalar);
        result.EqualityPoints.Should().Be(equality);
        result.InequalityPoints.Should().Be(range);
        result.EvidenceSource.Should().Be("CAPTURED_PREDICATES");
    }

    [Theory]
    [InlineData("IS", "(1)")]
    [InlineData("IS NOT", "(1)")]
    [InlineData("EQ", "NULL")]
    [InlineData("NE", "NULL")]
    public void Structured_NullSafeAndOrdinaryComparisonsAreNotInterchanged(string kind, string constant)
    {
        var result = Score(Scalar($"<Compare CompareOp=\"{kind}\">{Column()}{Constant(constant)}</Compare>"));
        result.EqualityPoints.Should().Be(0);
        result.InequalityPoints.Should().Be(0);
    }

    [Theory]
    [InlineData("Condition")]
    [InlineData("Then")]
    [InlineData("Else")]
    public void Structured_CaseBranchComparisonsNeverBecomeFilterConjuncts(string branch)
    {
        string condition = branch == "Condition" ? Comparison("K") : Comparison("V");
        string then = branch == "Then" ? Comparison("K") : Constant("(2)");
        string otherwise = branch == "Else" ? Comparison("K") : Constant("(3)");
        var scalar = Scalar($"<Compare CompareOp=\"EQ\"><ScalarOperator><IF><Condition>{condition}</Condition><Then>{then}</Then><Else>{otherwise}</Else></IF></ScalarOperator>{Constant("(3)")}</Compare>");
        ValidateScalar(scalar);
        scalar.SetAttributeValue("ScalarString", "[K] = 1"); // Text must not override structured CASE.
        Score(scalar).EqualityPoints.Should().Be(0);
    }

    [Theory]
    [InlineData("OR")]
    [InlineData("NOT")]
    public void Structured_DisjunctionAndNegationStayExcluded(string operation)
    {
        var scalar = Scalar($"<Logical Operation=\"{operation}\">{Comparison("K")}</Logical>");
        ValidateScalar(scalar);
        Score(scalar).EqualityPoints.Should().Be(0);
    }

    [Fact]
    public void Structured_AndWalkKeepsIndependentConjunctsAndIgnoresCaseInternals()
    {
        var scalar = Scalar($"<Logical Operation=\"AND\">{Comparison("K")}<ScalarOperator><Logical Operation=\"OR\">{Comparison("V")}{Comparison("R")}</Logical></ScalarOperator></Logical>");
        ValidateScalar(scalar);
        Score(scalar).EqualityPoints.Should().Be(30);
        scalar.Elements().Single().ReplaceNodes(scalar.Elements().Single().Nodes().Reverse().ToArray());
        Score(scalar).EqualityPoints.Should().Be(30);
    }

    [Fact]
    public void Structured_UnknownExpressionCannotPromoteItsDescendantScalarText()
    {
        var scalar = Scalar("<IF><Condition><ScalarOperator ScalarString=\"[K]=1\"><Const ConstValue=\"(1)\"/></ScalarOperator></Condition><Then><ScalarOperator><Const ConstValue=\"(2)\"/></ScalarOperator></Then><Else><ScalarOperator><Const ConstValue=\"(3)\"/></ScalarOperator></Else></IF>");
        Score(scalar).EqualityPoints.Should().Be(0);
    }

    [Fact]
    public void Structured_ExtraPayloadCannotMasqueradeAsIdentifier()
    {
        var scalar = Scalar($"<Compare CompareOp=\"EQ\"><ScalarOperator><Identifier><ColumnReference Database=\"[db]\" Schema=\"[dbo]\" Table=\"[T]\" Column=\"K\"/></Identifier><Const ConstValue=\"(0)\"/></ScalarOperator>{Constant("(1)")}</Compare>");
        Score(scalar).EqualityPoints.Should().Be(0);
    }

    [Fact]
    public void Text_QuotedAtNameRequiresCapturedParameterEvidence()
    {
        var (doc, candidate) = Fixture(Text("[K]=([@p])"));
        IndexScoringCalculator.Evaluate(candidate, doc, Ns).EqualityPoints.Should().Be(0);
        doc.Descendants(Ns + "QueryPlan").First().Add(new XElement(Ns + "ParameterList",
            new XElement(Ns + "ColumnReference", new XAttribute("Column", "@p"), new XAttribute("ParameterDataType", "int"))));
        candidate = CostImpactModelTests.Candidates(doc)[0];
        IndexScoringCalculator.Evaluate(candidate, doc, Ns).EqualityPoints.Should().Be(30);
    }

    [Fact]
    public void Text_EntityNamedLikeParameterRemainsColumnToColumn()
    {
        var (doc, _) = Fixture(Text("[K]=[@real]"));
        doc.Descendants(Ns + "OutputList").First().Add(new XElement(Ns + "ColumnReference",
            new XAttribute("Database", "[db]"), new XAttribute("Schema", "[dbo]"), new XAttribute("Table", "[T]"), new XAttribute("Column", "@real")));
        var candidate = CostImpactModelTests.Candidates(doc)[0];
        IndexScoringCalculator.Evaluate(candidate, doc, Ns).EqualityPoints.Should().Be(0);
    }

    [Fact]
    public void Text_ParameterMetadataCannotBeBorrowedFromAnotherQueryPlan()
    {
        var (doc, _) = Fixture(Text("[K]=[@p]"));
        doc.Descendants(Ns + "QueryPlan").Last().Add(new XElement(Ns + "ParameterList",
            new XElement(Ns + "ColumnReference", new XAttribute("Column", "@p"), new XAttribute("ParameterDataType", "int"))));
        var candidate = CostImpactModelTests.Candidates(doc)[0];
        IndexScoringCalculator.Evaluate(candidate, doc, Ns).EqualityPoints.Should().Be(0);
    }

    [Theory]
    [InlineData("AND")]
    [InlineData("Compare")]
    public void Structured_ForeignSiblingInvalidatesTheWholeBooleanNode(string kind)
    {
        string body = kind == "AND" ? $"<Logical Operation=\"AND\">{Comparison("K")}<ScalarOperator xmlns=\"urn:foreign\"/></Logical>"
            : $"<Compare CompareOp=\"EQ\">{Column()}{Constant("(1)")}<ScalarOperator xmlns=\"urn:foreign\"/></Compare>";
        Score(Scalar(body)).EqualityPoints.Should().Be(0);
    }

    [Fact]
    public void Structured_InternalInfoCannotSupplyAdditionalPredicateEvidence()
    {
        var scalar = Scalar($"<Const ConstValue=\"(1)\"/><InternalInfo>{Comparison("K")}</InternalInfo>");
        Score(scalar).EqualityPoints.Should().Be(0);
    }

    [Fact]
    public void Structured_SeekRangeExpressionIsAValueRatherThanAFilterRoot()
    {
        var (doc, _) = Fixture(Scalar("<Const ConstValue=\"(1)\"/>"));
        var access = doc.Descendants(Ns + "TableScan").First();
        access.Add(SafeXmlHelper.ParseSafe($"<SeekPredicates xmlns=\"{Ns}\"><SeekPredicate><StartRange ScanType=\"GT\"><RangeColumns><ColumnReference Column=\"V\"/></RangeColumns><RangeExpressions>{Comparison("K")}</RangeExpressions></StartRange></SeekPredicate></SeekPredicates>").Root!);
        var candidate = CostImpactModelTests.Candidates(doc)[0];
        IndexScoringCalculator.Evaluate(candidate, doc, Ns).EqualityPoints.Should().Be(0);
    }

    [Theory]
    [InlineData("(([K]=(1))) AND NOT ([V]=(2))", 30)]
    [InlineData("(([K]=(1)) OR [V]=(2))", 0)]
    [InlineData("NOT (([K]=(1)))", 0)]
    [InlineData("([K]) IS NULL", 30)]
    public void Text_BooleanParenthesesRetainConjunctBoundaries(string predicate, int equality) => Score(Text(predicate)).EqualityPoints.Should().Be(equality);

    [Fact]
    public void ExistingFixtureAndSandbox_RestoreEqualityAndRangeWithoutChangingImpact()
    {
        var doc = SafeXmlHelper.ParseSafe(EmbeddedResourceHelper.GetResourceContent("imp15_index_targets.sqlplan"));
        var candidate = PlanDiagnosticAnalyzer.ExtractMissingIndexes(doc, Ns).First();
        candidate.Score.Should().Be(85);
        candidate.CapturedImpact.Should().Be(80);
        new IndexSandboxViewModel(candidate, doc).ScoreBreakdown.Should().Contain("85/100");
    }

    [Theory]
    [InlineData(0, 30, 0)]
    [InlineData(1, 30, 0)]
    [InlineData(2, 0, 15)]
    [InlineData(3, 0, 0)]
    [InlineData(4, 30, 0)]
    [InlineData(5, 30, 0)]
    [InlineData(6, 0, 0)]
    [InlineData(7, 0, 0)]
    public void NativeSqlServerPlan_PreservesDirectPredicatesAndUnsupportedBoundaries(int index, int equality, int range)
    {
        var doc = SafeXmlHelper.ParseSafe(EmbeddedResourceHelper.GetResourceContent("imp16_review_predicates.sqlplan"));
        var candidates = CostImpactModelTests.Candidates(doc);
        candidates.Should().HaveCount(8);
        var result = IndexScoringCalculator.Evaluate(candidates[index], doc, Ns);
        result.EqualityPoints.Should().Be(equality);
        result.InequalityPoints.Should().Be(range);
        // The final capture contains an auto-parameterized ConstExpr/Convert value.
        // It must remain unknown until that expression has an explicit supported model.
        if (index == 7) result.EvidenceSource.Should().Be("MISSING_PREDICATE_EVIDENCE");
    }

    private static IndexScoreResult Score(XElement scalar)
    {
        var (doc, candidate) = Fixture(scalar);
        return IndexScoringCalculator.Evaluate(candidate, doc, Ns);
    }
    private static (XDocument, MissingIndexSuggestion) Fixture(XElement scalar)
    {
        var doc = CostImpactModelTests.Plan();
        doc.Descendants(Ns + "Predicate").First().ReplaceNodes(new XElement(scalar));
        return (doc, CostImpactModelTests.Candidates(doc)[0]);
    }
    private static XElement Text(string text) => new(Ns + "ScalarOperator", new XAttribute("ScalarString", text));
    private static XElement Scalar(string body) => SafeXmlHelper.ParseSafe($"<ScalarOperator xmlns=\"{Ns}\">{body}</ScalarOperator>").Root!;
    private static string Column(string name = "K") => $"<ScalarOperator><Identifier><ColumnReference Database=\"[db]\" Schema=\"[dbo]\" Table=\"[T]\" Column=\"{name}\"/></Identifier></ScalarOperator>";
    private static string Constant(string value) => $"<ScalarOperator><Const ConstValue=\"{value}\"/></ScalarOperator>";
    private static string Comparison(string name) => $"<ScalarOperator><Compare CompareOp=\"EQ\">{Column(name)}{Constant("(1)")}</Compare></ScalarOperator>";

    private static void ValidateScalar(XElement scalar)
    {
        var schemaDoc = SafeXmlHelper.ParseSafe(EmbeddedResourceHelper.GetResourceContent("showplanxml-sql2019.xsd"));
        using var schemaReader = schemaDoc.CreateReader();
        var schema = XmlSchema.Read(schemaReader, null)!;
        schema.Items.Add(new XmlSchemaElement { Name = "ReviewScalar", SchemaTypeName = new XmlQualifiedName("ScalarType", Ns.NamespaceName) });
        var schemas = new XmlSchemaSet { XmlResolver = null };
        schemas.Add(schema);
        var document = new XDocument(new XElement(scalar) { Name = Ns + "ReviewScalar" });
        document.Validate(schemas, (_, args) => throw args.Exception);
    }
}
