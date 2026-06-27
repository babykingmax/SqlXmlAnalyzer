using System;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class DeadlockGraphEdgeRegistryServiceTests
    {
        [Fact]
        public void FindEdgesForNode_ReturnsEdgesWhereNodeIsSourceOrTarget()
        {
            var service = new DeadlockGraphEdgeRegistryService();
            var edges = new[]
            {
                new DeadlockGraphEdge("proc_id_1", "res_single_0", "Req: S", true),
                new DeadlockGraphEdge("res_single_0", "proc_id_2", "Own: X", false),
                new DeadlockGraphEdge("proc_id_3", "res_single_1", "Req: U", true)
            };

            var result = service.FindEdgesForNode(edges, "res_single_0");

            result.Should().Equal(edges[0], edges[1]);
        }

        [Fact]
        public void IsWaitEdge_WhenEdgeExists_ReturnsStoredDirectionType()
        {
            var service = new DeadlockGraphEdgeRegistryService();
            var edges = new[]
            {
                new DeadlockGraphEdge("proc_id_1", "res_single_0", "Req: S", true),
                new DeadlockGraphEdge("res_single_0", "proc_id_2", "Own: X", false)
            };

            service.IsWaitEdge(edges, "proc_id_1", "res_single_0").Should().BeTrue();
            service.IsWaitEdge(edges, "res_single_0", "proc_id_2").Should().BeFalse();
        }

        [Fact]
        public void IsWaitEdge_WhenEdgeIsUnknown_DefaultsToWaitEdge()
        {
            var service = new DeadlockGraphEdgeRegistryService();

            bool result = service.IsWaitEdge(
                Array.Empty<DeadlockGraphEdge>(),
                "missing_from",
                "missing_to");

            result.Should().BeTrue();
        }

        [Fact]
        public void Methods_WhenArgumentsAreNull_Throw()
        {
            var service = new DeadlockGraphEdgeRegistryService();

            Action nullEdgesForFind = () => service.FindEdgesForNode(null!, "node");
            Action nullNodeId = () => service.FindEdgesForNode(Array.Empty<DeadlockGraphEdge>(), null!);
            Action nullEdgesForLookup = () => service.IsWaitEdge(null!, "from", "to");
            Action nullFromId = () => service.IsWaitEdge(Array.Empty<DeadlockGraphEdge>(), null!, "to");
            Action nullToId = () => service.IsWaitEdge(Array.Empty<DeadlockGraphEdge>(), "from", null!);

            nullEdgesForFind.Should().Throw<ArgumentNullException>();
            nullNodeId.Should().Throw<ArgumentNullException>();
            nullEdgesForLookup.Should().Throw<ArgumentNullException>();
            nullFromId.Should().Throw<ArgumentNullException>();
            nullToId.Should().Throw<ArgumentNullException>();
        }
    }
}
