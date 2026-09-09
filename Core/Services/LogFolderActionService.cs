using System;
using System.ComponentModel;
using System.IO;
using System.Security;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Core.Services
{
    public enum LogFolderActionStatus
    {
        Ready,
        MissingDirectory,
        Failed,
        Opened
    }

    public sealed record LogFolderActionResult(
        LogFolderActionStatus Status,
        string FolderPath,
        string UserMessage)
    {
        public UnexpectedErrorReport? Diagnostic { get; init; }
    }

    public sealed class LogFolderActionService
    {
        private readonly Func<string> _logDirectoryProvider;
        private readonly Func<string, bool> _directoryExists;
        private readonly IUnexpectedErrorReporter _diagnostics;

        public LogFolderActionService(
            Func<string>? logDirectoryProvider = null,
            Func<string, bool>? directoryExists = null,
            IUnexpectedErrorReporter? diagnostics = null)
        {
            // Resolve at invocation time: the logger may be initialized or reconfigured later.
            _logDirectoryProvider = logDirectoryProvider ?? Logger.GetLogDirectory;
            _directoryExists = directoryExists ?? Directory.Exists;
            _diagnostics = diagnostics ?? UnexpectedErrorReporter.Shared;
        }

        public LogFolderActionResult BuildOpenLogsFolder()
        {
            string logsPath = string.Empty;
            try
            {
                string directory = _logDirectoryProvider();
                ArgumentException.ThrowIfNullOrWhiteSpace(directory);
                logsPath = Path.GetFullPath(directory);

                return _directoryExists(logsPath)
                    ? new(LogFolderActionStatus.Ready, logsPath, string.Empty)
                    : new(LogFolderActionStatus.MissingDirectory, logsPath,
                        $"日志目录不存在或无法访问：\n{logsPath}");
            }
            catch (Exception exception)
            {
                return Failure(exception, "LogFolder.Resolve", logsPath);
            }
        }

        public LogFolderActionResult OpenLogsFolder(Action<string> openFolder)
        {
            ArgumentNullException.ThrowIfNull(openFolder);
            var result = BuildOpenLogsFolder();
            if (result.Status != LogFolderActionStatus.Ready)
            {
                if (result.Status == LogFolderActionStatus.MissingDirectory)
                    Logger.Warning("LogFolder.Open: directory missing or inaccessible.");
                return result;
            }

            try
            {
                Logger.Debug("LogFolder.Open: requesting the system file browser.");
                openFolder(result.FolderPath);
                Logger.Debug("LogFolder.Open: the system accepted the request.");
                return result with { Status = LogFolderActionStatus.Opened };
            }
            catch (Exception exception)
            {
                return Failure(exception, "LogFolder.Open", result.FolderPath);
            }
        }

        private LogFolderActionResult Failure(Exception exception, string operation, string path)
        {
            UnexpectedErrorReport? diagnostic = null;
            // Bad paths and native shell/access errors are expected failures of this boundary.
            if (ExceptionPolicy.IsExpected(exception) || exception is ArgumentException or NotSupportedException or SecurityException or Win32Exception)
            {
                if (exception is OperationCanceledException) Logger.Warning($"操作取消：{operation}");
                else Logger.Error($"操作失败：{operation}", exception);
            }
            else
            {
                diagnostic = ExceptionPolicy.Capture(exception, operation, _diagnostics);
            }

            string message = $"无法打开日志目录：{ExceptionPolicy.Message(exception)}";
            if (diagnostic != null) message += $"\n{diagnostic.Summary}";
            return new(LogFolderActionStatus.Failed, path, message) { Diagnostic = diagnostic };
        }
    }
}
