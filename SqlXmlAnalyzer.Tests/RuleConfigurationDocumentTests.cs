using System.IO;
using System.Text.Json.Nodes;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Rules;

namespace SqlXmlAnalyzer.Tests;

public sealed class RuleConfigurationDocumentTests
{
    internal const string Id = "RULE_004_ESTIMATE_MISMATCH";
    internal const string CompatibleJson = """
        {"schemaVersion":9,"extensions":{"secret":"private-config-marker","array":[1,null,true]},"rules":[
          {"ruleid":"RULE_004_ESTIMATE_MISMATCH","enabled":false,"severityoverride":"Info","future":{"a":1}},
          {"RuleId":"FUTURE_RULE","Enabled":"future-mode","SeverityOverride":{"future":9},"payload":[1,2]},
          {"RuleId":"RULE_016_WAIT_STATS","Enabled":true}
        ]}
        """;

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Rules\":null}")]
    [InlineData("{\"Rules\":[]}")]
    public void Parse_OldEmptyForms_KeepBuiltInDefaults(string json)
    {
        var config = RuleConfigurationDocument.Parse(json);
        config.Fingerprint.Should().Be(RuleConfigurationDocument.Defaults.Fingerprint);
        config.Get(Id).Should().Be(new RuleConfigurationSnapshot(Id, true, null));
    }

    [Fact]
    public void EditAndReset_PreserveUnknownRulesFieldsAliasesAndRootCasing()
    {
        var original = RuleConfigurationDocument.Parse(CompatibleJson);
        var edited = original.WithSettings([new(Id, true, "Critical")]);
        var reset = edited.WithSettings([new(Id, true, null)]);
        var before = JsonNode.Parse(CompatibleJson)!;
        foreach (var document in new[] { edited, reset })
        {
            var after = JsonNode.Parse(document.Json)!;
            after["Rules"].Should().BeNull();
            JsonNode.DeepEquals(before["extensions"], after["extensions"]).Should().BeTrue();
            JsonNode.DeepEquals(before["rules"]![0]!["future"], after["rules"]![0]!["future"]).Should().BeTrue();
            JsonNode.DeepEquals(before["rules"]![1], after["rules"]![1]).Should().BeTrue();
            after["rules"]![2]!["RuleId"]!.GetValue<string>().Should().Be("RULE_016_WAIT_STATS");
            document.UnknownRules.Should().ContainSingle().Which.Location.Should().Be("$.Rules[1]");
            document.Warnings.Should().Contain(w => w.Contains("RULE_036_WAIT_STATS"));
        }
        original.Get(Id).Enabled.Should().BeFalse();
        edited.Get(Id).SeverityOverride.Should().Be("Critical");
        reset.Fingerprint.Should().Be(RuleConfigurationDocument.Defaults.Fingerprint);
    }

    [Fact]
    public void LegacyProjection_MutationCannotChangeCapturedConfiguration()
    {
        var config = RuleConfigurationDocument.Parse(CompatibleJson); string fingerprint = config.Fingerprint;
        var mutable = config.ToLegacy(); mutable.Rules[0].Enabled = true; mutable.Rules.Clear();
        config.Get(Id).Enabled.Should().BeFalse(); config.Fingerprint.Should().Be(fingerprint);
    }

    [Theory]
    [InlineData("{\"Rules\":[{\"RuleId\":\"RULE_004_ESTIMATE_MISMATCH\",\"Enabled\":1}]}", "$.Rules[0].Enabled")]
    [InlineData("{\"Rules\":[{\"RuleId\":\"RULE_004_ESTIMATE_MISMATCH\",\"SeverityOverride\":\"Fatal\"}]}", "$.Rules[0].SeverityOverride")]
    [InlineData("{\"Rules\":[{\"RuleId\":\"RULE_004_ESTIMATE_MISMATCH\",\"Enabled\":true,\"enabled\":false}]}", "$.Rules[0].Enabled")]
    [InlineData("{\"Rules\":[],\"rules\":[]}", "$.Rules")]
    [InlineData("{\"Rules\":[null]}", "$.Rules[0]")]
    [InlineData("{\"extensions\":{\"a\":1,\"a\":2}}", "extensions")]
    [InlineData("{\"Rules\":[{\"RuleId\":\"RULE_016_WAIT_STATS\"},{\"RuleId\":\"RULE_036_WAIT_STATS\"}]}", "Duplicate RuleId")]
    [InlineData("{\"Rules\":[{\"RuleId\":\"future\"},{\"RuleId\":\"FUTURE\"}]}", "Duplicate RuleId")]
    [InlineData("{\n\"Rules\":broken}", "行 2")]
    public void Parse_InvalidInput_HasLocationAndNoPartialApplication(string json, string location)
    {
        var action = () => RuleConfigurationDocument.Parse(json);
        action.Should().Throw<InvalidDataException>().Which.Message.Should().Contain(location);
    }

    [Fact]
    public void Parse_BudgetsBoundBytesDepthAndEntries()
    {
        Action bytes = () => RuleConfigurationDocument.Parse("{\"x\":\"" + new string('x', RuleConfigurationDocument.MaxBytes) + "\"}");
        Action depth = () => RuleConfigurationDocument.Parse("{\"x\":" + new string('[', 33) + "0" + new string(']', 33) + "}");
        Action entries = () => RuleConfigurationDocument.Parse("{\"Rules\":[" + string.Join(",", Enumerable.Range(0, 1025).Select(i => $"{{\"RuleId\":\"FUTURE_{i}\"}}")) + "]}");
        bytes.Should().Throw<InvalidDataException>().WithMessage("*1 MiB*");
        depth.Should().Throw<InvalidDataException>().WithMessage("*invalid JSON*");
        entries.Should().Throw<InvalidDataException>().WithMessage("*1024*");
    }

    [Fact]
    public void UnchangedSettings_DoNotReformatOrAmplifyLargeUnknownPayload()
    {
        var document = RuleConfigurationDocument.Parse("{\"extensions\":[" + string.Join(",", Enumerable.Repeat("0", 200_000)) + "],\"Rules\":[]}");
        var noChange = document.WithSettings(RuleMetadataCatalog.RegisteredRuleIds.Select(id => document.Get(id)));
        noChange.Should().BeSameAs(document);
        noChange.Json.Length.Should().BeLessThan(RuleConfigurationDocument.MaxBytes);
    }

    [Fact]
    public void Fingerprint_TracksEffectiveSettingsAndIgnoresUnknownFieldsAndOrdering()
    {
        var left = RuleConfigurationDocument.Parse("{\"Rules\":[{\"RuleId\":\"rule_004_estimate_mismatch\",\"SeverityOverride\":\" warning \"}]}");
        var right = RuleConfigurationDocument.Defaults.WithSettings([new(Id, true, "Warning")]);
        left.Fingerprint.Should().Be(right.Fingerprint);
        RuleConfigurationDocument.Parse("{\"Future\":123,\"Rules\":[{\"RuleId\":\"Future\"}]}").Fingerprint
            .Should().Be(RuleConfigurationDocument.Defaults.Fingerprint);
        right.Fingerprint.Should().NotBe(RuleConfigurationDocument.Defaults.Fingerprint, "an override differs from evidence-dependent default severity");
    }
}
