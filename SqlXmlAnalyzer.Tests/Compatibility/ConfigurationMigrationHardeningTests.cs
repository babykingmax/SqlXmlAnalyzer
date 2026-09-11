using System.IO;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests.Compatibility;

public sealed class ConfigurationMigrationHardeningTests
{
    [Theory]
    [InlineData("unicode-value")]
    [InlineData("unicode-name")]
    [InlineData("supplementary-value")]
    [InlineData("html-characters")]
    public void VersionMigration_DoesNotExpandAcceptedExtensionCharacters(string scenario)
    {
        string name = scenario == "unicode-name" ? new string('中', 200_000) : "extension";
        string value = scenario switch
        {
            "unicode-value" => new string('中', 200_000),
            "supplementary-value" => string.Concat(Enumerable.Repeat("😀", 100_000)),
            "html-characters" => new string('<', 200_000),
            _ => "unchanged"
        };
        string json = "{\"" + name + "\":\"" + value + "\",\"Rules\":[]}";
        var original = RuleConfigurationDocument.Parse(json);

        var converted = original.WithCurrentSchema();

        converted.SchemaVersion.Should().Be("1");
        converted.Fingerprint.Should().Be(original.Fingerprint);
        Encoding.UTF8.GetByteCount(converted.Json).Should().BeLessThan(Encoding.UTF8.GetByteCount(json) + 64);
        using var parsed = JsonDocument.Parse(converted.Json);
        parsed.RootElement.GetProperty(name).GetString().Should().Be(value);
        original.Json.Should().Be(json);
    }

    [Fact]
    public void VersionMigration_DenseUnknownArrayStillFitsConfigurationBudget()
    {
        string json = "{\"Rules\":[],\"extension\":[" + string.Join(',', Enumerable.Repeat("0", 200_000)) + "]}";
        var original = RuleConfigurationDocument.Parse(json);

        var converted = original.WithCurrentSchema();

        converted.SchemaVersion.Should().Be("1");
        converted.Fingerprint.Should().Be(original.Fingerprint);
        Encoding.UTF8.GetByteCount(converted.Json).Should().BeLessThan(RuleConfigurationDocument.MaxBytes);
        using var parsed = JsonDocument.Parse(converted.Json);
        var values = parsed.RootElement.GetProperty("extension");
        values.GetArrayLength().Should().Be(200_000);
        values.EnumerateArray().Should().OnlyContain(value => value.GetInt32() == 0);
        original.Json.Should().Be(json);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData(" \r\n\t{ \r\n\t}\t\n")]
    [InlineData("\r\n{ \"Rules\":null }\t ")]
    public void VersionMigration_EmptyAndWhitespaceFormsRetainDefaultSettings(string json)
    {
        var converted = RuleConfigurationDocument.Parse(json).WithCurrentSchema();
        converted.SchemaVersion.Should().Be("1");
        converted.Fingerprint.Should().Be(RuleConfigurationDocument.Defaults.Fingerprint);
        RuleConfigurationDocument.Parse(converted.Json).SchemaVersion.Should().Be("1");
    }

    [Fact]
    public void VersionMigration_PreservesUnknownRawValuesNamesAndRuleAliases()
    {
        const string json = """
            {
              "sch\u0065maVersion":9,
              "Rules":[{"RuleId":"RULE_016_WAIT_STATS","Enabled":false},{"RuleId":"FUTURE_RULE","Enabled":"future"}],
              "extension":{"negativeZero":-0,"huge":1e9999,"escaped":"\u4e2d\u6587","text":"{}\"\\", "Case":1, "case":2}
            }
            """;
        var original = RuleConfigurationDocument.Parse(json);
        var converted = original.WithCurrentSchema();
        converted.Fingerprint.Should().Be(original.Fingerprint);
        converted.Get("RULE_036_WAIT_STATS").Enabled.Should().BeFalse();
        converted.UnknownRules.Should().ContainSingle().Which.Json.Should().Be(original.UnknownRules.Single().Json);
        using var before = JsonDocument.Parse(json);
        using var after = JsonDocument.Parse(converted.Json);
        foreach (var property in before.RootElement.EnumerateObject())
            after.RootElement.GetProperty(property.Name).GetRawText().Should().Be(property.Value.GetRawText());
        converted.Json.Should().Contain("\"sch\\u0065maVersion\":9");
    }

