using System;
using System.Collections.Generic;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class DeadlockGraphSelectionServiceTests
    {
        [Fact]
        public void FindResourceForNode_WhenResourceNodeMatches_ReturnsResource()
        {
            var service = new DeadlockGraphSelectionService();
            var resource = CreateResource("keylock", "Sales.dbo.Orders");
            var resources = new[] { CreateResource("pagelock", "Other"), resource };
            var details = new Dictionary<string, (string LockType, string ObjectName)>
            {
                ["res_single_1"] = ("keylock", "Sales.dbo.Orders")
            };

            LockResource? result = service.FindResourceForNode(
                "res_single_1",
                details,
                resources);

            result.Should().BeSameAs(resource);
        }

        [Fact]
        public void FindResourceForNode_WhenNodeIsNotResource_ReturnsNull()
        {
            var service = new DeadlockGraphSelectionService();
            var details = new Dictionary<string, (string LockType, string ObjectName)>();

            LockResource? result = service.FindResourceForNode(
                "proc_id_process1",
                details,
                new[] { CreateResource("keylock", "Sales.dbo.Orders") });

            result.Should().BeNull();
        }

        [Fact]
        public void FindProcessForNode_WhenProcessNodeMatches_ReturnsProcess()
        {
            var service = new DeadlockGraphSelectionService();
            var process = CreateProcess("process2");
            var processes = new[] { CreateProcess("process1"), process };

            DeadlockProcess? result = service.FindProcessForNode(
                "proc_id_process2",
                processes);

            result.Should().BeSameAs(process);
        }

        [Fact]
        public void FindProcessForNode_WhenNodeIsNotProcess_ReturnsNull()
        {
            var service = new DeadlockGraphSelectionService();

            DeadlockProcess? result = service.FindProcessForNode(
                "res_single_1",
                new[] { CreateProcess("process1") });

            result.Should().BeNull();
        }

        [Fact]
        public void FindResourceForNode_WhenNodeIdIsNull_Throws()
        {
            var service = new DeadlockGraphSelectionService();

            Action act = () => service.FindResourceForNode(
                null!,
                new Dictionary<string, (string LockType, string ObjectName)>(),
                Array.Empty<LockResource>());

            act.Should().Throw<ArgumentNullException>();
        }

        private static LockResource CreateResource(string lockType, string objectName)
        {
            return new LockResource(
                lockType,
                objectName,
                "IX_Test",
                "hobt",
                "5",
                new List<LockOwner>(),
                new List<LockWaiter>());
        }

        private static DeadlockProcess CreateProcess(string id)
        {
            return new DeadlockProcess(
                id,
                "57",
                "login",
                "host",
                "read committed",
                "suspended",
                "SELECT 1",
                new List<ExecutionFrame>());
        }
    }
}
