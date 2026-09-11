using FluentAssertions;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.ViewModels;

namespace SqlXmlAnalyzer.Tests;

public sealed class AnalysisOperationTests
{
    [Theory]
    [InlineData(AnalysisOperationState.Ready)]
    [InlineData(AnalysisOperationState.Partial)]
    [InlineData(AnalysisOperationState.Failed)]
    public void Pipeline_RecordsStagesAndTerminalStateCannotBeOverwritten(AnalysisOperationState terminal)
    {
        using var coordinator = new AnalysisSessionCoordinator();
        coordinator.Progress.State.Should().Be(AnalysisOperationState.Idle);
        var session = coordinator.Begin();
        coordinator.Report(session.RequestId, AnalysisOperationState.Parsing).Should().BeTrue();
        coordinator.Report(session.RequestId, AnalysisOperationState.Analyzing).Should().BeTrue();
        coordinator.Report(session.RequestId, AnalysisOperationState.PreparingView).Should().BeTrue();
        coordinator.Report(session.RequestId, terminal).Should().BeTrue();
        coordinator.Progress.Stages.Select(s => s.State).Should().Equal(AnalysisOperationState.Loading,
            AnalysisOperationState.Parsing, AnalysisOperationState.Analyzing, AnalysisOperationState.PreparingView);
        coordinator.Progress.Stages.Should().OnlyContain(s => s.Milliseconds >= 0);
        coordinator.Progress.IsBusy.Should().BeFalse();
        coordinator.Report(session.RequestId, AnalysisOperationState.Ready).Should().BeFalse();
        coordinator.Progress.State.Should().Be(terminal);
    }

    [Theory]
    [InlineData(AnalysisOperationState.Loading)]
    [InlineData(AnalysisOperationState.Parsing)]
    [InlineData(AnalysisOperationState.Analyzing)]
    [InlineData(AnalysisOperationState.PreparingView)]
    public void Cancellation_InEveryBusyStageBlocksReadyAndLateProgress(AnalysisOperationState stage)
    {
        using var coordinator = new AnalysisSessionCoordinator();
        var session = coordinator.Begin(); coordinator.Report(session.RequestId, stage);
        coordinator.Cancel(session.RequestId).Should().BeTrue();
        session.Token.IsCancellationRequested.Should().BeTrue();
        coordinator.Progress.State.Should().Be(AnalysisOperationState.Cancelled);
        coordinator.Report(session.RequestId, AnalysisOperationState.Ready).Should().BeFalse();
        coordinator.Report(session.RequestId, AnalysisOperationState.PreparingView).Should().BeFalse();
        coordinator.Progress.IsBusy.Should().BeFalse();
    }

    [Fact]
    public void OlderRequestAndOutOfOrderStageCannotOverwriteNewRequest()
    {
        using var coordinator = new AnalysisSessionCoordinator();
        var a = coordinator.Begin(); var b = coordinator.Begin();
        coordinator.Report(a.RequestId, AnalysisOperationState.Failed).Should().BeFalse();
        coordinator.Report(b.RequestId, AnalysisOperationState.Analyzing).Should().BeTrue();
        coordinator.Report(b.RequestId, AnalysisOperationState.Parsing).Should().BeFalse();
        coordinator.Cancel(a.RequestId).Should().BeFalse();
        coordinator.Progress.RequestId.Should().Be(b.RequestId);
        coordinator.Progress.State.Should().Be(AnalysisOperationState.Analyzing);
    }

    [Fact]
    public async Task CancelAcknowledgement_DoesNotWaitForArbitraryTokenCallback()
    {
        using var coordinator = new AnalysisSessionCoordinator();
        var session = coordinator.Begin();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = session.Token.Register(() => { entered.SetResult(); release.Task.GetAwaiter().GetResult(); });
        try
        {
            await Task.Run(() => coordinator.Cancel(session.RequestId)).WaitAsync(TimeSpan.FromSeconds(2));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            coordinator.Progress.State.Should().Be(AnalysisOperationState.Cancelled);
            release.Task.IsCompleted.Should().BeFalse();
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public void ResetAndReopen_AreAllowedAfterCancellationOrFailure()
    {
        using var coordinator = new AnalysisSessionCoordinator();
        var a = coordinator.Begin(); coordinator.Report(a.RequestId, AnalysisOperationState.Failed);
        var b = coordinator.Begin(); coordinator.Cancel(b.RequestId); coordinator.Reset();
        coordinator.Progress.State.Should().Be(AnalysisOperationState.Idle);
        var c = coordinator.Begin(); coordinator.Report(c.RequestId, AnalysisOperationState.Ready);
        coordinator.Progress.State.Should().Be(AnalysisOperationState.Ready);
    }

    [Fact]
    public void DisposedCoordinator_RejectsNewRequests()
    {
        var coordinator = new AnalysisSessionCoordinator(); var session = coordinator.Begin(); coordinator.Dispose();
        session.Token.IsCancellationRequested.Should().BeTrue();
        coordinator.IsCurrent(session.RequestId).Should().BeFalse();
        Action begin = () => coordinator.Begin(); begin.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void CancellationException_InvalidatesRequestAndPreventsLateCommit()
    {
        using var coordinator = new AnalysisSessionCoordinator();
        var session = coordinator.Begin();
        coordinator.Fail(session.RequestId, new OperationCanceledException());
        coordinator.Progress.State.Should().Be(AnalysisOperationState.Cancelled);
        coordinator.IsCurrent(session.RequestId).Should().BeFalse();
        session.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void ViewModel_RejectsLateDispatcherNotifications()
    {
        var model = new AnalysisOperationViewModel();
        model.Update(new(2, 10, AnalysisOperationState.Cancelled, "cancelled", []));
        model.Update(new(1, 9, AnalysisOperationState.Ready, "stale", []));
        model.Progress.RequestId.Should().Be(2); model.Progress.State.Should().Be(AnalysisOperationState.Cancelled);
        model.CancelCommand.CanExecute(null).Should().BeFalse();
    }
}
