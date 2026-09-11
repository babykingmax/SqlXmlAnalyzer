using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using FluentAssertions;
using SqlXmlAnalyzer.Services;

namespace SqlXmlAnalyzer.Tests;

public sealed class UiBatchSchedulerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_CancelOrSupersedeStopsBeforeAnotherBatch(bool supersede) => RunSta(async () =>
    {
        using var cancel = new CancellationTokenSource();
        bool current = true;
        var applied = new List<int>();
        Func<Task> run = () => UiBatchScheduler.ApplyAsync(Enumerable.Range(0, 100).ToArray(), item =>
        {
            applied.Add(item);
            if (item == 3)
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
                { if (supersede) current = false; else cancel.Cancel(); }));
        }, cancel.Token, () => current);
        await run.Should().ThrowAsync<OperationCanceledException>();
        applied.Should().Equal(0, 1, 2, 3);
    });

    [Fact]
    public void Apply_KeepsOrderAndAllowsInputBetweenUpdates() => RunSta(async () =>
    {
        int thread = Environment.CurrentManagedThreadId;
        int inputSeenAt = -1;
        var values = new List<int>();
        await UiBatchScheduler.ApplyAsync(Enumerable.Range(0, 17).ToArray(), value =>
        {
            Environment.CurrentManagedThreadId.Should().Be(thread);
            values.Add(value);
            if (value == 0) Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => inputSeenAt = values.Count));
        }, CancellationToken.None, () => true);
        values.Should().Equal(Enumerable.Range(0, 17));
        inputSeenAt.Should().BeInRange(1, 4);
    });

    [Fact]
    public void Apply_UnknownFailureStopsTheBatchAndPropagates() => RunSta(async () =>
    {
        var applied = new List<int>(); var error = new InvalidOperationException("synthetic UI commit failure");
        Func<Task> run = () => UiBatchScheduler.ApplyAsync(new[] { 1, 2, 3 }, item =>
        { if (item == 2) throw error; applied.Add(item); }, CancellationToken.None, () => true);
        var assertion = await run.Should().ThrowAsync<InvalidOperationException>();
        assertion.Which.Should().BeSameAs(error); applied.Should().Equal(1);
    });

    private static void RunSta(Func<Task> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try
            {
                var task = test(); var watch = System.Diagnostics.Stopwatch.StartNew();
                while (!task.IsCompleted)
                {
                    if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Dispatcher batch test timed out");
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                }
                task.GetAwaiter().GetResult();
            }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        thread.Join(TimeSpan.FromSeconds(15)).Should().BeTrue();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
