using System;
using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using SqlXmlAnalyzer.ViewModels;
using SqlXmlAnalyzer.Core.Models;

namespace SqlXmlAnalyzer.Tests
{
    public class DeadlockPlaybackViewModelTests
    {
        [Fact]
        public void Constructor_ShouldInitializeCorrectly()
        {
            var events = new List<DeadlockEvent>
            {
                new DeadlockEvent { StepNumber = 1, Description = "Event 1" },
                new DeadlockEvent { StepNumber = 2, Description = "Event 2" },
                new DeadlockEvent { StepNumber = 3, Description = "Event 3" }
            };

            var viewModel = new DeadlockPlaybackViewModel(events);

            viewModel.TotalSteps.Should().Be(3);
            viewModel.CurrentStep.Should().Be(0);
            viewModel.IsPlaying.Should().BeFalse();
            viewModel.CurrentStepDescription.Should().Be("准备就绪。点击播放开始回放死锁形成过程。");
        }

        [Fact]
        public void StepForward_ShouldAdvanceStepAndFireEvent()
        {
            var events = new List<DeadlockEvent>
            {
                new DeadlockEvent { StepNumber = 1, Description = "Event 1" },
                new DeadlockEvent { StepNumber = 2, Description = "Event 2" }
            };

            var viewModel = new DeadlockPlaybackViewModel(events);

            bool eventFired = false;
            viewModel.StepChanged += (s, e) => eventFired = true;

            viewModel.StepForwardCommand.Execute(null);

            viewModel.CurrentStep.Should().Be(1);
            viewModel.CurrentStepDescription.Should().Be("步骤 1/2: Event 1");
            eventFired.Should().BeTrue();
        }

        [Fact]
        public void StepBackward_ShouldDecreaseStep()
        {
            var events = new List<DeadlockEvent>
            {
                new DeadlockEvent { StepNumber = 1, Description = "Event 1" },
                new DeadlockEvent { StepNumber = 2, Description = "Event 2" }
            };

            var viewModel = new DeadlockPlaybackViewModel(events);
            viewModel.CurrentStep = 2; // Jump to end

            viewModel.StepBackwardCommand.Execute(null);

            viewModel.CurrentStep.Should().Be(1);
            viewModel.CurrentStepDescription.Should().Be("步骤 1/2: Event 1");
        }

        [Fact]
        public void TogglePlay_ShouldChangePlayingState()
        {
            var events = new List<DeadlockEvent>
            {
                new DeadlockEvent { StepNumber = 1, Description = "Event 1" }
            };

            var viewModel = new DeadlockPlaybackViewModel(events);

            viewModel.PlayCommand.Execute(null);
            viewModel.IsPlaying.Should().BeTrue();

            viewModel.PlayCommand.Execute(null);
            viewModel.IsPlaying.Should().BeFalse();
        }

        [Fact]
        public void StepForward_ShouldNotExceedTotalSteps()
        {
            var events = new List<DeadlockEvent>
            {
                new DeadlockEvent { StepNumber = 1, Description = "Event 1" }
            };

            var viewModel = new DeadlockPlaybackViewModel(events);
            viewModel.CurrentStep = 1;

            viewModel.StepForwardCommand.Execute(null);

            viewModel.CurrentStep.Should().Be(1);
        }

        [Fact]
        public void StepBackward_ShouldNotGoBelowZero()
        {
            var events = new List<DeadlockEvent>
            {
                new DeadlockEvent { StepNumber = 1, Description = "Event 1" }
            };

            var viewModel = new DeadlockPlaybackViewModel(events);
            viewModel.CurrentStep = 0;

            viewModel.StepBackwardCommand.Execute(null);

            viewModel.CurrentStep.Should().Be(0);
        }
    }
}
