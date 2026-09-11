namespace SqlXmlAnalyzer.Application.Services;

internal static class SqlSemanticDatabaseLifetime
{
    internal static async Task<T> RunAsync<T>(Func<Task> create, Func<Task<T>> observe,
        Func<CancellationToken, Task> cleanup, Action<Exception> cleanupFailed, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            // Ownership is tracked before dispatch. A canceled/faulted acknowledgement
            // cannot tell us whether CREATE DATABASE committed on the server.
            await create();
            return await observe();
        }
        finally
        {
            // The caller's cancellation must not suppress cleanup; cleanup still has
            // its own finite deadline and must record every unresolved failure.
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await cleanup(deadline.Token); }
            catch (Exception exception) { cleanupFailed(exception); }
        }
    }
}
