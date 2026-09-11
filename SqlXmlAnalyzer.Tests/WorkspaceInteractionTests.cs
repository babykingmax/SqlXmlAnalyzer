using System.IO;
using System.Windows.Media;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class WorkspaceInteractionTests
{
    [Theory]
    [InlineData(1024, 576, true)]
    [InlineData(853.33, 480, true)]
    [InlineData(640, 360, true)]
    [InlineData(1092.8, 614.4, true)]
    [InlineData(910.67, 512, true)]
    [InlineData(683, 384, true)]
    [InlineData(1536, 864, false)]
    [InlineData(1280, 720, false)]
    [InlineData(960, 540, true)]
    public void Layout_UsesDipAreaForRequestedMonitorMatrix(double width, double height, bool compact)
    {
        var layout = WorkspaceInteractionService.Layout(width, height);
        layout.Compact.Should().Be(compact);
        layout.MinimumWidth.Should().BeLessThanOrEqualTo(width);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void Layout_InvalidDimensions_AreExpectedInputErrors(double width)
    {
        Action build = () => WorkspaceInteractionService.Layout(width, 720);
        build.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(0, -1, 1, -1)]
    [InlineData(3, -1, 1, 0)]
    [InlineData(3, -1, -1, 2)]
    [InlineData(3, 2, 1, 0)]
    [InlineData(3, 0, -1, 2)]
    [InlineData(3, 20, 1, 0)]
    [InlineData(int.MaxValue, int.MaxValue - 2, -1, int.MaxValue - 3)]
    [InlineData(int.MaxValue, int.MaxValue - 1, 1, 0)]
    public void KeyboardSelection_HandlesEmptyStaleAndWraparound(int count, int selected, int direction, int expected) =>
        WorkspaceInteractionService.MoveSelection(count, selected, direction).Should().Be(expected);

    [Fact]
    public void ReducedMotion_PausesAutomaticPlaybackAndKeepsManualSteps()
    {
        var model = new SqlXmlAnalyzer.ViewModels.DeadlockPlaybackViewModel([
            new Core.Models.DeadlockEvent { StepNumber = 1, Description = "first" },
            new Core.Models.DeadlockEvent { StepNumber = 2, Description = "second" }]);
        model.IsPlaying = true;
        model.ReduceMotion = true;
        model.IsPlaying.Should().BeFalse();
        model.PlayCommand.CanExecute(null).Should().BeFalse();
        model.PlayCommand.Execute(null);
        model.IsPlaying.Should().BeFalse();
        model.StepForwardCommand.Execute(null);
        model.CurrentStep.Should().Be(1);
        model.ReduceMotion = false;
        model.PlayCommand.CanExecute(null).Should().BeTrue();
        model.IsPlaying.Should().BeFalse("changing the preference must not restart playback");
    }

    [Fact]
    public void DisabledCommand_DoesNotInvokeAction()
    {
        bool invoked = false;
        new WorkspaceInteractionService().Run("Disabled", () => false, () => invoked = true, _ => { }).Should().BeFalse();
        invoked.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Theme_ImportantSemanticPairsHaveTextContrast(bool dark)
    {
        var palette = WorkspaceThemeService.Palette(dark);
        foreach (var pair in new[] { ("TextBrush", "SurfaceBrush"), ("SecondaryTextBrush", "SurfaceBrush"),
            ("TextBrush", "SelectionBrush"), ("WarningBrush", "WarningSurfaceBrush"), ("CriticalBrush", "CriticalSurfaceBrush"),
            ("AccentForegroundBrush", "AccentBrush"), ("DisabledTextBrush", "DisabledSurfaceBrush") })
            Contrast(palette[pair.Item1], palette[pair.Item2]).Should().BeGreaterThanOrEqualTo(4.5, $"{pair} in dark={dark}");
    }

    internal static double Contrast(Color a, Color b)
    {
        static double Linear(byte value) { double c = value / 255.0; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        static double Luminance(Color c) => .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        double left = Luminance(a), right = Luminance(b);
        return (Math.Max(left, right) + .05) / (Math.Min(left, right) + .05);
    }
}
