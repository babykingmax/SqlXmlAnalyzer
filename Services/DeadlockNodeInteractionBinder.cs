using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SqlXmlAnalyzer.Core;

namespace SqlXmlAnalyzer.Services
{
    internal sealed class DeadlockNodeInteractionBinder
    {
        private readonly Core.Services.DeadlockNodeDragService _dragService;
        private readonly Core.Services.DeadlockGraphSelectionService _selectionService;

        public DeadlockNodeInteractionBinder(
            Core.Services.DeadlockNodeDragService dragService,
            Core.Services.DeadlockGraphSelectionService selectionService)
        {
            _dragService = dragService
                ?? throw new ArgumentNullException(nameof(dragService));
            _selectionService = selectionService
                ?? throw new ArgumentNullException(nameof(selectionService));
        }

        public void Attach(
            FrameworkElement element,
            string nodeId,
            Canvas canvas,
            IDictionary<string, Point> nodePositions,
            IReadOnlyDictionary<string, (string LockType, string ObjectName)> resourceGroupDetails,
            ListView processesList,
            ListView resourcesList,
            Action<string> updateConnections)
        {
            ArgumentNullException.ThrowIfNull(element);
            ArgumentNullException.ThrowIfNull(nodeId);
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(nodePositions);
            ArgumentNullException.ThrowIfNull(resourceGroupDetails);
            ArgumentNullException.ThrowIfNull(processesList);
            ArgumentNullException.ThrowIfNull(resourcesList);
            ArgumentNullException.ThrowIfNull(updateConnections);

            bool isDragging = false;
            Point lastPosition = default;

            element.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                {
                    SyncSelection(
                        nodeId,
                        resourceGroupDetails,
                        processesList,
                        resourcesList);
                    e.Handled = true;
                    return;
                }

                isDragging = true;
                lastPosition = e.GetPosition(canvas);
                element.CaptureMouse();
                e.Handled = true;
            };

            element.MouseMove += (_, e) =>
            {
                if (!isDragging)
                {
                    return;
                }

                Point currentPosition = e.GetPosition(canvas);
                Point currentNodePosition =
                    _dragService.NormalizeCanvasPosition(
                        Canvas.GetLeft(element),
                        Canvas.GetTop(element));
                Core.Services.DeadlockNodeDragResult dragResult =
                    _dragService.Drag(
                        currentNodePosition,
                        lastPosition,
                        currentPosition);

                Canvas.SetLeft(element, dragResult.Position.X);
                Canvas.SetTop(element, dragResult.Position.Y);
                nodePositions[nodeId] = dragResult.Position;
                lastPosition = dragResult.LastPointer;

                updateConnections(nodeId);
            };

            element.MouseLeftButtonUp += (_, e) =>
            {
                if (!isDragging)
                {
                    return;
                }

                isDragging = false;
                element.ReleaseMouseCapture();
                e.Handled = true;
            };
        }

        private void SyncSelection(
            string nodeId,
            IReadOnlyDictionary<string, (string LockType, string ObjectName)> resourceGroupDetails,
            ListView processesList,
            ListView resourcesList)
        {
            LockResource? resource = _selectionService.FindResourceForNode(
                nodeId,
                resourceGroupDetails,
                resourcesList.ItemsSource?.Cast<LockResource>());
            if (resource != null)
            {
                resourcesList.SelectedItem = resource;
                resourcesList.ScrollIntoView(resource);
            }

            DeadlockProcess? process = _selectionService.FindProcessForNode(
                nodeId,
                processesList.ItemsSource?.Cast<DeadlockProcess>());
            if (process != null)
            {
                processesList.SelectedItem = process;
                processesList.ScrollIntoView(process);
            }
        }
    }
}
