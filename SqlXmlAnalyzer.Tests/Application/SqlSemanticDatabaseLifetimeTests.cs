using System.IO;
using FluentAssertions;
using SqlXmlAnalyzer.Application.Services;

namespace SqlXmlAnalyzer.Tests.Application;

public sealed class SqlSemanticDatabaseLifetimeTests
{
    [Theory]
    [InlineData("master")]
    [InlineData("tempdb")]
    [InlineData("SxaSemantic_")]
    [InlineData("SxaSemantic_not-a-guid")]
    [InlineData("SxaSemantic_00000000000000000000000000000000]; DROP DATABASE master;--")]
    public async Task Cleanup_NonOwnedNameIsRejectedBeforeConnecting(string database)
    {
        Func<Task> cleanup = () => LocalDbSqlSemanticRunner.CleanupDatabaseAsync("unused-invalid-instance", database, default);
        await cleanup.Should().ThrowAsync<InvalidDataException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_FailedAcknowledgementStillCleansOwnedDatabase(bool cancel)
    {
        using var request = new CancellationTokenSource();
        bool databaseExists = false, observed = false, cleaned = false;
        Exception fault = cancel ? new OperationCanceledException(request.Token) : new IOException("lost acknowledgement");
        Func<Task> run = () => SqlSemanticDatabaseLifetime.RunAsync<int>(
            () => { databaseExists = true; if (cancel) request.Cancel(); return Task.FromException(fault); },
            () => { observed = true; return Task.FromResult(1); },
            cleanupToken => { cleanupToken.IsCancellationRequested.Should().BeFalse(); databaseExists = false; cleaned = true; return Task.CompletedTask; },
            exception => throw new InvalidOperationException("Cleanup must succeed", exception), request.Token);
        (await run.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(fault);
        observed.Should().BeFalse(); cleaned.Should().BeTrue(); databaseExists.Should().BeFalse();
    }

    [Fact]
    public async Task Observe_FaultRetainsPrimaryExceptionAndReportsCleanupFailure()
    {
        var primary = new InvalidOperationException("observe failed");
        var cleanup = new IOException("cleanup unavailable");
        Exception? reported = null;
        Func<Task> run = () => SqlSemanticDatabaseLifetime.RunAsync<int>(() => Task.CompletedTask,
            () => Task.FromException<int>(primary), _ => Task.FromException(cleanup), error => reported = error, default);
        (await run.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(primary);
        reported.Should().BeSameAs(cleanup);
    }

    [Fact]
    public async Task Observe_SuccessCannotHideCleanupFailure()
    {
        var cleanup = new InvalidOperationException("unknown cleanup failure");
        var failures = new List<Exception>();
        (await SqlSemanticDatabaseLifetime.RunAsync(() => Task.CompletedTask, () => Task.FromResult(1),
            _ => Task.FromException(cleanup), failures.Add, default)).Should().Be(1);
        failures.Should().ContainSingle().Which.Should().BeSameAs(cleanup);
    }

    [Fact]
    public async Task Run_AlreadyCanceledDoesNotDispatchOrCleanAnything()
    {
        bool called = false;
        Func<Task> run = () => SqlSemanticDatabaseLifetime.RunAsync(
            () => { called = true; return Task.CompletedTask; }, () => Task.FromResult(1),
            _ => { called = true; return Task.CompletedTask; }, _ => { called = true; }, new CancellationToken(true));
        await run.Should().ThrowAsync<OperationCanceledException>();
        called.Should().BeFalse();
    }
}
