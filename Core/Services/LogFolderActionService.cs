using System;
using System.IO;

namespace SqlXmlAnalyzer.Core.Services
{
    public enum LogFolderActionStatus
    {
        Ready,
        MissingDirectory
    }

    public sealed record LogFolderActionResult(
        LogFolderActionStatus Status,
        string FolderPath,
        string UserMessage);

    public sealed class LogFolderActionService
    {
        private readonly Func<string> _baseDirectoryProvider;
        private readonly Func<string, bool> _directoryExists;

        public LogFolderActionService(
            Func<string>? baseDirectoryProvider = null,
            Func<string, bool>? directoryExists = null)
        {
            _baseDirectoryProvider = baseDirectoryProvider
                ?? (() => AppDomain.CurrentDomain.BaseDirectory);
            _directoryExists = directoryExists ?? Directory.Exists;
        }

        public LogFolderActionResult BuildOpenLogsFolder()
        {
            string logsPath = Path.Combine(_baseDirectoryProvider(), "log");

            return _directoryExists(logsPath)
                ? new LogFolderActionResult(
                    LogFolderActionStatus.Ready,
                    logsPath,
                    string.Empty)
                : new LogFolderActionResult(
                    LogFolderActionStatus.MissingDirectory,
                    logsPath,
                    "The log folder has not been created yet.");
        }
    }
}
