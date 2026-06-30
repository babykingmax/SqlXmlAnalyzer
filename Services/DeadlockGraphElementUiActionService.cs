using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class DeadlockGraphElementUiActionService
    {
        private readonly DeadlockGraphNodeElementFactory _nodeElementFactory;
        private readonly DeadlockGraphEdgeElementFactory _edgeElementFactory;
        private readonly DeadlockNodeInteractionBinder _nodeInteractionBinder;
        private readonly Core.Services.DeadlockGraphEdgeRegistryService _edgeRegistryService;
        private readonly Core.Services.DeadlockGraphGeometryService _geometryService;
        private readonly Canvas _graphCanvas;
        private readonly ListView _processesList;
        private readonly ListView _resourcesList;
        private readonly DeadlockGraphUiState _graphState;

        public DeadlockGraphElementUiActionService(
            DeadlockGraphNodeElementFactory nodeElementFactory,
            DeadlockGraphEdgeElementFactory edgeElementFactory,
            DeadlockNodeInteractionBinder nodeInteractionBinder,
            Core.Services.DeadlockGraphEdgeRegistryService edgeRegistryService,
            Core.Services.DeadlockGraphGeometryService geometryService,
            Canvas graphCanvas,
            ListView processesList,
            ListView resourcesList,
            DeadlockGraphUiState graphState)
        {
            _nodeElementFactory = nodeElementFactory
                ?? throw new ArgumentNullException(nameof(nodeElementFactory));
            _edgeElementFactory = edgeElementFactory
                ?? throw new ArgumentNullException(nameof(edgeElementFactory));
            _nodeInteractionBinder = nodeInteractionBinder
                ?? throw new ArgumentNullException(nameof(nodeInteractionBinder));
            _edgeRegistryService = edgeRegistryService
                ?? throw new ArgumentNullException(nameof(edgeRegistryService));
            _geometryService = geometryService
                ?? throw new ArgumentNullException(nameof(geometryService));
            _graphCanvas = graphCanvas ?? throw new ArgumentNullException(nameof(graphCanvas));
            _processesList = processesList ?? throw new ArgumentNullException(nameof(processesList));
            _resourcesList = resourcesList ?? throw new ArgumentNullException(nameof(resourcesList));
            _graphState = graphState ?? throw new ArgumentNullException(nameof(graphState));
        }

        public void DrawProcessNode(Core.Services.DeadlockGraphProcessPlacement placement)
        {
            FrameworkElement card = _nodeElementFactory.CreateProcessNode(
                placement.Width,
                placement.Height,
                placement.Process.PrimaryProcess,
                placement.IsVictim,
                placement.NodeId,
                placement.Process.ThreadCount);

            AddNode(card, placement.NodeId, placement.Position.X, placement.Position.Y);
        }

        public void DrawResourceNode(Core.Services.DeadlockGraphResourcePlacement placement)
        {
            FrameworkElement container = _nodeElementFactory.CreateResourceNode(
                placement.Width,
                placement.Height,
                placement.Resource.RawResources.First(),
                placement.NodeId,
                placement.Resource.LockCount);

            AddNode(container, placement.NodeId, placement.Position.X, placement.Position.Y);
        }

        public void DrawEdge(Core.Services.DeadlockGraphEdge edge)
        {
            Core.Services.DeadlockConnectionPoints points =
                _geometryService.CalculateConnectionPoints(
                    _graphState.NodePositions,
                    edge.FromId,
                    edge.ToId);

            DeadlockGraphEdgeElements elements =
                _edgeElementFactory.CreateEdge(
                    points,
                    edge.Label,
                    edge.IsWaitEdge);

            _graphCanvas.Children.Add(elements.Line);
            _graphCanvas.Children.Add(elements.ArrowHead);
            _graphCanvas.Children.Add(elements.Label);

            _graphState.ArrowCache[(edge.FromId, edge.ToId)] = elements;
            _graphState.EdgesForDrawing.Add(edge);
        }

        private void AddNode(
            FrameworkElement element,
            string nodeId,
            double x,
            double y)
        {
            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);
            _graphCanvas.Children.Add(element);

            _graphState.NodeElements[nodeId] = element;
            _graphState.NodePositions[nodeId] = new Point(x, y);

            _nodeInteractionBinder.Attach(
                element,
                nodeId,
                _graphCanvas,
                _graphState.NodePositions,
                _graphState.ResourceGroupDetails,
                _processesList,
                _resourcesList,
                UpdateConnectionsForNode);
        }

        private void UpdateConnectionsForNode(string movedId)
        {
            IReadOnlyList<Core.Services.DeadlockGraphEdge> edgesToUpdate =
                _edgeRegistryService.FindEdgesForNode(_graphState.EdgesForDrawing, movedId);

            foreach (Core.Services.DeadlockGraphEdge edge in edgesToUpdate)
            {
                var key = (edge.FromId, edge.ToId);
                if (_graphState.ArrowCache.TryGetValue(key, out DeadlockGraphEdgeElements? cached))
                {
                    Core.Services.DeadlockConnectionPoints points =
                        _geometryService.CalculateConnectionPoints(
                            _graphState.NodePositions,
                            edge.FromId,
                            edge.ToId);
                    _edgeElementFactory.UpdateEdge(cached, points);
                }
            }
        }
    }
}
