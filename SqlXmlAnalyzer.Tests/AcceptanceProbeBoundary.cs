using System.IO;
using System.Text.Json;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Tests;

// Also compiled into the standalone IMP-29 probe host. No xUnit dependency.
internal static class AcceptanceProbeBoundary
{
    private static IUnexpectedErrorReporter? _reporter;
    private static string? _failure;

    public static string Describe(Exception exception)
    {
        _failure = ExceptionPolicy.Describe(exception, "IMP29.AcceptanceProbe", _reporter);
        return _failure;
    }

    public static int Execute(Func<int> action, string outputDirectory, IUnexpectedErrorReporter? reporter = null)
    {
        Directory.CreateDirectory(outputDirectory);
        Logger.Shutdown();
        Logger.Initialize(customLogFilePath: Path.Combine(outputDirectory, "probe.log"));
        _reporter = reporter ?? new UnexpectedErrorReporter(new WindowsMiniDumpWriter(), Path.Combine(outputDirectory, "dumps"));
        _failure = null;
        int code;
        try
        {
            Logger.Debug("IMP29 acceptance probe started.");
            code = action();
            if (code != 0) Logger.Error("IMP29 acceptance probe failed; exit code=" + code);
            else Logger.Debug("IMP29 acceptance probe completed.");
        }
        catch (Exception exception)
        {
            Describe(exception);
            code = 1;
        }
        try
        {
            File.WriteAllText(Path.Combine(outputDirectory, "boundary.json"), JsonSerializer.Serialize(new
            {
                Passed = code == 0 && _failure == null,
                ExitCode = code,
                Failure = _failure,
                Configuration = Logger.IsDebugMode ? "Debug" : "Release"
            }, new JsonSerializerOptions { WriteIndented = true }));
            return _failure == null ? code : 1;
        }
        finally { _reporter = null; Logger.Shutdown(); }
    }
}
