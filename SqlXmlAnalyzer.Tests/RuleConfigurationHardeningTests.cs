using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Configuration;

namespace SqlXmlAnalyzer.Tests;

public sealed class RuleConfigurationHardeningTests
{
    [Fact]
    public void Parse_LongAncestorAndWideArray_UsesBoundedAllocations()
    {
        string json = "{\"" + new string('x', 8192) + "\":[" + string.Join(",", Enumerable.Repeat("0", 5000)) + "],\"Rules\":[]}";
        _ = RuleConfigurationDocument.Defaults;
        _ = RuleConfigurationDocument.Parse("{\"extension\":[0]}");
        long before = GC.GetAllocatedBytesForCurrentThread();
        var document = RuleConfigurationDocument.Parse(json);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        document.Json.Should().Be(json);
        allocated.Should().BeLessThan(8 * 1024 * 1024, "an 18 KB configuration must not copy an 8 KB path for every scalar");
    }

    [Theory]
    [InlineData("{\"Rules\":[{\"RuleId\":\"\\uD800\"}]}", "$.Rules[0].RuleId")]
    [InlineData("{\"Rules\":[{\"RuleId\":\"RULE_004_ESTIMATE_MISMATCH\",\"SeverityOverride\":\"\\uDC00\"}]}", "$.Rules[0].SeverityOverride")]
    [InlineData("{\"extensions\":{\"\\uD800\":true}}", "$.extensions")]
    [InlineData("{\"extensions\":[\"\\uD800\"]}", "$.extensions[0]")]
    [InlineData("{\"Rules\":[{\"RuleId\":\"FUTURE\",\"payload\":\"\\uD800x\"}]}", "$.Rules[0].payload")]
    public void Parse_InvalidUnicode_IsLocatedValidationFailure(string json, string location)
    {
        Action parse = () => RuleConfigurationDocument.Parse(json);
        parse.Should().Throw<InvalidDataException>().Which.Message.Should().Contain(location).And.Contain("Unicode");
    }

    [Fact]
    public void Edit_ValidSurrogatePairsInUnknownFields_RoundTrip()
    {
        const string json = "{\"extensions\":{\"\\uD83D\\uDE00\":\"\\uD83D\\uDE80\"},\"Rules\":[]}";
        var document = RuleConfigurationDocument.Parse(json).WithSettings([new(RuleConfigurationDocumentTests.Id, false, null)]);
        using var parsed = System.Text.Json.JsonDocument.Parse(document.Json);
        parsed.RootElement.GetProperty("extensions").GetProperty("😀").GetString().Should().Be("🚀");
    }

    [Fact]
    public void Parse_LongDuplicateNames_BoundsDiagnosticOutput()
    {
        string key = new('x', 8000);
        Action parse = () => RuleConfigurationDocument.Parse("{\"extensions\":{\"" + key + "\":1,\"" + key + "\":2}}");
        parse.Should().Throw<InvalidDataException>().Which.Message.Length.Should().BeLessThan(2048);
    }

    [Fact]
    public void Parse_Cancelled_DoesNotReturnConfiguration()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action parse = () => RuleConfigurationDocument.Parse("{}", cancellation.Token);
        parse.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void Parse_UnescapedInvalidUtf16_IsValidationFailure()
    {
        Action parse = () => RuleConfigurationDocument.Parse("{\"extension\":\"" + '\ud800' + "\"}");
        parse.Should().Throw<InvalidDataException>().WithMessage("*Unicode*");
    }
}
