using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class PlanGraphCostVisualServiceTests
{
    private readonly PlanGraphCostVisualService _service = new();

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(40)]
    [InlineData(100)]
    public void GetStyle_UsesBlueIntensityWithoutRiskColors(double cost)
    {
        var style = _service.GetStyle(cost);
        foreach (string hex in new[] { style.BackgroundTopColorHex, style.BackgroundBottomColorHex, style.BorderColorHex, style.BadgeBackgroundColorHex })
        {
            int red = Convert.ToInt32(hex.Substring(1, 2), 16);
            int blue = Convert.ToInt32(hex.Substring(5, 2), 16);
            blue.Should().BeGreaterThan(red);
        }
        style.BorderThickness.Should().Be(1, "estimated cost must not change the severity outline");
    }

    [Fact]
    public void GetStyle_HigherCostHasDarkerIntensity()
    {
        int Brightness(string hex) => Convert.ToInt32(hex.Substring(1, 2), 16) + Convert.ToInt32(hex.Substring(3, 2), 16) + Convert.ToInt32(hex.Substring(5, 2), 16);
        Brightness(_service.GetStyle(100).BackgroundBottomColorHex).Should().BeLessThan(Brightness(_service.GetStyle(0).BackgroundBottomColorHex));
    }

    [Fact]
    public void GetStyle_OutOfRangeValuesClampTheGradient()
    {
        _service.GetStyle(-10).BackgroundTopColorHex.Should().Be(_service.GetStyle(0).BackgroundTopColorHex);
        _service.GetStyle(125).BackgroundTopColorHex.Should().Be(_service.GetStyle(100).BackgroundTopColorHex);
    }
}
