using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Core.Release;

/// <summary>Common boundary for local release scripts, including managed exceptions wrapped by PowerShell.</summary>
public static class ReleaseOperation
{
    public static int Run(Action action, string diagnosticsDirectory, IUnexpectedErrorReporter? reporter = null)
    {
        Logger.Shutdown();
        Logger.Initialize(customLogFilePath: Path.Combine(diagnosticsDirectory, "release.log"));
        try
        {
            Logger.Debug("Local release operation started.");
            action();
            Logger.Debug("Local release operation completed.");
            return 0;
        }
        catch (Exception exception)
        {
            while (exception.InnerException != null &&
                (exception.GetType().Namespace == "System.Management.Automation"
                 || exception is System.Reflection.TargetInvocationException))
                exception = exception.InnerException;
            ExceptionPolicy.Describe(exception, "IMP30.Release", reporter ??
                new UnexpectedErrorReporter(new WindowsMiniDumpWriter(), Path.Combine(diagnosticsDirectory, "dumps")));
            return exception is OperationCanceledException ? 130 : 1;
        }
        finally { Logger.Shutdown(); }
    }
}
