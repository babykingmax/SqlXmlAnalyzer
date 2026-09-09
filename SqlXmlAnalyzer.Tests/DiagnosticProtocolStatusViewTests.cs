using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;
using static SqlXmlAnalyzer.Tests.Rules.DiagnosticProtocolTests;

namespace SqlXmlAnalyzer.Tests;

public sealed class DiagnosticProtocolStatusViewTests
{
    [Fact]
    public void ProductionTooltipPanels_SeparateStatusFromWarningsForAllOutcomes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Loose XAML needs the icon assembly loaded before resolving its URI namespace.
                _ = new MaterialDesignThemes.Wpf.PackIcon();
                var root = SafeXmlHelper.LoadSafe(Path.Combine(FindRepository(), "PlanGraphControl.xaml")).Root!;
                XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                XElement Panel(string visibility) => root.Descendants(presentation + "StackPanel").Single(e =>
                    (string?)e.Attribute("Visibility") == "{Binding " + visibility + "}");
                var canvas = new StackPanel { Width = 560, Background = Brushes.White };
                foreach (string state in new[] { "NoHit", "Skipped", "Hit", "Failed" })
                {
                    var rule = new NativeRule("TEST_STATUS", c => state switch
                    {
                        "NoHit" => RuleEvaluation.NoHit(),
                        "Skipped" => RuleEvaluation.Skipped("RULE_MISSING_EVIDENCE", "No runtime counters"),
                        "Hit" => RuleEvaluation.Hit(Observation(c)),
                        _ => throw new InvalidOperationException("Synthetic failure")
                    });
                    var op = DiagnosticProtocolHardeningFlowTests.CleanPlan().Descendants(Ns + "RelOp").Single();
                    var node = new PlanGraphNodeBuilderService(ruleEngine: Engine(rule)).Build(op, Ns, new(2, 100));
                    var fragment = new XElement(presentation + "StackPanel",
                        new XElement(Panel("HasDiagnosticStatusVisible")), new XElement(Panel("HasWarningVisible")));
                    foreach (var declaration in root.Attributes().Where(a => a.IsNamespaceDeclaration)) fragment.Add(new XAttribute(declaration));
                    using var reader = fragment.CreateReader();
                    var panels = (StackPanel)XamlReader.Load(reader);
                    panels.Resources["TextBrush"] = Brushes.Black;
                    panels.Resources["BorderBrush"] = Brushes.LightGray;
                    panels.Resources["AccentBrush"] = Brushes.DarkSlateBlue;
                    panels.DataContext = new PlanNodeViewModel { Warnings = node.Warnings, DiagnosticStatusText = node.DiagnosticStatusText };
                    canvas.Children.Add(new TextBlock { Text = state, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(12, 14, 12, 0) });
                    canvas.Children.Add(new Border { Padding = new Thickness(12), Child = panels });
                    canvas.Measure(new Size(560, double.PositiveInfinity));
                    canvas.Arrange(new Rect(new Point(), canvas.DesiredSize));
                    canvas.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    var statusPanel = (StackPanel)panels.Children[0];
                    var warningPanel = (StackPanel)panels.Children[1];
                    statusPanel.Visibility.Should().Be(Visibility.Visible);
                    ((TextBox)statusPanel.Children[2]).Text.Should().Contain(state + " 1");
                    warningPanel.Visibility.Should().Be(state is "Hit" or "Failed" ? Visibility.Visible : Visibility.Collapsed);
                }
                string? output = Environment.GetEnvironmentVariable("SQLXML_IMP12_VISUAL_OUTPUT");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    var bitmap = new RenderTargetBitmap(560, (int)Math.Ceiling(canvas.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(canvas);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(output, "node-status.png"));
                    encoder.Save(stream);
                }
            }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SqlXmlAnalyzer.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
