using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SqlXmlAnalyzer.Core;

namespace SqlXmlAnalyzer
{
    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            ServiceProvider = serviceCollection.BuildServiceProvider();

            // ==================== CLI 验证模式 ====================
            if (Core.Services.CliService.HandleCommandLineArgs(e.Args))

            {
                Shutdown();
                return;
            }

            // ==================== 初始化全局日志系统（GUI 专用配置） ====================
            try
            {
                // 【修改后】默认日志目录：应用程序同级目录下的 log 文件夹
                // 例如：D:\Tools\SqlXmlAnalyzer\log\SqlXmlAnalyzer_20260601_103000.log
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string defaultLogDir = Path.Combine(baseDirectory, "log");

                Directory.CreateDirectory(defaultLogDir);

                string defaultLogFile = Path.Combine(defaultLogDir,
                    $"SqlXmlAnalyzer_{DateTime.Now:yyyyMMdd_HHmmss}.log");

                Logger.Initialize(
                    forceVerbose: false,
                    logLevel: Logger.IsDebugMode ? LogLevel.Debug : LogLevel.Info,
                    customLogFilePath: defaultLogFile,
                    enableFileLogging: true
                );

                Logger.Info("=== SqlXmlAnalyzer WPF 应用启动 ===");
                Logger.Info($"日志文件: {Logger.LogFilePath}");
            }
            catch (Exception logEx)
            {
                // 即使日志初始化失败，也要尽量记录（兜底）
                try
                {
                    // 兜底1：尝试写入应用程序目录下的 log 文件夹
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string fallbackDir = Path.Combine(baseDir, "log");
                    Directory.CreateDirectory(fallbackDir);
                    string fallbackLog = Path.Combine(fallbackDir, "SqlXmlAnalyzer_startup_error.log");
                    File.AppendAllText(fallbackLog, $"[{DateTime.Now}] Logger 初始化失败: {logEx}\r\n");

                    // 兜底2：同时也写一份到桌面，方便用户快速找到
                    string desktopFallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "SqlXmlAnalyzer_crash.log");
                    File.AppendAllText(desktopFallback, $"[{DateTime.Now}] Logger 初始化失败: {logEx}\r\n");
                }
                catch { }
            }

            // ==================== 全局异常捕获（确保所有错误都进日志） ====================
            // UI 线程未处理异常
            this.DispatcherUnhandledException += (sender, args) =>
            {
                Logger.Critical("UI 线程未处理异常 (DispatcherUnhandledException)", args.Exception);
                Logger.Flush();
                args.Handled = true; // 防止应用直接崩溃
                ShowFriendlyError("界面操作时发生错误", args.Exception);
            };

            // 非 UI 线程未处理异常
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                Logger.Critical("应用程序域未处理异常 (AppDomain.UnhandledException)", ex);
                Logger.Flush();
            };

            // Task 异步未观察异常
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                Logger.Critical("异步任务未观察异常 (UnobservedTaskException)", args.Exception);
                Logger.Flush();
                args.SetObserved(); // 防止进程终止
            };

            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            if (e.Args.Length > 0 && File.Exists(e.Args[0]))
            {
                mainWindow.Loaded += (sender, args) =>
                {
                    mainWindow.AnalyzeFile(e.Args[0]);
                };
            }
            mainWindow.Show();
        }

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<XelReader>();
            services.AddTransient<MainWindow>();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logger.Info("=== SqlXmlAnalyzer WPF 应用正常关闭 ===");
            Logger.Shutdown();
            base.OnExit(e);
        }

        private void ShowFriendlyError(string title, Exception ex)
        {
            // 这里可以弹出友好提示，同时错误已经记录到日志
            string logLocation = Logger.LogFilePath ?? "应用程序目录下的 log 文件夹";
            MessageBox.Show(
                $"{title}\n\n详细信息已记录到日志文件：\n{logLocation}\n\n错误摘要：{ex.Message}",
                "SqlXmlAnalyzer 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

    }
}