using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class DeadlockGraphRenderUiActionService
    {
        private readonly Core.Services.DeadlockGraphLayoutService _layoutService;
        private readonly Core.Services.DeadlockGraphPlacementService _placementService;
        private readonly Core.Services.DeadlockGraphEdgeService _edgeService;
        private readonly Canvas _graphCanvas;
        private readonly Border _canvasBorder;
        private readonly ScaleTransform _scaleTransform;
        private readonly TranslateTransform _translateTransform;
        private readonly DeadlockGraphUiState _graphState;
        private readonly Action<Core.Services.DeadlockGraphProcessPlacement> _drawProcessNode;
        private readonly Action<Core.Services.DeadlockGraphResourcePlacement> _drawResourceNode;
        private readonly Action<Core.Services.DeadlockGraphEdge> _drawEdge;
        private readonly Action<Action> _invokeWhenLoaded;
        private readonly Action _zoomToFit;

        public DeadlockGraphRenderUiActionService(
            Core.Services.DeadlockGraphLayoutService layoutService,
            Core.Services.DeadlockGraphPlacementService placementService,
            Core.Services.DeadlockGraphEdgeService edgeService,
            Canvas graphCanvas,
            Border canvasBorder,
            ScaleTransform scaleTransform,
            TranslateTransform translateTransform,
            DeadlockGraphUiState graphState,
            Action<Core.Services.DeadlockGraphProcessPlacement> drawProcessNode,
            Action<Core.Services.DeadlockGraphResourcePlacement> drawResourceNode,
            Action<Core.Services.DeadlockGraphEdge> drawEdge,
            Action<Action> invokeWhenLoaded,
            Action zoomToFit)
        {
            _layoutService = layoutService ?? throw new ArgumentNullException(nameof(layoutService));
            _placementService = placementService ?? throw new ArgumentNullException(nameof(placementService));
            _edgeService = edgeService ?? throw new ArgumentNullException(nameof(edgeService));
            _graphCanvas = graphCanvas ?? throw new ArgumentNullException(nameof(graphCanvas));
            _canvasBorder = canvasBorder ?? throw new ArgumentNullException(nameof(canvasBorder));
            _scaleTransform = scaleTransform ?? throw new ArgumentNullException(nameof(scaleTransform));
            _translateTransform = translateTransform ?? throw new ArgumentNullException(nameof(translateTransform));
            _graphState = graphState ?? throw new ArgumentNullException(nameof(graphState));
            _drawProcessNode = drawProcessNode
                ?? throw new ArgumentNullException(nameof(drawProcessNode));
            _drawResourceNode = drawResourceNode
                ?? throw new ArgumentNullException(nameof(drawResourceNode));
            _drawEdge = drawEdge ?? throw new ArgumentNullException(nameof(drawEdge));
            _invokeWhenLoaded = invokeWhenLoaded
                ?? throw new ArgumentNullException(nameof(invokeWhenLoaded));
            _zoomToFit = zoomToFit ?? throw new ArgumentNullException(nameof(zoomToFit));
        }

        public void RenderAndZoomToFit(DeadlockGraph graph)
        {
            Render(graph);
            _invokeWhenLoaded(_zoomToFit);
        }

        public void Render(DeadlockGraph graph)
        {
            ClearGraphState();

            Core.Services.DeadlockGraphLayout layout = _layoutService.BuildLayout(graph);
            foreach (KeyValuePair<string, (string LockType, string ObjectName)> detail in layout.ResourceGroupDetails)
            {
                _graphState.ResourceGroupDetails[detail.Key] = detail.Value;
            }

            if (layout.Processes.Count == 0)
            {
                _graphCanvas.Children.Add(CreateNoDataMessage());
                return;
            }

            ResetViewport();

            Core.Services.DeadlockGraphPlacementResult placement =
                _placementService.PlaceNodes(
                    layout,
                    graph.VictimProcessId,
                    GetCanvasWidth(),
                    GetCanvasHeight());

            foreach (Core.Services.DeadlockGraphProcessPlacement processPlacement in placement.Processes)
            {
                _drawProcessNode(processPlacement);
            }

            foreach (Core.Services.DeadlockGraphResourcePlacement resourcePlacement in placement.Resources)
            {
                _drawResourceNode(resourcePlacement);
            }

            foreach (Core.Services.DeadlockGraphEdge edge in _edgeService.BuildEdges(layout.Resources))
            {
                _drawEdge(edge);
            }

            AddGraphTip(placement.TipPosition);
        }

        private void ClearGraphState()
        {
            _graphCanvas.Children.Clear();
            _graphState.NodePositions.Clear();
            _graphState.NodeElements.Clear();
            _graphState.EdgesForDrawing.Clear();
            _graphState.ArrowCache.Clear();
            _graphState.ResourceGroupDetails.Clear();
        }

        private void ResetViewport()
        {
            _scaleTransform.ScaleX = 1.0;
            _scaleTransform.ScaleY = 1.0;
            _translateTransform.X = 0;
            _translateTransform.Y = 0;
        }

        private double GetCanvasWidth()
        {
            return _canvasBorder.ActualWidth > 0 ? _canvasBorder.ActualWidth : 800;
        }

        private double GetCanvasHeight()
        {
            return _canvasBorder.ActualHeight > 0 ? _canvasBorder.ActualHeight : 600;
        }

        private static TextBlock CreateNoDataMessage()
        {
            return new TextBlock
            {
                Text = "No valid deadlock process data",
                Margin = new Thickness(20),
                FontSize = 12,
                Foreground = Brushes.Gray
            };
        }

        private void AddGraphTip(Point tipPosition)
        {
            var tip = new TextBlock
            {
                Text = "Full graph mode: all parallel threads and lock resources are shown.",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.SlateGray,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Canvas.SetLeft(tip, tipPosition.X);
            Canvas.SetTop(tip, tipPosition.Y);
            _graphCanvas.Children.Add(tip);
        }
    }
}
