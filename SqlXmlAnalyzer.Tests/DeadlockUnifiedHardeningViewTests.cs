using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentAssertions;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Services;
using SqlXmlAnalyzer.ViewModels;

namespace SqlXmlAnalyzer.Tests;

public sealed class DeadlockUnifiedHardeningViewTests
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void EnableOffscreenEvidenceRendering()
    {
        // WPF suppresses rendering when the interactive session has no active display.
        // Enable its documented runtime switch only for opt-in test evidence capture.
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SQLXML_IMP13_VISUAL_OUTPUT")))
            AppContext.SetSwitch("Switch.System.Windows.Media.ShouldRenderEvenWhenNoDisplayDevicesAreAvailable", true);
    }

    [Fact]
    public void ProductionCanvas_TracksEveryParallelRelationshipThroughPlaybackDragResetAndRerender()
    {
        RunSta(() =>
        {
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            var analysis = new DeadlockAnalysisService().Analyze(DeadlockUnifiedHardeningTests.DuplicateRelations());
            var canvas = new Canvas { Width = 800, Height = 600, Background = Brushes.White };
            var ui = new DeadlockGraphUiState();
            var geometry = new DeadlockGraphGeometryService();
            var registry = new DeadlockGraphEdgeRegistryService();
            var elements = new DeadlockGraphElementUiActionService(new(), new(geometry), new(new(), new()),
                registry, geometry, canvas, new ListView(), new ListView(), ui);
            var renderer = new DeadlockGraphRenderUiActionService(new(), new(), new(), canvas, new Border(),
                new ScaleTransform(), new TranslateTransform(), ui, elements.DrawProcessNode, elements.DrawResourceNode,
                elements.DrawEdge, action => action(), () => { });
            var playback = new DeadlockPlaybackUiActionService(new(), new(), new(), new(), registry, canvas, new Control(), ui);
            var viewModel = new DeadlockPlaybackViewModel(analysis.Timeline) { FocusCriticalPath = false };
            playback.SetCurrentPlayback(analysis.Timeline, viewModel);
            renderer.Render(analysis.Graph);

            ui.ArrowCache.Should().HaveCount(5);
            ui.ArrowCache.Keys.Select(k => k.EvidenceId).Should().BeEquivalentTo(analysis.Graph.ResourceLinks.Select(l => l.LinkId));
            int children = canvas.Children.Count;
            foreach (var edge in ui.EdgesForDrawing.ToArray()) elements.DrawEdge(edge);
            canvas.Children.Count.Should().Be(children, "redrawing the same identity must not leave orphan visuals");
            playback.UpdateGraphVisibility(true);
            ui.ArrowCache.Values.Should().OnlyContain(e => e.Line.Opacity == 0.2 && e.ArrowHead.Opacity == 0.2 && e.Label.Opacity == 0.2);
            ui.StepBadges.Should().BeEmpty();

            var grants = analysis.Timeline.Events.Where(e => e.Type == "Grant" && e.ProcessId == "b").OrderBy(e => e.StepNumber).ToArray();
            grants.Should().HaveCount(2);
            var keys = grants.Select(e => ui.ArrowCache.Keys.Single(k => k.EvidenceId == e.EvidenceId)).ToArray();
            viewModel.CurrentStep = grants[0].StepNumber;
            playback.UpdateGraphVisibility(true);
            ui.ArrowCache[keys[0]].Line.Opacity.Should().Be(1);
            ui.ArrowCache[keys[1]].Line.Opacity.Should().Be(0.2);
            ui.StepBadges.Should().ContainKey(keys[0]).And.NotContainKey(keys[1]);

            viewModel.CurrentStep = grants[1].StepNumber;
            playback.UpdateGraphVisibility(true);
            for (int i = 0; i < keys.Length; i++)
                ((TextBlock)ui.StepBadges[keys[i]].Child).Text.Should().Be(grants[i].StepNumber.ToString());
            ui.StepBadges[keys[0]].Should().NotBeSameAs(ui.StepBadges[keys[1]]);
            // Correct clipping can put both midpoints at the same Y (or the same X).
            // Distinct parallel relationships need distinct 2D positions, not distinct Y.
            Position(ui.StepBadges[keys[0]]).Should().NotBe(Position(ui.StepBadges[keys[1]]));
            Position(ui.ArrowCache[keys[0]].Label).Should().NotBe(Position(ui.ArrowCache[keys[1]].Label));
            var old = keys.Select(k => ui.ArrowCache[k].Line.X2).ToArray();
            ui.NodePositions["proc_id_b"] += new Vector(55, 40);
            elements.UpdateConnectionsForNode("proc_id_b");
            for (int i = 0; i < keys.Length; i++)
            {
                var offset = ui.EdgesForDrawing.Single(e => e.Key == keys[i]).ParallelOffset;
                var points = geometry.CalculateConnectionPoints(ui.NodePositions, keys[i].FromId, keys[i].ToId, offset);
                var visuals = ui.ArrowCache[keys[i]];
                visuals.Line.X2.Should().Be(points.To.X).And.NotBe(old[i]);
                visuals.ArrowHead.Points[0].Should().Be(points.To);
                var badge = new DeadlockStepBadgeService().PlaceBadge(grants[i].StepNumber, points.From.X, points.From.Y, points.To.X, points.To.Y);
                Canvas.GetLeft(ui.StepBadges[keys[i]]).Should().Be(badge.Left);
                Canvas.GetTop(ui.StepBadges[keys[i]]).Should().Be(badge.Top);
            }
            playback.HidePlayback();
            ui.ArrowCache.Values.Should().OnlyContain(e => e.Line.Opacity == 1 && e.Label.Visibility == Visibility.Visible);
            ui.StepBadges.Values.Should().OnlyContain(b => b.Visibility == Visibility.Collapsed);

            renderer.Render(analysis.Graph);
            ui.StepBadges.Should().BeEmpty("rerender must not reuse badges removed from the canvas");
            viewModel.CurrentStep = analysis.Timeline.Events.Count;
            playback.UpdateGraphVisibility(true);
            ui.StepBadges.Should().HaveCount(5);
            foreach (var badge in ui.StepBadges.Values) canvas.Children.Contains(badge).Should().BeTrue();
            foreach (var edge in ui.ArrowCache.Values)
            {
                canvas.Children.Contains(edge.Line).Should().BeTrue();
                canvas.Children.Contains(edge.ArrowHead).Should().BeTrue();
                canvas.Children.Contains(edge.Label).Should().BeTrue();
            }
            SaveCanvas(canvas, "deadlock-parallel-relations.png");
            viewModel.CurrentStep = 0;
            playback.UpdateGraphVisibility(true);
            ui.ArrowCache.Values.Should().OnlyContain(e => e.Line.Opacity == 0.2);
            ui.StepBadges.Values.Should().OnlyContain(b => b.Visibility == Visibility.Collapsed);
            SaveCanvas(canvas, "deadlock-parallel-relations-step0.png");
        });
    }

    private static void SaveCanvas(Canvas canvas, string filename)
    {
        string? output = Environment.GetEnvironmentVariable("SQLXML_IMP13_VISUAL_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        canvas.Measure(new Size(800, 600)); canvas.Arrange(new Rect(0, 0, 800, 600)); canvas.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        var bitmap = new RenderTargetBitmap(800, 600, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var pixels = new byte[800 * 600 * 4];
        bitmap.CopyPixels(pixels, 800 * 4, 0);
        pixels.Where((_, index) => index % 4 == 3).Count(alpha => alpha > 0).Should().BeGreaterThan(1000,
            "a transparent bitmap is not valid WPF visual verification");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(output);
        using var stream = File.Create(Path.Combine(output, filename)); encoder.Save(stream);
    }

    private static Point Position(FrameworkElement element) => new(Canvas.GetLeft(element), Canvas.GetTop(element));

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
