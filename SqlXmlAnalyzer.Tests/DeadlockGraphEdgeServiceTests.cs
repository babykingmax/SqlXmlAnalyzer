using System;
using System.Collections.Generic;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class DeadlockGraphEdgeServiceTests
    {
        [Fact]
        public void BuildEdges_WhenResourceHasWaitersAndOwners_ReturnsDirectionalEdges()
        {
            var resource = CreateResourceNode(
                CreateResource(
                    "keylock",
                    new[] { new LockOwner("owner1", "X") },
                    new[] { new LockWaiter("waiter1", "S", "wait") }));

            var service = new DeadlockGraphEdgeService();

            IReadOnlyList<DeadlockGraphEdge> edges = service.BuildEdges(new[] { resource });

            edges.Should().Equal(
                new DeadlockGraphEdge("proc_id_waiter1", "res_single_0", "Req: S", true),
                new DeadlockGraphEdge("res_single_0", "proc_id_owner1", "Own: X", false));
        }

        [Fact]
        public void BuildEdges_WhenWaiterModeIsEmpty_UsesRequestType()
        {
            var resource = CreateResourceNode(
                CreateResource(
                    "keylock",
                    Array.Empty<LockOwner>(),
                    new[] { new LockWaiter("waiter1", "", "convert") }));

            var service = new DeadlockGraphEdgeService();

            IReadOnlyList<DeadlockGraphEdge> edges = service.BuildEdges(new[] { resource });

            edges.Should().ContainSingle()
                .Which.Label.Should().Be("Req: convert");
        }

        [Fact]
        public void BuildEdges_WhenModeIsEmptyForExchangeEvent_UsesSyncLabel()
        {
            var resource = CreateResourceNode(
                CreateResource(
                    "exchangeEvent",
                    new[] { new LockOwner("owner1", "") },
                    new[] { new LockWaiter("waiter1", "", "") }));

            var service = new DeadlockGraphEdgeService();

            IReadOnlyList<DeadlockGraphEdge> edges = service.BuildEdges(new[] { resource });

            edges.Should().Equal(
                new DeadlockGraphEdge("proc_id_waiter1", "res_single_0", "Req: Sync", true),
                new DeadlockGraphEdge("res_single_0", "proc_id_owner1", "Own: Sync", false));
        }

        [Fact]
        public void BuildEdges_WhenModesAreEmpty_ReturnsPlainLabels()
        {
            var resource = CreateResourceNode(
                CreateResource(
                    "pagelock",
                    new[] { new LockOwner("owner1", "") },
                    new[] { new LockWaiter("waiter1", "", "") }));

            var service = new DeadlockGraphEdgeService();

            IReadOnlyList<DeadlockGraphEdge> edges = service.BuildEdges(new[] { resource });

            edges.Should().Equal(
                new DeadlockGraphEdge("proc_id_waiter1", "res_single_0", "Req", true),
                new DeadlockGraphEdge("res_single_0", "proc_id_owner1", "Own", false));
        }

        [Fact]
        public void BuildEdges_WhenResourcesAreEmpty_ReturnsNoEdges()
        {
            var service = new DeadlockGraphEdgeService();

            IReadOnlyList<DeadlockGraphEdge> edges = service.BuildEdges(Array.Empty<DeadlockGraphResourceNode>());

            edges.Should().BeEmpty();
        }

        [Fact]
        public void BuildEdges_WhenResourcesAreNull_Throws()
        {
            var service = new DeadlockGraphEdgeService();

            Action act = () => service.BuildEdges(null!);

            act.Should().Throw<ArgumentNullException>();
        }

        private static DeadlockGraphResourceNode CreateResourceNode(LockResource resource)
        {
            return new DeadlockGraphResourceNode
            {
                Id = "res_single_0",
                LockType = resource.LockType,
                ObjectName = resource.ObjectName,
                IndexName = resource.IndexName,
                LockCount = 1,
                RawResources = new[] { resource },
                OwnerSpids = resource.Owners.Select(owner => owner.Id).ToHashSet(StringComparer.Ordinal),
                WaiterSpids = resource.Waiters.Select(waiter => waiter.Id).ToHashSet(StringComparer.Ordinal)
            };
        }

        private static LockResource CreateResource(
            string lockType,
            IEnumerable<LockOwner> owners,
            IEnumerable<LockWaiter> waiters)
        {
            return new LockResource(
                lockType,
                "Sales.dbo.Orders",
                "IX_Orders",
                "hobt",
                "5",
                owners.ToList(),
                waiters.ToList());
        }
    }
}
