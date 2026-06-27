using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace SqlXmlAnalyzer.Core.Services
{
    public sealed record DeadlockGraphProcessPlacement(
        DeadlockGraphProcessNode Process,
        string NodeId,
        Point Position,
        double Width,
        double Height,
        bool IsVictim);

    public sealed record DeadlockGraphResourcePlacement(
        DeadlockGraphResourceNode Resource,
        string NodeId,
        Point Position,
        double Width,
        double Height);

    public sealed record DeadlockGraphPlacementResult(
        IReadOnlyList<DeadlockGraphProcessPlacement> Processes,
        IReadOnlyList<DeadlockGraphResourcePlacement> Resources,
        Point TipPosition);

    public sealed class DeadlockGraphPlacementService
    {
        public const double ProcessWidth = 220;
        public const double ProcessHeight = 90;
        public const double ResourceWidth = 160;
        public const double ResourceHeight = 50;

        private const double DefaultCanvasWidth = 800;
        private const double DefaultCanvasHeight = 600;
        private const double MinimumRadius = 250;
        private const double RadiusNodeSpacing = 120;
        private const double RadiusYFactor = 0.8;

        public DeadlockGraphPlacementResult PlaceNodes(
            DeadlockGraphLayout layout,
            string? victimProcessId,
            double canvasWidth,
            double canvasHeight)
        {
            ArgumentNullException.ThrowIfNull(layout);

            double effectiveWidth = canvasWidth > 0 ? canvasWidth : DefaultCanvasWidth;
            double effectiveHeight = canvasHeight > 0 ? canvasHeight : DefaultCanvasHeight;
            double centerX = effectiveWidth / 2;
            double centerY = effectiveHeight / 2;

            int totalNodes = layout.Processes.Count + layout.Resources.Count;
            if (totalNodes == 0)
            {
                return new DeadlockGraphPlacementResult(
                    Array.Empty<DeadlockGraphProcessPlacement>(),
                    Array.Empty<DeadlockGraphResourcePlacement>(),
                    new Point(50, centerY + 60));
            }

            double dynamicRadius = Math.Max(MinimumRadius, (totalNodes * RadiusNodeSpacing) / (2 * Math.PI));
            double radiusX = dynamicRadius;
            double radiusY = dynamicRadius * RadiusYFactor;
            int nodeIndex = 0;

            var processPlacements = new List<DeadlockGraphProcessPlacement>(layout.Processes.Count);
            foreach (DeadlockGraphProcessNode process in layout.Processes)
            {
                Point position = CalculatePosition(
                    centerX,
                    centerY,
                    radiusX,
                    radiusY,
                    nodeIndex,
                    totalNodes,
                    ProcessWidth,
                    ProcessHeight);

                processPlacements.Add(new DeadlockGraphProcessPlacement(
                    process,
                    $"proc_id_{process.PrimaryId}",
                    position,
                    ProcessWidth,
                    ProcessHeight,
                    process.Threads.Any(thread => thread.Id == victimProcessId)));

                nodeIndex++;
            }

            var resourcePlacements = new List<DeadlockGraphResourcePlacement>(layout.Resources.Count);
            foreach (DeadlockGraphResourceNode resource in layout.Resources)
            {
                Point position = CalculatePosition(
                    centerX,
                    centerY,
                    radiusX,
                    radiusY,
                    nodeIndex,
                    totalNodes,
                    ResourceWidth,
                    ResourceHeight);

                resourcePlacements.Add(new DeadlockGraphResourcePlacement(
                    resource,
                    resource.Id,
                    position,
                    ResourceWidth,
                    ResourceHeight));

                nodeIndex++;
            }

            return new DeadlockGraphPlacementResult(
                processPlacements,
                resourcePlacements,
                new Point(50, centerY + radiusY + 60));
        }

        private static Point CalculatePosition(
            double centerX,
            double centerY,
            double radiusX,
            double radiusY,
            int nodeIndex,
            int totalNodes,
            double width,
            double height)
        {
            double angle = 2 * Math.PI * nodeIndex / totalNodes;
            return new Point(
                centerX + radiusX * Math.Cos(angle) - width / 2,
                centerY + radiusY * Math.Sin(angle) - height / 2);
        }
    }
}
