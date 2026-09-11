using System.Threading.Tasks;
using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using Xunit;

namespace SqlXmlAnalyzer.Tests
{
    public class AnalysisSessionCoordinatorTests
    {
        [Fact]
        public void Begin_CancelsPreviousSessionAndMakesNewSessionCurrent()
        {
            using var coordinator = new AnalysisSessionCoordinator();
            AnalysisSession first = coordinator.Begin();

            AnalysisSession second = coordinator.Begin();

            first.Token.IsCancellationRequested.Should().BeTrue();
            coordinator.IsCurrent(first.RequestId).Should().BeFalse();
            coordinator.IsCurrent(second.RequestId).Should().BeTrue();
        }

        [Fact]
        public async Task OlderCompletion_CannotCommitAfterNewSessionStarts()
        {
            using var coordinator = new AnalysisSessionCoordinator();
            AnalysisSession slowSession = coordinator.Begin();
            bool slowCommitted = false;
            bool fastCommitted = false;

            Task slow = Task.Run(async () =>
            {
                await Task.Delay(40);
                if (coordinator.IsCurrent(slowSession.RequestId))
                {
                    slowCommitted = true;
                }
            });

            AnalysisSession fastSession = coordinator.Begin();
            if (coordinator.IsCurrent(fastSession.RequestId))
            {
                fastCommitted = true;
            }

            await slow;

            fastCommitted.Should().BeTrue();
            slowCommitted.Should().BeFalse();
        }

        [Fact]
        public void CancelCurrent_InvalidatesCurrentSession()
        {
            using var coordinator = new AnalysisSessionCoordinator();
            AnalysisSession session = coordinator.Begin();

            coordinator.CancelCurrent();

            session.Token.IsCancellationRequested.Should().BeTrue();
            coordinator.IsCurrent(session.RequestId).Should().BeFalse();
        }

        [Theory]
        [InlineData(AnalysisDocumentKind.Unknown)]
        [InlineData(AnalysisDocumentKind.DeadlockXml)]
        [InlineData(AnalysisDocumentKind.XelDeadlockTrace)]
        public void ConfigurationCancellation_DoesNotCancelUnrelatedWork(AnalysisDocumentKind kind)
        {
            using var coordinator = new AnalysisSessionCoordinator();
            var session = coordinator.Begin(kind);
            coordinator.CancelCurrent(AnalysisDocumentKind.ExecutionPlanXml).Should().BeFalse();
            session.Token.IsCancellationRequested.Should().BeFalse();
            coordinator.IsCurrent(session.RequestId).Should().BeTrue();
        }

        [Fact]
        public void ConfigurationCancellation_CancelsPlanAfterContentRecognition()
        {
            using var coordinator = new AnalysisSessionCoordinator();
            var session = coordinator.Begin();
            coordinator.TrySetKind(session.RequestId, AnalysisDocumentKind.ExecutionPlanXml).Should().BeTrue();
            coordinator.CancelCurrent(AnalysisDocumentKind.ExecutionPlanXml).Should().BeTrue();
            session.Token.IsCancellationRequested.Should().BeTrue();
            coordinator.IsCurrent(session.RequestId).Should().BeFalse();
        }

        [Fact]
        public void OldRequest_CannotReclassifyOrCancelNewDeadlockWork()
        {
            using var coordinator = new AnalysisSessionCoordinator();
            var old = coordinator.Begin(AnalysisDocumentKind.ExecutionPlanXml);
            var current = coordinator.Begin(AnalysisDocumentKind.DeadlockXml);
            coordinator.TrySetKind(old.RequestId, AnalysisDocumentKind.ExecutionPlanXml).Should().BeFalse();
            coordinator.Cancel(old.RequestId).Should().BeFalse();
            coordinator.CancelCurrent(AnalysisDocumentKind.ExecutionPlanXml).Should().BeFalse();
            coordinator.IsCurrent(current.RequestId).Should().BeTrue();
        }
    }
}
