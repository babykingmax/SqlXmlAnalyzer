using System.IO;
using System.Text.Json;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests.Compatibility;

public sealed class ConfigurationCompatibilityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SqlXmlAnalyzer-IMP28-config-" + Guid.NewGuid().ToString("N"));
    public ConfigurationCompatibilityTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData("\"2\"")]
    [InlineData("\"1.1\"")]
    [InlineData("1")]
    [InlineData("null")]
    [InlineData("\"\"")]
    public void Parse_UnsupportedDeclaredVersionCannotExecuteAsLegacy(string version)
    {
        Action parse = () => RuleConfigurationDocument.Parse($$"""{"ConfigurationSchemaVersion":{{version}},"Rules":[]}""");
        parse.Should().Throw<InvalidDataException>().WithMessage("*CONFIG_SCHEMA_UNSUPPORTED*");
    }

    [Fact]
    public void VersionMigration_PreservesPreviouslyAcceptedGenericSchemaVersionExtension()
    {
        var original = RuleConfigurationDocument.Parse(RuleConfigurationDocumentTests.CompatibleJson);
        original.SchemaVersion.Should().BeNull();
        var migrated = original.WithCurrentSchema();
        migrated.SchemaVersion.Should().Be("1");
        migrated.Fingerprint.Should().Be(original.Fingerprint);
        using var json = JsonDocument.Parse(migrated.Json);
        json.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(9);
        json.RootElement.GetProperty("ConfigurationSchemaVersion").GetString().Should().Be("1");
    }

    [Fact]
    public async Task Load_UnsupportedSchemaPreservesFileAndActiveConfiguration()
    {
        string source = Path.Combine(_directory, "future.json");
        File.WriteAllText(source, "{\"ConfigurationSchemaVersion\":\"99\",\"Rules\":[]}");
        byte[] original = File.ReadAllBytes(source);
        var active = new RuleConfigurationSession(RuleConfigurationDocument.Parse(CompatibilityFixtures.Read("configuration-legacy.json")));
        var before = active.Capture();
        Func<Task> load = async () => active.Apply((await new RuleConfigurationStore().LoadAsync(source, default)).Document);
        await load.Should().ThrowAsync<InvalidDataException>().WithMessage("*CONFIG_SCHEMA_UNSUPPORTED*");
        active.Capture().Should().BeSameAs(before);
        RuleConfigurationLoader.Load(source).IsSuccess.Should().BeFalse();
        File.ReadAllBytes(source).Should().Equal(original);
        Directory.GetFiles(_directory).Should().ContainSingle();
    }

    [Fact]
    public void Parse_PreImprovementConfigurationRetainsKnownSettings()
    {
        var document = RuleConfigurationDocument.Parse(CompatibilityFixtures.Read("configuration-imp01.json"));
        document.Get("RULE_015_LOCAL_VARIABLES").SeverityOverride.Should().Be("Warning");
        document.Get("RULE_004_ESTIMATE_MISMATCH").Enabled.Should().BeTrue();
        document.UnknownRules.Should().BeEmpty();
    }

    [Fact]
    public async Task EditAndSaveCopy_PreservesLegacyAliasesExtensionsAndOriginalBytes()
    {
        string source = Path.Combine(_directory, "legacy.json");
        string destination = Path.Combine(_directory, "converted.json");
        File.WriteAllText(source, CompatibilityFixtures.Read("configuration-legacy.json"));
        byte[] original = File.ReadAllBytes(source);
        var store = new RuleConfigurationStore();
        var loaded = await store.LoadAsync(source, default);
        loaded.Document.Get("RULE_036_WAIT_STATS").Enabled.Should().BeFalse();
        loaded.Document.UnknownRules.Should().ContainSingle().Which.RuleId.Should().Be("FUTURE_VENDOR_RULE");
        var converted = loaded.Document.WithCurrentSchema();
        converted.Fingerprint.Should().Be(loaded.Document.Fingerprint);
        converted.WithCurrentSchema().Should().BeSameAs(converted);
        var edited = converted.WithSettings([new("RULE_004_ESTIMATE_MISMATCH", false, "Critical")]);
        var saved = await store.SaveAsAsync(destination, edited, default);
        saved.Succeeded.Should().BeTrue(saved.Message);
        File.ReadAllBytes(source).Should().Equal(original);
        var roundtrip = await store.LoadAsync(destination, default);
        roundtrip.Document.Get("RULE_004_ESTIMATE_MISMATCH").Enabled.Should().BeFalse();
        using var json = JsonDocument.Parse(roundtrip.Document.Json);
        json.RootElement.GetProperty("VendorRootOption").GetProperty("Keep").GetString().Should().Be("legacy-extension");
        var rules = json.RootElement.GetProperty("Rules");
        json.RootElement.GetProperty("ConfigurationSchemaVersion").GetString().Should().Be("1");
        rules[0].GetProperty("RuleId").GetString().Should().Be("RULE_016_WAIT_STATS");
        rules[1].GetProperty("VendorRuleOption").GetProperty("Keep").GetBoolean().Should().BeTrue();
        rules[2].GetProperty("Enabled").GetString().Should().Be("future-semantics");
        Func<Task> overwrite = async () => await store.SaveAsAsync(source, edited, default);
        await overwrite.Should().ThrowAsync<IOException>();
        File.ReadAllBytes(source).Should().Equal(original);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
