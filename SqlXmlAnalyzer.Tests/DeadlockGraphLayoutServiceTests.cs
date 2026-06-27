using System;
using System.Collections.Generic;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class DeadlockGraphLayoutServiceTests
    {
        [Fact]
        public void BuildLayout_WhenGraphHasProcessesAndResources_ReturnsDrawableNodes()
        {
            var graph = new DeadlockGraph
            {
                VictimProcessId = "process1"
            };
            graph.Processes.AddRange(new[]
            {
                CreateProcess("process1", "57", "0"),
                CreateProcess("process2", "58", "0")
            });
            graph.Resources.Add(CreateResource(
                "keylock",
                "Sales.dbo.Orders",
                "IX_Orders",
                new[] { new LockOwner("process2", "X") },
                new[] { new LockWaiter("process1", "S", "wait") }));

            var service = new DeadlockGraphLayoutService();

            DeadlockGraphLayout layout = service.BuildLayout(graph);

            layout.Processes.Should().HaveCount(2);
            layout.Processes[0].PrimaryId.Should().Be("process1");
            layout.Processes[0].PrimaryProcess.Spid.Should().Be("57");
            layout.Processes[0].ThreadCount.Should().Be(1);

            layout.Resources.Should().HaveCount(1);
            DeadlockGraphResourceNode resourceNode = layout.Resources[0];
            resourceNode.Id.Should().Be("res_single_0");
            resourceNode.LockType.Should().Be("keylock");
            resourceNode.ObjectName.Should().Be("Sales.dbo.Orders");
            resourceNode.OwnerSpids.Should().Equal("process2");
            resourceNode.WaiterSpids.Should().Equal("process1");
            resourceNode.OwnerModes.Should().Be("X");
            resourceNode.WaiterModes.Should().Be("S");
            resourceNode.Dbid.Should().Be("5");

            layout.ResourceGroupDetails.Should().Contain(
                "res_single_0",
                ("keylock", "Sales.dbo.Orders"));
        }

        [Fact]
        public void BuildLayout_WhenProcessIdsRepeat_KeepsFirstDistinctProcess()
        {
            var graph = new DeadlockGraph();
            graph.Processes.Add(CreateProcess("process1", "57", "1"));
            graph.Processes.Add(CreateProcess("process1", "57", "0"));
            graph.Processes.Add(CreateProcess("process2", "58", "0"));

            var service = new DeadlockGraphLayoutService();

            DeadlockGraphLayout layout = service.BuildLayout(graph);

            layout.Processes.Should().HaveCount(2);
            layout.Processes[0].PrimaryProcess.Ecid.Should().Be("1");
            layout.Processes[0].ThreadCount.Should().Be(1);
        }

        [Fact]
        public void BuildLayout_WhenMultipleResources_PreservesIndividualResourceNodes()
        {
            var graph = new DeadlockGraph();
            graph.Resources.Add(CreateResource(
                "pagelock",
                "Sales.dbo.OrderLines",
                "IX_OrderLines",
                Array.Empty<LockOwner>(),
                Array.Empty<LockWaiter>()));
            graph.Resources.Add(CreateResource(
                "objectlock",
                "Sales.dbo.Customers",
                "PK_Customers",
                Array.Empty<LockOwner>(),
                Array.Empty<LockWaiter>()));

            var service = new DeadlockGraphLayoutService();

            DeadlockGraphLayout layout = service.BuildLayout(graph);

            layout.Resources.Select(resource => resource.Id)
                .Should()
                .Equal("res_single_0", "res_single_1");
            layout.ResourceGroupDetails["res_single_1"]
                .Should()
                .Be(("objectlock", "Sales.dbo.Customers"));
        }

        [Fact]
        public void BuildLayout_WhenGraphIsEmpty_ReturnsEmptyLayout()
        {
            var service = new DeadlockGraphLayoutService();

            DeadlockGraphLayout layout = service.BuildLayout(new DeadlockGraph());

            layout.Processes.Should().BeEmpty();
            layout.Resources.Should().BeEmpty();
            layout.ResourceGroupDetails.Should().BeEmpty();
        }

        [Fact]
        public void BuildLayout_WhenGraphIsNull_Throws()
        {
            var service = new DeadlockGraphLayoutService();

            Action act = () => service.BuildLayout(null!);

            act.Should().Throw<ArgumentNullException>();
        }

        private static DeadlockProcess CreateProcess(string id, string spid, string ecid)
        {
            return new DeadlockProcess(
                id,
                spid,
                "login",
                "host",
                "read committed",
                "suspended",
                "SELECT 1",
                new List<ExecutionFrame>(),
                Ecid: ecid);
        }

        private static LockResource CreateResource(
            string lockType,
            string objectName,
            string indexName,
            IEnumerable<LockOwner> owners,
            IEnumerable<LockWaiter> waiters)
        {
            return new LockResource(
                lockType,
                objectName,
                indexName,
                "hobt",
                "5",
                owners.ToList(),
                waiters.ToList());
        }
    }
}
