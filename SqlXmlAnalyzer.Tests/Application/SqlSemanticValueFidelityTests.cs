using System.Text.Json;
using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Application.Services;

namespace SqlXmlAnalyzer.Tests.Application;

public sealed class SqlSemanticValueFidelityTests
{
    [Theory]
    [InlineData(0xD800, 0xD801)]
    [InlineData(0xDC00, 0xDC01)]
    [InlineData(0xD800, 0xFFFD)]
    [InlineData(0xDC00, 0xFFFD)]
    public void EncodeRow_DistinctUtf16CodeUnitsRemainDistinct(int first, int second)
    {
        string left = "before" + (char)first + "after";
        string right = "before" + (char)second + "after";
        SqlSemanticComparison.EncodeRow([left]).Should().NotBe(SqlSemanticComparison.EncodeRow([right]));
    }

    [Theory]
    [InlineData(new[] { 0xD800 })]
    [InlineData(new[] { 0xDC00 })]
    [InlineData(new[] { 0x61, 0xD800, 0x62, 0xDC00 })]
    [InlineData(new[] { 0xD800, 0xD801, 0xDC00 })]
    public void EncodeRow_UnpairedSurrogatesHaveLosslessPortableRepresentation(int[] codeUnits)
    {
        string value = new(codeUnits.Select(c => (char)c).ToArray());
        using var json = JsonDocument.Parse(SqlSemanticComparison.EncodeRow([value]));
        var cell = json.RootElement[0];
        cell[0].GetString().Should().Be("System.String.UTF16LE");
        var bytes = Convert.FromBase64String(cell[1].GetString()!);
        var actual = new string(Enumerable.Range(0, bytes.Length / 2)
            .Select(i => (char)(bytes[2 * i] | bytes[2 * i + 1] << 8)).ToArray());
        actual.Should().Be(value);
    }

    [Theory]
    [InlineData("sql_variant")]
    [InlineData("SQL_VARIANT")]
    public void ColumnPolicy_VariantCannotBecomeEligibleEvidence(string sqlTypeName)
    {
        Action validate = () => SqlSemanticValueReader.EnsureComparableType(sqlTypeName);
        validate.Should().Throw<InvalidDataException>().WithMessage("UnsupportedSemanticType:*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("普通文字\0\uFFFD")]
    [InlineData("\uD83D\uDE00")]
    public void EncodeRow_ValidUnicodeKeepsReadableExistingRepresentation(string value)
    {
        using var json = JsonDocument.Parse(SqlSemanticComparison.EncodeRow([value]));
        json.RootElement[0][0].GetString().Should().Be("System.String");
        json.RootElement[0][1].GetString().Should().Be(value);
    }
}
