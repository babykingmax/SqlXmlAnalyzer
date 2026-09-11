namespace SqlXmlAnalyzer.Core.Diagnostics;

public sealed class GlobalExceptionHandlers : IDisposable
{
    public GlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobserved;
    }

    private static void OnUnhandled(object sender, UnhandledExceptionEventArgs args)
    {
        ExceptionPolicy.Describe(args.ExceptionObject as Exception ?? new Exception("Non-Exception unhandled failure"),
            "AppDomain.UnhandledException");
        Logger.Flush();
    }

    private static void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        ExceptionPolicy.Describe(args.Exception, "TaskScheduler.UnobservedTaskException");
        Logger.Flush();
        args.SetObserved();
    }

    public void Dispose()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        TaskScheduler.UnobservedTaskException -= OnUnobserved;
    }
}