    [Theory]
    [InlineData("{\"configurationschemaversion\":\"1\",\"Rules\":[]}")]
    [InlineData("{\"ConfigurationSchema\\u0056ersion\":\"1\",\"extension\":\"中文\"}")]
    public void VersionMigration_AlreadyVersionedDocumentKeepsExactInstanceAndText(string json)
    {
        var original = RuleConfigurationDocument.Parse(json);
        original.WithCurrentSchema().Should().BeSameAs(original);
        original.Json.Should().Be(json);
    }

    [Theory]
    [InlineData(1_048_543, true)]
    [InlineData(1_048_544, false)]
    public void VersionMigration_EnforcesUtf8ByteBudgetAtTheVersionFieldBoundary(int inputBytes, bool fits)
    {
        string json = NearLimitJson(inputBytes);
        Encoding.UTF8.GetByteCount(json).Should().Be(inputBytes);
        var original = RuleConfigurationDocument.Parse(json);
        if (fits)
        {
            var converted = original.WithCurrentSchema();
            Encoding.UTF8.GetByteCount(converted.Json).Should().Be(1_048_576);
            RuleConfigurationDocument.Parse(converted.Json).SchemaVersion.Should().Be("1");
            converted.Fingerprint.Should().Be(original.Fingerprint);
        }
        else
        {
            Action migrate = () => original.WithCurrentSchema();
            var failure = migrate.Should().Throw<InvalidDataException>().WithMessage("CONFIG_MIGRATION_BUDGET_EXCEEDED*").Which;
            ExceptionPolicy.IsExpected(failure).Should().BeTrue();
        }
        original.Json.Should().Be(json);
        original.SchemaVersion.Should().BeNull();
    }

    [Fact]
    public async Task SaveMigratedCopy_LargeExtensionRoundtripsAndOriginalSurvivesOverwriteAndCancellation()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP28-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "legacy.json");
            string destination = Path.Combine(directory, "converted.json");
            string value = new('中', 200_000);
            File.WriteAllText(source, "{\"Rules\":[],\"extension\":\"" + value + "\"}");
            byte[] original = File.ReadAllBytes(source);
            var store = new RuleConfigurationStore();
            var loaded = await store.LoadAsync(source, default);
            var converted = loaded.Document.WithCurrentSchema();
            var saved = await store.SaveAsAsync(destination, converted, default);
            saved.Succeeded.Should().BeTrue(saved.Message);
            var restored = await store.LoadAsync(destination, default);
            restored.Document.Json.Should().Be(converted.Json);
            restored.Document.Fingerprint.Should().Be(loaded.Document.Fingerprint);
            using var json = JsonDocument.Parse(restored.Document.Json);
            json.RootElement.GetProperty("extension").GetString().Should().Be(value);

            Func<Task> overwrite = () => store.SaveAsAsync(source, converted, default);
            await overwrite.Should().ThrowAsync<IOException>();
            Func<Task> cancelled = () => store.SaveAsAsync(Path.Combine(directory, "cancelled.json"), converted, new(true));
            await cancelled.Should().ThrowAsync<OperationCanceledException>();
            File.ReadAllBytes(source).Should().Equal(original);
            Directory.GetFiles(directory).Should().HaveCount(2);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task SaveMigratedCopy_WhenVersionWouldExceedBudgetPreservesSourceAndActiveConfiguration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP28-migration-limit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "legacy.json");
            File.WriteAllText(source, NearLimitJson(1_048_544));
            byte[] bytes = File.ReadAllBytes(source);
            var store = new RuleConfigurationStore();
            var loaded = await store.LoadAsync(source, default);
            var active = new RuleConfigurationSession(loaded.Document);
            var before = active.Capture();
            Func<Task> migrate = () => store.SaveAsAsync(Path.Combine(directory, "converted.json"), loaded.Document.WithCurrentSchema(), default);
            await migrate.Should().ThrowAsync<InvalidDataException>().WithMessage("CONFIG_MIGRATION_BUDGET_EXCEEDED*");
            File.ReadAllBytes(source).Should().Equal(bytes);
            active.Capture().Should().BeSameAs(before);
            Directory.GetFiles(directory).Should().ContainSingle();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string NearLimitJson(int bytes)
    {
        string prefix = "{\"Rules\":[],\"extension\":\"" + new string('中', 349_000);
        return prefix + new string('x', bytes - Encoding.UTF8.GetByteCount(prefix) - 2) + "\"}";
    }
}
