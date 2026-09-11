using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanWorkspacePreferencesTests
{
    [Fact]
    public void SaveAndLoad_PreservesUserLayoutAndColumnVisibility()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PlanPreferences-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PlanWorkspacePreferencesStore(Path.Combine(directory, "preferences.json"));
            store.Load().Should().BeEquivalentTo(new PlanWorkspacePreferences());
            var preferences = new PlanWorkspacePreferences
            {
                LeftWidth = 310, RightWidth = 360, ActiveView = "operators", LeftOpen = false,
                Columns = [new("ActualRows", 150, 3, false), new("ObjectDisplay", 260, 0, true)]
            };
            store.Save(preferences).Should().BeTrue();
            store.Load().Should().BeEquivalentTo(preferences);
            Directory.GetFiles(directory).Should().HaveCount(1, "atomic save removes its temporary file");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Version\":99,\"LeftWidth\":900}")]
    [InlineData("null")]
    public void Load_InvalidOrFuturePreferences_FallsBackWithoutBlockingOpen(string json)
    {
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, json);
            new PlanWorkspacePreferencesStore(file).Load().Should().BeEquivalentTo(new PlanWorkspacePreferences());
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Normalize_ClampsSizesAndDiscardsDuplicateColumns()
    {
        var result = PlanWorkspacePreferencesStore.Normalize(new()
        {
            LeftWidth = double.NaN, RightWidth = 9999, AuxiliaryHeight = -1, ActiveView = "unknown",
            Columns = [new("ActualRows", double.PositiveInfinity, -1, true), new("ActualRows", 9000, 88, false)]
        });
        result.LeftWidth.Should().Be(280);
        result.RightWidth.Should().Be(520);
        result.AuxiliaryHeight.Should().Be(120);
        result.ActiveView.Should().Be("graph");
        result.Columns.Should().ContainSingle().Which.Should().Be(new PlanGridColumnPreference("ActualRows", 120, 0, true));
    }

    [Fact]
    public void Save_UnavailableDestination_DoesNotThrowOrReplaceExistingData()
    {
        string parentFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(parentFile, "retain");
            new PlanWorkspacePreferencesStore(Path.Combine(parentFile, "prefs.json")).Save(new()).Should().BeFalse();
            File.ReadAllText(parentFile).Should().Be("retain");
        }
        finally { File.Delete(parentFile); }
    }
}
