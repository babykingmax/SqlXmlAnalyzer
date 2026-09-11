using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentAssertions;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Parsers;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Services;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests;

public sealed class DeadlockUnifiedViewTests
{
    [Fact]
    public void ProductionCards_RetainSharedCycleAndVictimStateAfterPlaybackReset()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var document = SafeXmlHelper.ParseSafe(Utilities.EmbeddedResourceHelper.GetResourceContent("imp13_overlapping_deadlock.xdl"));
                var analysis = new DeadlockAnalysisService(options: new() { MaxCycleLength = 1 }).Analyze(document);
                var root = new StackPanel { Width = 760, Background = Brushes.White };
                root.Children.Add(new TextBlock { Text = "IMP-13 · 生产节点组件：退出依赖推演后的状态", FontSize = 19, Margin = new Thickness(15) });
                root.Children.Add(new TextBlock { Text = new DeadlockPlaybackViewModel(analysis.Timeline).InferenceNotice,
                    Margin = new Thickness(15), TextWrapping = TextWrapping.Wrap, FontSize = 14 });
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(15) };
                root.Children.Add(row);
                var factory = new DeadlockGraphNodeElementFactory();
                var visual = new DeadlockGraphPlaybackVisualService();
                var stateService = new DeadlockGraphVisualStateService();
                foreach (var process in analysis.Processes)
                {
                    bool victim = analysis.Graph.VictimProcessIds.Contains(process.Id);
                    bool inCycle = analysis.Graph.CycleAnalysis.ProcessIds.Contains(process.Id);
                    var card = (Border)factory.CreateProcessNode(220, 110, process, victim, "proc_id_" + process.Id, 1);
                    card.Margin = new Thickness(0, 0, 15, 15);
                    visual.ApplyNodeVisualState(card, stateService.CreateResetNodeState(victim, inCycle));
                    card.Visibility.Should().Be(Visibility.Visible);
                    card.BorderThickness.Should().Be(new Thickness(3));
                    ((SolidColorBrush)card.BorderBrush).Color.Should().Be(victim ? Color.FromRgb(211, 47, 47) : Colors.DarkOrange);
                    row.Children.Add(card);
                }
                root.Measure(new Size(760, double.PositiveInfinity)); root.Arrange(new Rect(new Point(), root.DesiredSize)); root.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                string? output = Environment.GetEnvironmentVariable("SQLXML_IMP13_VISUAL_OUTPUT");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    var bitmap = new RenderTargetBitmap(760, (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(root);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(output, "deadlock-shared-state.png")); encoder.Save(stream);
                }
            }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
