using System.IO;
using FluentAssertions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Refactoring;

namespace SqlXmlAnalyzer.Tests.Refactoring;

public sealed class IndexDdlIdentityTests
{
    [Theory]
    [InlineData("[Order.Detail]", "Order.Detail")]
    [InlineData("[Order]]Detail]", "Order]Detail")]
    [InlineData("[Order]]]]Detail]", "Order]]Detail")]
    [InlineData("[訂單 明細]", "訂單 明細")]
    public void Generate_QuotesEachNameAsOneIdentifier(string token, string name)
    {
        var suggestion = Suggestion();
        suggestion.Table = token;
        suggestion.KeyColumns[0].Name = token;
        var create = Parse<CreateIndexStatement>(suggestion.CreateIndexStatement);
        create.OnName.Identifiers.Select(i => i.Value).Should().Equal("db.one", "sales", name);
        create.Columns.Single().Column.MultiPartIdentifier.Identifiers.Select(i => i.Value).Should().Equal(name);
        var drop = Parse<DropIndexStatement>(suggestion.RollbackStatement);
        var clause = drop.DropIndexClauses.Single().Should().BeOfType<DropIndexClause>().Subject;
        clause.Index.Value.Should().Be(create.Name.Value);
        clause.Object.Identifiers.Select(i => i.Value).Should().Equal("db.one", "sales", name);
    }

    [Fact]
    public void Names_DistinguishFourthKeyIncludeColumnsAndPunctuation()
    {
        var first = Suggestion();
        first.KeyColumns = Enumerable.Range(1, 4).Select(i => new IndexColumn { Name = "K" + i, Usage = "EQUALITY" }).ToList();
        var name = Parse<CreateIndexStatement>(first.CreateIndexStatement).Name.Value;
        first.KeyColumns[3].Name = "K5";
        Parse<CreateIndexStatement>(first.CreateIndexStatement).Name.Value.Should().NotBe(name);
        name = Parse<CreateIndexStatement>(first.CreateIndexStatement).Name.Value;
        first.IncludeColumns.Add(new() { Name = "I", Usage = "INCLUDE" });
        Parse<CreateIndexStatement>(first.CreateIndexStatement).Name.Value.Should().NotBe(name);
        first.Table = "A.B";
        name = Parse<CreateIndexStatement>(first.CreateIndexStatement).Name.Value;
        first.Table = "AB";
        Parse<CreateIndexStatement>(first.CreateIndexStatement).Name.Value.Should().NotBe(name);
    }

