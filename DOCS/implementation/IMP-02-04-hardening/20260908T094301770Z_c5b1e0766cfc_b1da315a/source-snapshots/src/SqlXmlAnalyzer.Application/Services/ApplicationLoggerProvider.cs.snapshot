using Microsoft.Extensions.Logging;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Application.Services;

/// <summary>Routes layered ILogger events through the same build policy and file sink as WPF/Core.</summary>
public sealed class ApplicationLoggerProvider(IUnexpectedErrorReporter? unexpectedErrors = null) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ApplicationLogger(categoryName, unexpectedErrors);
    public void Dispose() => Logger.Flush();

    private sealed class ApplicationLogger(string category, IUnexpectedErrorReporter? unexpectedErrors) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) =>
            logLevel != Microsoft.Extensions.Logging.LogLevel.None && Logger.IsEnabled(Map(logLevel));

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            string message;
            try { message = $"{category}: {formatter(state, exception)}"; }
            catch (Exception formattingError)
            {
                // Avoid invoking the same formatter during diagnostic reporting.
                ExceptionPolicy.Describe(formattingError, "LogFormatter", unexpectedErrors);
                message = $"{category}: 日志格式化失败";
            }
            switch (Map(logLevel))
            {
                case SqlXmlAnalyzer.LogLevel.Critical: Logger.Critical(message, exception); break;
                case SqlXmlAnalyzer.LogLevel.Error:
                    if (exception == null) Logger.Error(message); else Logger.Error(message, exception);
                    break;
                case SqlXmlAnalyzer.LogLevel.Warning: Logger.Warning(message); break;
                default: Logger.Debug(message); break;
            }
        }

        private static SqlXmlAnalyzer.LogLevel Map(Microsoft.Extensions.Logging.LogLevel level) => level switch
        {
            Microsoft.Extensions.Logging.LogLevel.Critical => SqlXmlAnalyzer.LogLevel.Critical,
            Microsoft.Extensions.Logging.LogLevel.Error => SqlXmlAnalyzer.LogLevel.Error,
            Microsoft.Extensions.Logging.LogLevel.Warning => SqlXmlAnalyzer.LogLevel.Warning,
            _ => SqlXmlAnalyzer.LogLevel.Debug
        };
    }
}
