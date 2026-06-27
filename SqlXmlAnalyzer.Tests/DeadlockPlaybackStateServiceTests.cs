using System;
using System.Collections.Generic;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Parsers;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Tests
{
    public sealed class DeadlockPlaybackStateServiceTests
    {
        [Fact]
        public void BuildState_WhenEventsAreWithinCurrentStep_MarksNodesAndEdgesActive()
        {
            DeadlockTimelineParser.ParsedDeadlock timeline = CreateTimeline();
            var service = new DeadlockPlaybackStateService();

            DeadlockPlaybackGraphState state = service.BuildState(
                timeline,
                currentStep: 2,
                focusCriticalPath: false,
                new[] { "proc_id_process1", "proc_id_process2", "res_single_0" },
                new[]
                {
                    new DeadlockPlaybackEdgeKey("res_single_0", "proc_id_process2"),
                    new DeadlockPlaybackEdgeKey("proc_id_process1", "res_single_0")
                });

            state.Nodes["proc_id_process1"].IsActive.Should().BeTrue();
            state.Nodes["proc_id_process2"].IsActive.Should().BeTrue();
            state.Nodes["res_single_0"].IsActive.Should().BeTrue();

            state.Edges[new DeadlockPlaybackEdgeKey("res_single_0", "proc_id_process2")]
                .BadgeStepNumber.Should().Be(1);
            state.Edges[new DeadlockPlaybackEdgeKey("proc_id_process1", "res_single_0")]
                .BadgeStepNumber.Should().Be(2);
            state.Nodes["proc_id_process1"].IsVictim.Should().BeTrue();
            state.Nodes["proc_id_process1"].IsVictimRevealed.Should().BeFalse();
        }

        [Fact]
        public void BuildState_WhenVictimStepHasPassed_RevealsVictim()
        {
            DeadlockTimelineParser.ParsedDeadlock timeline = CreateTimeline();
            var service = new DeadlockPlaybackStateService();

            DeadlockPlaybackGraphState state = service.BuildState(
                timeline,
                currentStep: 3,
                focusCriticalPath: false,
                new[] { "proc_id_process1" },
                Array.Empty<DeadlockPlaybackEdgeKey>());

            state.Nodes["proc_id_process1"].IsVictimRevealed.Should().BeTrue();
        }

        [Fact]
        public void BuildState_WhenCurrentStepIsBeforeEvent_MarksFutureNodeAndEdgeInactive()
        {
            DeadlockTimelineParser.ParsedDeadlock timeline = CreateTimeline();
            var service = new DeadlockPlaybackStateService();

            DeadlockPlaybackGraphState state = service.BuildState(
                timeline,
                currentStep: 1,
                focusCriticalPath: false,
                new[] { "proc_id_process1", "proc_id_process2", "res_single_0" },
                new[]
                {
                    new DeadlockPlaybackEdgeKey("res_single_0", "proc_id_process2"),
                    new DeadlockPlaybackEdgeKey("proc_id_process1", "res_single_0")
                });

            state.Nodes["proc_id_process1"].IsActive.Should().BeFalse();
            state.Nodes["proc_id_process2"].IsActive.Should().BeTrue();
            state.Edges[new DeadlockPlaybackEdgeKey("proc_id_process1", "res_single_0")]
                .IsActive.Should().BeFalse();
            state.Edges[new DeadlockPlaybackEdgeKey("proc_id_process1", "res_single_0")]
                .BadgeStepNumber.Should().BeNull();
        }

        [Fact]
        public void BuildState_WhenFocusCriticalPathIsEnabled_CollapsesNonCycleItems()
        {
            DeadlockTimelineParser.ParsedDeadlock timeline = CreateTimeline();
            timeline.Processes["process3"] = new DeadlockNodeInfo
            {
                Id = "process3",
                Spid = "61",
                IsInCycle = false
            };
            timeline.Resources["res_1"] = new DeadlockResourceInfo
            {
                Id = "res_1",
                Name = "pagelock",
                IsInCycle = false
            };
            timeline.Events.Add(new DeadlockEvent
            {
                StepNumber = 1,
                Type = "Request",
                ProcessId = "process3",
                ResourceId = "res_1",
                IsInCycle = false
            });

            var service = new DeadlockPlaybackStateService();

            DeadlockPlaybackGraphState state = service.BuildState(
                timeline,
                currentStep: 3,
                focusCriticalPath: true,
                new[] { "proc_id_process1", "proc_id_process3", "res_single_0", "res_single_1" },
                new[]
                {
                    new DeadlockPlaybackEdgeKey("proc_id_process1", "res_single_0"),
                    new DeadlockPlaybackEdgeKey("proc_id_process3", "res_single_1")
                });

            state.Nodes["proc_id_process1"].IsCollapsed.Should().BeFalse();
            state.Nodes["proc_id_process3"].IsCollapsed.Should().BeTrue();
            state.Nodes["res_single_1"].IsCollapsed.Should().BeTrue();
            state.Edges[new DeadlockPlaybackEdgeKey("proc_id_process3", "res_single_1")]
                .IsCollapsed.Should().BeTrue();
        }

        [Fact]
        public void BuildState_WhenArgumentsAreNull_Throws()
        {
            var service = new DeadlockPlaybackStateService();
            DeadlockTimelineParser.ParsedDeadlock timeline = CreateTimeline();

            Action nullTimeline = () => service.BuildState(
                null!,
                1,
                false,
                Array.Empty<string>(),
                Array.Empty<DeadlockPlaybackEdgeKey>());
            Action nullNodes = () => service.BuildState(
                timeline,
                1,
                false,
                null!,
                Array.Empty<DeadlockPlaybackEdgeKey>());
            Action nullEdges = () => service.BuildState(
                timeline,
                1,
                false,
                Array.Empty<string>(),
                null!);

            nullTimeline.Should().Throw<ArgumentNullException>();
            nullNodes.Should().Throw<ArgumentNullException>();
            nullEdges.Should().Throw<ArgumentNullException>();
        }

        private static DeadlockTimelineParser.ParsedDeadlock CreateTimeline()
        {
            return new DeadlockTimelineParser.ParsedDeadlock
            {
                Processes = new Dictionary<string, DeadlockNodeInfo>
                {
                    ["process1"] = new()
                    {
                        Id = "process1",
                        Spid = "58",
                        IsVictim = true,
                        IsInCycle = true
                    },
                    ["process2"] = new()
                    {
                        Id = "process2",
                        Spid = "59",
                        IsInCycle = true
                    }
                },
                Resources = new Dictionary<string, DeadlockResourceInfo>
                {
                    ["res_0"] = new()
                    {
                        Id = "res_0",
                        Name = "keylock",
                        IsInCycle = true
                    }
                },
                Events = new List<DeadlockEvent>
                {
                    new()
                    {
                        StepNumber = 1,
                        Type = "Grant",
                        ProcessId = "process2",
                        ResourceId = "res_0",
                        IsInCycle = true
                    },
                    new()
                    {
                        StepNumber = 2,
                        Type = "Request",
                        ProcessId = "process1",
                        ResourceId = "res_0",
                        IsInCycle = true
                    },
                    new()
                    {
                        StepNumber = 3,
                        Type = "Victim",
                        ProcessId = "process1",
                        IsInCycle = true,
                        IsVictim = true
                    }
                }
            };
        }
    }
}
