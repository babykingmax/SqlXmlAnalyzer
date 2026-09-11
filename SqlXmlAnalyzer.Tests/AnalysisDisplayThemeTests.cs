using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using FluentAssertions;
using MaterialDesignColors;
using MaterialDesignThemes.Wpf;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests;

public sealed class AnalysisDisplayThemeTests
{
    [Theory]
    [InlineData("DeadlockPlaybackControl.xaml", "DependencyInferenceNotice")]
    [InlineData("IndexSandboxWindow.xaml", "CostReductionSummary")]
    [InlineData("IndexSandboxWindow.xaml", "SimulationNotice")]
    [InlineData("IndexSandboxWindow.xaml", "CostReductionDescription")]
    [InlineData("IndexSandboxWindow.xaml", "InputAssumptionsNotice")]
    [InlineData("IndexSandboxWindow.xaml", "TippingPointStatus")]
    [InlineData("IndexSandboxWindow.xaml", "TippingPointDetails")]
    [InlineData("IndexSandboxWindow.xaml", "ImpactSummary")]
    [InlineData("IndexSandboxWindow.xaml", "ScoreBreakdown")]
    [InlineData("IndexSandboxWindow.xaml", "ScoreWeights")]
    [InlineData("IndexSandboxWindow.xaml", "ScoreSource")]
    [InlineData("IndexSandboxWindow.xaml", "ModelSummary")]
    [InlineData("IndexSandboxWindow.xaml", "CostEvidenceSummary")]
    [InlineData("PlanComparisonWorkspaceView.xaml", "ComparisonEvidenceNotice")]
    [InlineData("PlanComparisonWorkspaceView.xaml", "ComparisonMatchSummary")]
    [InlineData("PlanComparisonWorkspaceView.xaml", "ComparisonConditionsText")]
    public void EvidenceText_RemainsReadableWhenThemeResourcesAreReplaced(string view, string target)
    {
        RunSta(() =>
        {
            // Exercise the production TextBlock and its declared backing surface without a global
            // Application. A separate process probe verifies the complete controls and PaletteHelper.
            Border host = LoadEvidenceText(view, target);
            var text = (TextBlock)host.Child;
            host.DataContext = new IndexSandboxViewModel(new MissingIndexSuggestion
            {
                Table = "[Orders]",
                Schema = "[dbo]",
                KeyColumns = [new IndexColumn { Name = "[Id]", Usage = "EQUALITY" }]
            });

            if (target is "ComparisonMatchSummary" or "ComparisonConditionsText")
            {
                var vm = new Core.ViewModels.MainViewModel();
                vm.PublishComparison(PlanComparisonMultiStatementTests.Compare(
                    PlanComparisonMultiStatementTests.RuntimeSnapshot(100), PlanComparisonMultiStatementTests.RuntimeSnapshot(10)));
                host.DataContext = vm;
            }
            foreach (BaseTheme theme in new[] { BaseTheme.Light, BaseTheme.Dark, BaseTheme.Light, BaseTheme.Dark })
            {
                host.Resources.MergedDictionaries.Clear();
                host.Resources.MergedDictionaries.Add(new BundledTheme
                {
                    BaseTheme = theme, PrimaryColor = PrimaryColor.DeepPurple, SecondaryColor = SecondaryColor.Lime
                });
                Core.Services.WorkspaceThemeService.Apply(host.Resources, theme == BaseTheme.Dark);
                host.Measure(new Size(900, 300));
                host.Arrange(new Rect(0, 0, 900, 300));
                host.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

                text.Text.Should().NotBeNullOrWhiteSpace();
                text.ActualHeight.Should().BeGreaterThan(0);
                Contrast(text.Foreground, host.Background).Should().BeGreaterThanOrEqualTo(4.5,
                    $"{view}/{target} must remain readable in {theme}, including after resource replacement");
            }
        });
    }

    private static Border LoadEvidenceText(string view, string target)
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XElement root = SafeXmlHelper.LoadSafe(Path.Combine(FindRepository(), "Views", view)).Root!;
        XElement text = root.Descendants(presentation + "TextBlock").Single(element =>
            (string?)element.Attribute(x + "Name") == target ||
            (string?)element.Attribute("Text") == $"{{Binding {target}}}");
        string background = text.Ancestors().Select(element => (string?)element.Attribute("Background"))
            .First(value => value != null)!;
        var fragment = new XElement(presentation + "Border", new XAttribute("Background", background), new XElement(text));
        foreach (XAttribute declaration in root.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            fragment.Add(new XAttribute(declaration));
        using var reader = fragment.CreateReader();
        return (Border)XamlReader.Load(reader);
    }

    private static double Contrast(Brush foreground, Brush background)
    {
        var ink = foreground.Should().BeOfType<SolidColorBrush>().Subject;
        var paper = background.Should().BeOfType<SolidColorBrush>().Subject;
        paper.Color.A.Should().Be(255);
        paper.Opacity.Should().Be(1);
        double alpha = ink.Color.A / 255.0 * ink.Opacity;
        double red = ink.Color.R * alpha + paper.Color.R * (1 - alpha);
        double green = ink.Color.G * alpha + paper.Color.G * (1 - alpha);
        double blue = ink.Color.B * alpha + paper.Color.B * (1 - alpha);
        double inkLuminance = Luminance(red, green, blue);
        double paperLuminance = Luminance(paper.Color.R, paper.Color.G, paper.Color.B);
        return (Math.Max(inkLuminance, paperLuminance) + 0.05) / (Math.Min(inkLuminance, paperLuminance) + 0.05);
    }

    private static double Luminance(double red, double green, double blue)
    {
        static double Linear(double channel)
        {
            double value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(red) + 0.7152 * Linear(green) + 0.0722 * Linear(blue);
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SqlXmlAnalyzer.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository containing WPF source was not found.");
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new TimeoutException("Theme regression test timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
