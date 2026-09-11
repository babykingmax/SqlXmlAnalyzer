using System.IO;
using FluentAssertions;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class StatisticsCommandBuilderTests
{
    [Fact]
    public void BracketedShowplanNames_AreDecodedAndQuotedExactlyOnce()
    {
        var statistics = Names("[Db]", "[dbo]", "[T]", "[IX_T]");
        StatisticsCommandBuilder.BuildUpdateStatistics(statistics)
            .Should().Be("UPDATE STATISTICS [Db].[dbo].[T] ([IX_T]) WITH FULLSCAN;");
        StatisticsCommandBuilder.BuildShowStatistics(statistics)
            .Should().Be("DBCC SHOW_STATISTICS (N'[Db].[dbo].[T]', N'IX_T') WITH HISTOGRAM;");
    }

    [Theory]
    [InlineData("[s]]x']", "s]x'")]
    [InlineData("\"s\"\"x'\"", "s\"x'")]
    [InlineData("原始统计]名称'", "原始统计]名称'")]
    [InlineData("[s]]'); SELECT 123 AS injected;--]", "s]'); SELECT 123 AS injected;--")]
    public void NamesWithDelimiters_StayWithinOneParsedCommand(string encoded, string decoded)
    {
        var statistics = Names("[Db']", "[dbo]]x]", "[T'; SELECT 999;--]", encoded);
        string show = StatisticsCommandBuilder.BuildShowStatistics(statistics);
        var dbcc = ParseSingle(show).Should().BeOfType<DbccStatement>().Subject;
        var strings = new Strings();
        dbcc.Accept(strings);
        strings.Values.Should().Equal("[Db'].[dbo]]x].[T'; SELECT 999;--]", decoded);
        ParseSingle(StatisticsCommandBuilder.BuildUpdateStatistics(statistics)).Should().BeOfType<UpdateStatisticsStatement>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("[]")]
    [InlineData("[broken")]
    [InlineData("[a]b]")]
    [InlineData("[s]'); SELECT 123 AS injected;--")]
    [InlineData("a\0b")]
    public void MissingOrMalformedIdentity_ProducesNoSql(string invalid)
    {
        foreach (int field in Enumerable.Range(0, 4))
        {
            string[] names = ["Db", "dbo", "T", "IX_T"];
            names[field] = invalid;
            var statistics = Names(names[0], names[1], names[2], names[3]);
            Action show = () => StatisticsCommandBuilder.BuildShowStatistics(statistics);
            Action update = () => StatisticsCommandBuilder.BuildUpdateStatistics(statistics);
            show.Should().Throw<ArgumentException>();
            update.Should().Throw<ArgumentException>();
        }
    }

    [Fact]
    public void IdentifierLength_IsCheckedAfterDecoding()
    {
        ParseSingle(StatisticsCommandBuilder.BuildShowStatistics(Names("Db", "dbo", "T", "[" + new string(']', 256) + "]")))
            .Should().BeOfType<DbccStatement>();
        Action build = () => StatisticsCommandBuilder.BuildUpdateStatistics(Names("Db", "dbo", "T", new string('x', 129)));
        build.Should().Throw<ArgumentException>();
    }

    private static StatisticsInfo Names(string database, string schema, string table, string statistics) =>
        new() { Database = database, Schema = schema, Table = table, Statistics = statistics };

    private static TSqlStatement ParseSingle(string sql)
    {
        var fragment = new TSql160Parser(true).Parse(new StringReader(sql), out var errors);
        errors.Should().BeEmpty();
        return ((TSqlScript)fragment).Batches.SelectMany(batch => batch.Statements).Should().ContainSingle().Subject;
    }

    private sealed class Strings : TSqlFragmentVisitor
    {
        internal List<string> Values { get; } = [];
        public override void ExplicitVisit(StringLiteral node) => Values.Add(node.Value);
    }
}