    [Theory]
    [InlineData("PAGE); DROP TABLE [victim]; --")]
    [InlineData("COLUMNSTORE")]
    public void Generate_RejectsUnsupportedCompressionInsteadOfInterpolatingSql(string compression)
    {
        var action = () => IndexDdlCompiler.Generate(Suggestion(), new() { DataCompression = compression });
        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Generate_UsesAuthoritativeObjectPartsAndRejectsConflictingLegacyFields()
    {
        var suggestion = Suggestion();
        suggestion.ObjectIdentity = new(null, "other", "audit", "T");
        var action = () => suggestion.CreateIndexStatement;
        action.Should().Throw<InvalidDataException>();
    }

    internal static MissingIndexSuggestion Suggestion() => new()
    {
        Database = "[db.one]", Schema = "[sales]", Table = "[T]",
        KeyColumns = [new() { Name = "[K]", Usage = "EQUALITY" }]
    };

    [Theory]
    [InlineData(-1)]
    [InlineData(65)]
    public void Generate_RejectsInvalidMaxDop(int maxDop)
    {
        var action = () => IndexDdlCompiler.Generate(Suggestion(), new() { MaxDop = maxDop });
        action.Should().Throw<InvalidDataException>().WithMessage("INDEX_MAXDOP_INVALID*");
    }

    [Theory]
    [InlineData("[bad]name]")]
    [InlineData("[unfinished")]
    [InlineData("[]")]
    [InlineData("")]
    public void Generate_RejectsMalformedColumnTokens(string name)
    {
        var suggestion = Suggestion();
        suggestion.KeyColumns[0].Name = name;
        var action = () => suggestion.CreateIndexStatement;
        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Generate_RejectsDuplicateRolesOverlengthAndUnresolvedSchema()
    {
        var suggestion = Suggestion();
        suggestion.IncludeColumns.Add(new() { Name = "K", Usage = "INCLUDE" });
        Action action = () => IndexDdlCompiler.Compile(suggestion, new());
        action.Should().Throw<InvalidDataException>().WithMessage("INDEX_DUPLICATE_COLUMN*");
        suggestion.IncludeColumns.Clear();
        suggestion.KeyColumns[0].Name = new string('a', 129);
        action.Should().Throw<InvalidDataException>().WithMessage("INDEX_IDENTIFIER_INVALID*");
        suggestion.KeyColumns[0].Name = "K";
        suggestion.Schema = "";
        action.Should().Throw<InvalidDataException>().WithMessage("INDEX_TARGET_UNRESOLVED*");
        suggestion.KeyColumns.Clear();
        suggestion.CreateIndexStatement.Should().BeEmpty();
        suggestion.RollbackStatement.Should().BeEmpty();
    }

    [Fact]
    public void CompiledDefinition_IsStableAcrossOptionsAndUnaffectedByLaterEdits()
    {
        var suggestion = Suggestion();
        suggestion.Table = new string('訂', 128);
        var first = IndexDdlCompiler.Compile(suggestion, new());
        var second = IndexDdlCompiler.Compile(suggestion, new() { Online = false, MaxDop = 2, DataCompression = "ROW" });
        first.IndexName.Should().Be(second.IndexName);
        first.IndexName.Length.Should().BeLessThanOrEqualTo(128);
        first.Rollback.Should().Contain(IndexDdlCompiler.QuoteIdentifier(first.IndexName));
        suggestion.KeyColumns[0].Name = "Different";
        IndexDdlCompiler.Compile(suggestion, new()).IndexName.Should().NotBe(first.IndexName);
        first.Create.Should().Contain("([K])");
    }

    [Fact]
    public void ServerEvidence_IsEnforcedWithoutInventingFourPartIndexTarget()
    {
        var suggestion = Suggestion();
        suggestion.Server = "[server'one]";
        var compiled = IndexDdlCompiler.Compile(suggestion, new());
        compiled.Create.Should().Contain("SERVERPROPERTY").And.Contain("N'server''one'").And.Contain("THROW 50001");
        Parse<CreateIndexStatement>(compiled.Create).OnName.Identifiers.Should().HaveCount(3);
        compiled.Rollback.Should().Contain("SERVERPROPERTY").And.Contain("N'server''one'");
    }

    [Fact]
    public void Quoting_RoundTripsDeterministicAdversarialIdentifierSamples()
    {
        var random = new Random(15);
        const string alphabet = "aB. ]['_訂單;/*-\r\n";
        for (int sample = 0; sample < 100; sample++)
        {
            string name = "X" + new string(Enumerable.Range(0, random.Next(1, 70)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            var suggestion = Suggestion();
            suggestion.Table = IndexDdlCompiler.QuoteIdentifier(name);
            suggestion.KeyColumns[0].Name = IndexDdlCompiler.QuoteIdentifier(name);
            var create = Parse<CreateIndexStatement>(suggestion.CreateIndexStatement);
            create.OnName.BaseIdentifier.Value.Should().Be(name);
            create.Columns.Single().Column.MultiPartIdentifier.Identifiers.Single().Value.Should().Be(name);
        }
    }

    [Fact]
    public void DeploymentBundle_IdentifierTextCannotEscapeCommentsOrActivateRollback()
    {
        var suggestion = Suggestion();
        suggestion.Table = IndexDdlCompiler.QuoteIdentifier("T*/; DROP TABLE Victim; /*\r\n--X");
        suggestion.KeyColumns[0].Name = IndexDdlCompiler.QuoteIdentifier("K*/\nEXEC bad;");
        var bundle = new SqlXmlAnalyzer.Core.Services.MissingIndexDeploymentScriptService().BuildDeploymentBundle(suggestion);
        var fragment = new TSql160Parser(true).Parse(new StringReader(bundle), out var errors);
        errors.Should().BeEmpty();
        var visitor = new IndexVisitor();
        fragment.Accept(visitor);
        visitor.Creates.Should().Be(1);
        visitor.Drops.Should().Be(0);
    }

    private sealed class IndexVisitor : TSqlFragmentVisitor
    {
        public int Creates { get; private set; }
        public int Drops { get; private set; }
        public override void ExplicitVisit(CreateIndexStatement node) { Creates++; }
        public override void ExplicitVisit(DropIndexStatement node) { Drops++; }
    }

    [Fact]
    public void IdentifierApi_SeparatesSingleNamesFromPartsAndRejectsMissingMiddlePart()
    {
        IndexDdlCompiler.QuoteIdentifier("dbo.Order.Detail").Should().Be("[dbo.Order.Detail]");
        IndexDdlCompiler.QualifyName(null, "dbo", "Order.Detail").Should().Be("[dbo].[Order.Detail]");
        Action action = () => IndexDdlCompiler.QualifyName("db", null, "T");
        action.Should().Throw<InvalidDataException>();
    }

    internal static T Parse<T>(string sql) where T : TSqlStatement
    {
        var fragment = new TSql160Parser(true).Parse(new StringReader(sql), out var errors);
        errors.Should().BeEmpty();
        return ((TSqlScript)fragment).Batches.SelectMany(b => b.Statements).OfType<T>().Should().ContainSingle().Subject;
    }
}
