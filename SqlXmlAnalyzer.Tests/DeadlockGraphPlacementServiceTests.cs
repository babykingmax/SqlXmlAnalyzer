using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class DeadlockGraphPlacementServiceTests
    {
        [Fact]
        public void PlaceNodes_WhenLayoutHasProcessesAndResources_ReturnsCircularPlacements()
        {
            DeadlockGraphLayout layout = CreateLayout(
                new[]
                {
                    CreateProcessNode("process1"),
                    CreateProcessNode("process2")
                },
                new[] { CreateResourceNode("res_single_0") });
            var service = new DeadlockGraphPlacementService();

            DeadlockGraphPlacementResult result = service.PlaceNodes(
                layout,
                "process2",
                800,
                600);

            result.Processes.Should().HaveCount(2);
            result.Resources.Should().HaveCount(1);

            result.Processes[0].NodeId.Should().Be("proc_id_process1");
            result.Processes[0].Position.X.Should().BeApproximately(540, 0.0001);
            result.Processes[0].Position.Y.Should().BeApproximately(255, 0.0001);
            result.Processes[0].IsVictim.Should().BeFalse();

            result.Processes[1].NodeId.Should().Be("proc_id_process2");
            result.Processes[1].Position.X.Should().BeApproximately(165, 0.0001);
            result.Processes[1].Position.Y.Should().BeApproximately(428.2051, 0.0001);
            result.Processes[1].IsVictim.Should().BeTrue();

            result.Resources[0].NodeId.Should().Be("res_single_0");
            result.Resources[0].Position.X.Should().BeApproximately(195, 0.0001);
            result.Resources[0].Position.Y.Should().BeApproximately(101.7949, 0.0001);
            result.TipPosition.Should().Be(new System.Windows.Point(50, 560));
        }

        [Fact]
        public void PlaceNodes_WhenCanvasSizeIsInvalid_UsesDefaultCanvasSize()
        {
            DeadlockGraphLayout layout = CreateLayout(
                new[] { CreateProcessNode("process1") },
                Array.Empty<DeadlockGraphResourceNode>());
            var service = new DeadlockGraphPlacementService();

            DeadlockGraphPlacementResult result = service.PlaceNodes(
                layout,
                null,
                0,
                -1);

            result.Processes.Should().ContainSingle();
            result.Processes[0].Position.X.Should().BeApproximately(540, 0.0001);
            result.Processes[0].Position.Y.Should().BeApproximately(255, 0.0001);
            result.TipPosition.Should().Be(new System.Windows.Point(50, 560));
        }

        [Fact]
        public void PlaceNodes_WhenManyNodesArePresent_ExpandsRadius()
        {
            var processes = Enumerable.Range(1, 20)
                .Select(index => CreateProcessNode($"process{index}"))
                .ToArray();
            DeadlockGraphLayout layout = CreateLayout(
                processes,
                Array.Empty<DeadlockGraphResourceNode>());
            var service = new DeadlockGraphPlacementService();

            DeadlockGraphPlacementResult result = service.PlaceNodes(
                layout,
                null,
                800,
                600);

            result.Processes[0].Position.X.Should().BeGreaterThan(670);
            result.TipPosition.Y.Should().BeGreaterThan(660);
        }

        [Fact]
        public void PlaceNodes_WhenLayoutIsEmpty_ReturnsEmptyPlacement()
        {
            DeadlockGraphLayout layout = CreateLayout(
                Array.Empty<DeadlockGraphProcessNode>(),
                Array.Empty<DeadlockGraphResourceNode>());
            var service = new DeadlockGraphPlacementService();

            DeadlockGraphPlacementResult result = service.PlaceNodes(
                layout,
                null,
                1000,
                500);

            result.Processes.Should().BeEmpty();
            result.Resources.Should().BeEmpty();
            result.TipPosition.Should().Be(new System.Windows.Point(50, 310));
        }

        [Fact]
        public void PlaceNodes_WhenLayoutIsNull_Throws()
        {
            var service = new DeadlockGraphPlacementService();

            Action act = () => service.PlaceNodes(null!, null, 800, 600);

            act.Should().Throw<ArgumentNullException>();
        }

        private static DeadlockGraphLayout CreateLayout(
            IReadOnlyList<DeadlockGraphProcessNode> processes,
            IReadOnlyList<DeadlockGraphResourceNode> resources)
        {
            return new DeadlockGraphLayout(
                processes,
                resources,
                new Dictionary<string, (string LockType, string ObjectName)>());
        }

        private static DeadlockGraphProcessNode CreateProcessNode(string id)
        {
            DeadlockProcess process = new(
                id,
                "57",
                "login",
                "host",
                "read committed",
                "suspended",
                "SELECT 1",
                new List<ExecutionFrame>());

            return new DeadlockGraphProcessNode
            {
                Spid = process.Spid,
                PrimaryId = id,
                ThreadCount = 1,
                Threads = new[] { process }
            };
        }

        private static DeadlockGraphResourceNode CreateResourceNode(string id)
        {
            LockResource resource = new(
                "keylock",
                "Sales.dbo.Orders",
                "IX_Orders",
                "hobt",
                "5",
                new List<LockOwner>(),
                new List<LockWaiter>());

            return new DeadlockGraphResourceNode
            {
                Id = id,
                LockType = resource.LockType,
                ObjectName = resource.ObjectName,
                IndexName = resource.IndexName,
                LockCount = 1,
                RawResources = new[] { resource },
                OwnerSpids = new HashSet<string>(),
                WaiterSpids = new HashSet<string>()
            };
        }
    }
}
