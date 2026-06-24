// =====================================================================================
// Logger.cs - SqlXmlAnalyzer 高级条件日志系统 (v2)
// 支持功能：
//   • 编译时 DEBUG/RELEASE 控制
//   • 运行时 --verbose / --debug 强制详细日志
//   • --log-level 精确控制日志级别 (debug/verbose/info/warning/error/critical)
//   • --log-file 指定自定义日志文件路径
//   • 同时输出到控制台 + 文件
//   • XML 节点级深度调试信息
// =====================================================================================

#nullable disable

using System;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace SqlXmlAnalyzer
{
    /// <summary>
    /// 日志级别（数字越小越详细）
    /// </summary>
    public enum LogLevel
    {
        Debug = 0,
        Verbose = 1,
        Info = 2,
        Warning = 3,
        Error = 4,
        Critical = 5,
        None = 6
    }

    /// <summary>
    /// 高级条件日志记录器
    /// </summary>
    public static class Logger
    {
        // ==================== 状态 ====================
        public static bool IsDebugMode { get; } =
#if DEBUG
            true;
#else
            false;
#endif

        public static bool IsReleaseMode => !IsDebugMode;

        /// <summary>
        /// 当前生效的最小日志级别
        /// </summary>
        public static LogLevel MinimumLogLevel { get; private set; } = LogLevel.Info;

        /// <summary>
        /// 是否处于详细模式（兼容旧逻辑）
        /// </summary>
        public static bool VerboseMode { get; private set; }

        /// <summary>
        /// 是否已启用文件日志
        /// </summary>
        public static bool FileLoggingEnabled { get; private set; }

        /// <summary>
        /// 当前日志文件完整路径
        /// </summary>
        public static string LogFilePath { get; private set; }

        /// <summary>
        /// 用户指定的自定义日志路径（如果有）
        /// </summary>
        public static string CustomLogFilePath { get; private set; }

        private static StreamWriter _fileWriter;
        private static readonly object _lock = new object();
        private static bool _initialized = false;

        // ==================== 初始化 ====================
        /// <summary>
        /// 获取推荐的默认日志目录（应用程序同级 log 文件夹）
        /// 默认行为：所有日志会输出到 exe 同目录下的 log 文件夹
        /// </summary>
        public static string GetDefaultLogDirectory()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(baseDir, "log");
            }
            catch
            {
                // 极端情况下兜底到当前目录
                return Path.Combine(Directory.GetCurrentDirectory(), "log");
            }
        }

        /// <summary>
        /// 初始化日志系统
        /// </summary>
        public static void Initialize(
            bool forceVerbose = false,
            LogLevel? logLevel = null,
            string customLogFilePath = null,
            bool enableFileLogging = true)
        {
            if (_initialized) return;

            // 确定最终日志级别
            if (logLevel.HasValue)
            {
                MinimumLogLevel = logLevel.Value;
            }
            else if (forceVerbose || IsDebugMode)
            {
                MinimumLogLevel = LogLevel.Debug;
            }
            else
            {
                MinimumLogLevel = LogLevel.Info;   // Release 默认只显示 Info 及以上
            }

            VerboseMode = MinimumLogLevel <= LogLevel.Verbose;
            FileLoggingEnabled = enableFileLogging;
            CustomLogFilePath = customLogFilePath;

            if (FileLoggingEnabled)
            {
                try
                {
                    string finalLogPath;

                    if (!string.IsNullOrWhiteSpace(customLogFilePath))
                    {
                        // 用户指定了自定义路径
                        finalLogPath = customLogFilePath;
                        string dir = Path.GetDirectoryName(finalLogPath);
                        if (!string.IsNullOrEmpty(dir))
                            Directory.CreateDirectory(dir);
                    }
                    else
                    {
                        // 【修改后】默认使用应用程序同级 log 目录
                        string logDir = GetDefaultLogDirectory();
                        Directory.CreateDirectory(logDir);
                        CleanupOldLogs(logDir);

                        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        finalLogPath = Path.Combine(logDir, $"SqlXmlAnalyzer_{timestamp}.log");
                    }

                    LogFilePath = Path.GetFullPath(finalLogPath);

                    _fileWriter = new StreamWriter(LogFilePath, false, Encoding.UTF8)
                    {
                        AutoFlush = false   // 我们手动控制 Flush
                    };

                    // 写入日志头
                    WriteLogHeader();
                    FileLoggingEnabled = true;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"[警告] 无法创建日志文件: {ex.Message}");
                    Console.ResetColor();
                    FileLoggingEnabled = false;
                    LogFilePath = null;
                }
            }

            _initialized = true;

            // 记录初始化信息（如果允许）
            if (ShouldLog(LogLevel.Debug))
            {
                Debug($"Logger 初始化完成 | MinimumLogLevel={MinimumLogLevel} | VerboseMode={VerboseMode}");
                Debug($"文件日志: {(FileLoggingEnabled ? LogFilePath : "已禁用")}");
            }
        }

        private static void WriteLogHeader()
        {
            if (_fileWriter == null) return;

            _fileWriter.WriteLine("====================================================================");
            _fileWriter.WriteLine("SqlXmlAnalyzer 详细日志");
            _fileWriter.WriteLine($"启动时间        : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            _fileWriter.WriteLine($"版本            : {Core.ProductInfo.Version}");
            _fileWriter.WriteLine($"构建模式        : {(IsDebugMode ? "DEBUG" : "RELEASE")}");
            _fileWriter.WriteLine($"日志级别        : {MinimumLogLevel}");
            _fileWriter.WriteLine($"Verbose 模式    : {VerboseMode}");
            if (!string.IsNullOrEmpty(CustomLogFilePath))
                _fileWriter.WriteLine($"自定义日志路径  : {CustomLogFilePath}");
            _fileWriter.WriteLine($"日志文件        : {LogFilePath}");
            _fileWriter.WriteLine("====================================================================");
            _fileWriter.WriteLine();
            _fileWriter.Flush();
        }

        public static void Shutdown()
        {
            lock (_lock)
            {
                if (_fileWriter != null)
                {
                    try
                    {
                        _fileWriter.WriteLine();
                        _fileWriter.WriteLine("====================================================================");
                        _fileWriter.WriteLine($"日志结束时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                        _fileWriter.WriteLine("====================================================================");
                        _fileWriter.Flush();
                        _fileWriter.Dispose();
                    }
                    catch { }
                    _fileWriter = null;
                }
            }
        }

        // ==================== 核心判断 ====================

        private static bool ShouldLog(LogLevel level)
        {
            return level >= MinimumLogLevel;
        }

        // ==================== 写入实现 ====================

        private static void Write(string levelTag, string message, ConsoleColor? color = null, bool isError = false)
        {
            lock (_lock)
            {
                // 控制台输出
                if (color.HasValue)
                    Console.ForegroundColor = color.Value;

                string line = $"[{levelTag}] {DateTime.Now:HH:mm:ss.fff} {message}";

                if (isError)
                    Console.Error.WriteLine(line);
                else
                    Console.WriteLine(line);

                if (color.HasValue)
                    Console.ResetColor();

                // 文件输出（总是尝试写入，除非被级别过滤）
                if (_fileWriter != null)
                {
                    try
                    {
                        _fileWriter.WriteLine($"[{levelTag}] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}");
                    }
                    catch { }
                }
            }
        }

        // ==================== 公开日志方法 ====================

        public static void Debug(string message)
        {
            if (!ShouldLog(LogLevel.Debug)) return;
            Write("DEBUG", message, ConsoleColor.DarkGray);
        }

        public static void Verbose(string message)
        {
            if (!ShouldLog(LogLevel.Verbose)) return;
            Write("VERBOSE", message, ConsoleColor.DarkCyan);
        }

        public static void Info(string message)
        {
            if (!ShouldLog(LogLevel.Info)) return;
            Write("INFO", message, ConsoleColor.Gray);
        }

        public static void Warning(string message)
        {
            if (!ShouldLog(LogLevel.Warning)) return;
            Write("WARN", "⚠️  " + message, ConsoleColor.Yellow);
        }

        public static void Error(string message)
        {
            if (!ShouldLog(LogLevel.Error)) return;
            Write("ERROR", "❌ " + message, ConsoleColor.Red, isError: true);
        }

        public static void Error(string message, Exception ex)
        {
            if (!ShouldLog(LogLevel.Error)) return;

            Write("ERROR", "❌ " + message, ConsoleColor.Red, isError: true);

            if (ex == null) return;

            if (ShouldLog(LogLevel.Debug))
            {
                Write("ERROR", $"   异常类型: {ex.GetType().FullName}", ConsoleColor.Red, isError: true);
                Write("ERROR", $"   消息: {ex.Message}", ConsoleColor.Red, isError: true);
                Write("ERROR", $"   堆栈:\n{ex.StackTrace}", ConsoleColor.Red, isError: true);

                var inner = ex.InnerException;
                int i = 1;
                while (inner != null)
                {
                    Write("ERROR", $"   [Inner {i}] {inner.GetType().Name}: {inner.Message}", ConsoleColor.Red, isError: true);
                    inner = inner.InnerException;
                    i++;
                }
            }
            else if (ShouldLog(LogLevel.Error))
            {
                Write("ERROR", $"   异常类型: {ex.GetType().Name}", ConsoleColor.Red, isError: true);
                Write("ERROR", $"   消息: {ex.Message}", ConsoleColor.Red, isError: true);
            }
        }

        public static void Critical(string message, Exception ex = null)
        {
            if (!ShouldLog(LogLevel.Critical)) return;

            Write("CRITICAL", "💥 " + message, ConsoleColor.DarkRed, isError: true);

            if (ex != null && ShouldLog(LogLevel.Error))
            {
                Write("CRITICAL", $"   异常: {ex.GetType().FullName} - {ex.Message}", ConsoleColor.DarkRed, isError: true);
                if (ShouldLog(LogLevel.Debug) && !string.IsNullOrEmpty(ex.StackTrace))
                    Write("CRITICAL", $"   堆栈:\n{ex.StackTrace}", ConsoleColor.DarkRed, isError: true);
            }
        }

        /// <summary>
        /// 专门记录异常的强力方法（即使当前日志级别较高，也会确保完整异常信息写入日志文件）
        /// 推荐在所有 catch 块中使用
        /// </summary>
        public static void LogException(string context, Exception ex)
        {
            if (ex == null) return;

            // 临时降低级别确保能写进去
            var originalLevel = MinimumLogLevel;
            if (MinimumLogLevel > LogLevel.Error)
                MinimumLogLevel = LogLevel.Error;

            try
            {
                Error($"[{context}] 发生异常", ex);

                // 额外强制写入完整堆栈到文件（绕过级别限制）
                if (_fileWriter != null)
                {
                    lock (_lock)
                    {
                        _fileWriter.WriteLine($"[FULL EXCEPTION] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                        _fileWriter.WriteLine($"Context   : {context}");
                        _fileWriter.WriteLine($"Type      : {ex.GetType().FullName}");
                        _fileWriter.WriteLine($"Message   : {ex.Message}");
                        _fileWriter.WriteLine("StackTrace:");
                        _fileWriter.WriteLine(ex.StackTrace ?? "(no stack trace)");

                        Exception inner = ex.InnerException;
                        int depth = 1;
                        while (inner != null)
                        {
                            _fileWriter.WriteLine($"[InnerException {depth}] {inner.GetType().Name}: {inner.Message}");
                            _fileWriter.WriteLine(inner.StackTrace);
                            inner = inner.InnerException;
                            depth++;
                        }
                        _fileWriter.WriteLine(new string('=', 100));
                        _fileWriter.Flush();
                    }
                }
            }
            finally
            {
                MinimumLogLevel = originalLevel;
            }
        }

        // ==================== XML 调试方法 ====================

        public static void LogXmlElement(XElement element, string context = "", int maxDepth = 2)
        {
            if (!ShouldLog(LogLevel.Debug) || element == null) return;

            try
            {
                Debug($"[XML] {context} | <{element.Name.LocalName}> (ns: {element.Name.NamespaceName})");

                if (element.HasAttributes)
                {
                    foreach (var attr in element.Attributes())
                        Debug($"[XML]   @{attr.Name.LocalName} = \"{attr.Value}\"");
                }

                string text = element.Value?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    string display = text.Length > 180 ? text.Substring(0, 180) + "..." : text;
                    Debug($"[XML]   文本: {display}");
                }

                if (maxDepth > 0 && element.HasElements)
                {
                    foreach (var child in element.Elements())
                        LogXmlElement(child, $"{context}/{element.Name.LocalName}", maxDepth - 1);
                }
            }
            catch (Exception ex)
            {
                Debug($"[XML] 解析出错: {ex.Message}");
            }
        }

        public static void LogRelOpDetails(XElement relOp, string title = "RelOp 详情")
        {
            if (!ShouldLog(LogLevel.Debug) || relOp == null) return;

            Debug($"[XML] ========== {title} ==========");
            foreach (var attr in relOp.Attributes())
                Debug($"[XML]   {attr.Name.LocalName}: {attr.Value}");

            var obj = relOp.Descendants().FirstOrDefault(e => e.Name.LocalName == "Object");
            if (obj != null)
            {
                Debug("[XML]   关联对象:");
                foreach (var attr in obj.Attributes())
                    Debug($"[XML]     {attr.Name.LocalName}: {attr.Value}");
            }
            Debug("[XML] ========================================");
        }

        public static void LogMissingIndex(XElement missingIndexGroup, string context = "")
        {
            if (!ShouldLog(LogLevel.Debug) || missingIndexGroup == null) return;

            Debug($"[XML] {context} MissingIndexGroup Impact={missingIndexGroup.Attribute("Impact")?.Value}");

            var ns = missingIndexGroup.GetDefaultNamespace();
            var mi = missingIndexGroup.Element(ns + "MissingIndex") ?? missingIndexGroup.Element("MissingIndex");
            if (mi != null)
            {
                Debug($"[XML]   表: {mi.Attribute("Database")?.Value}.{mi.Attribute("Schema")?.Value}.{mi.Attribute("Table")?.Value}");

                foreach (var cg in mi.Elements())
                {
                    if (cg.Name.LocalName == "ColumnGroup")
                    {
                        var usage = cg.Attribute("Usage")?.Value;
                        var cols = string.Join(", ", cg.Elements().Select(c => c.Attribute("Name")?.Value));
                        Debug($"[XML]   {usage} 列: {cols}");
                    }
                }
            }
        }

        // ==================== 工具方法 ====================

        public static void DebugSection(string title)
        {
            if (!ShouldLog(LogLevel.Debug)) return;
            Debug($"──────────────────── [ {title} ] ────────────────────");
        }

        public static void Flush()
        {
            lock (_lock)
            {
                try { _fileWriter?.Flush(); } catch { }
            }
        }

        /// <summary>
        /// 自动清理 7 天前的历史日志文件，防止磁盘空间膨胀
        /// </summary>
        private static void CleanupOldLogs(string logDir, int keepDays = 7)
        {
            try
            {
                if (!Directory.Exists(logDir)) return;
                var dirInfo = new DirectoryInfo(logDir);
                var files = dirInfo.GetFiles("SqlXmlAnalyzer_*.log");
                var cutoff = DateTime.Now.AddDays(-keepDays);
                foreach (var file in files)
                {
                    if (file.LastWriteTime < cutoff)
                    {
                        try
                        {
                            file.Delete();
                        }
                        catch { } // 忽略被占用或无法删除的文件
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[警告] 清理历史日志失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 辅助方法：把字符串转换为 LogLevel
        /// </summary>
        public static LogLevel ParseLogLevel(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return LogLevel.Info;

            value = value.Trim().ToLowerInvariant();

            return value switch
            {
                "debug" or "d" => LogLevel.Debug,
                "verbose" or "v" => LogLevel.Verbose,
                "info" or "i" => LogLevel.Info,
                "warning" or "warn" or "w" => LogLevel.Warning,
                "error" or "err" or "e" => LogLevel.Error,
                "critical" or "crit" or "c" => LogLevel.Critical,
                "none" or "off" => LogLevel.None,
                _ => LogLevel.Info
            };
        }
    }
}


