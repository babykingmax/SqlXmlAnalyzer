using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SqlXmlAnalyzer.Core;
using SqlXmlAnalyzer.Core.Abstractions;
using SqlXmlAnalyzer.Application;
using SqlXmlAnalyzer.Application.Services;
using SqlXmlAnalyzer.Analysis;
using SqlXmlAnalyzer.Refactoring;
using SqlXmlAnalyzer.Refactoring.Rules;
using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SqlXmlAnalyzer
{
    public partial class App : System.Windows.Application
    {
        public static IServiceProvider ServiceProvider { get; private set; } = null!;
        private GlobalExceptionHandlers? _exceptionHandlers;

        protected override void OnStartup(StartupEventArgs e)
        {
            Logger.Initialize();
            _exceptionHandlers = new GlobalExceptionHandlers();
            DispatcherUnhandledException += (_, args) =>
            {
                string detail = ExceptionPolicy.Describe(args.Exception, "WPF.DispatcherUnhandledException");
                Logger.Flush();
                args.Handled = true;
                ShowFriendlyError("界面操作时发生错误", args.Exception, detail);
            };
            base.OnStartup(e);

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            ServiceProvider = serviceCollection.BuildServiceProvider();

            // ==================== CLI 验证模式 ====================
            if (Core.Services.CliService.HandleCommandLineArgs(e.Args))

            {
                Shutdown(Environment.ExitCode);
                return;
            }

            var configurationResult = RuleConfigurationLoader.Load();
            if (configurationResult.Warnings.Count > 0)
            {
                MessageBox.Show(
                    string.Join(Environment.NewLine, configurationResult.Warnings),
                    "规则配置警告",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else if (!configurationResult.IsSuccess)
            {
                MessageBox.Show(
                    string.Join(Environment.NewLine, configurationResult.Errors),
                    "规则配置错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
                return;
            }

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
            services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddProvider(new ApplicationLoggerProvider());
                logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
            });
            services.AddSingleton<IUnexpectedErrorReporter>(UnexpectedErrorReporter.Shared);
            services.AddSingleton<IFileHandler, PhysicalFileHandler>();
            services.AddSingleton<ISqlWritebackFileSystem, PhysicalSqlWritebackFileSystem>();
            services.AddSingleton<ISqlWritebackService, SqlWritebackService>();
            services.AddSingleton<IResultReporter>(sp => new ConsoleResultReporter { ShowSql = false });
            services.AddSingleton<IAnalysisEngine>(sp => new SqlXmlAnalysisEngine());
            services.AddSingleton<IRuleFilter, DefaultRuleFilter>();

            // Rules
            services.AddSingleton<ISqlRefactorRule, ConstantFoldingRefactorRule>();
            services.AddSingleton<ISqlRefactorRule, IsNullComparisonRefactorRule>();
            services.AddSingleton<ISqlRefactorRule, LeftOrSubstringRefactorRule>();
            services.AddSingleton<ISqlRefactorRule, TrimRefactorRule>();
            services.AddSingleton<ISqlRefactorRule, ImplicitConversionRefactorRule>();
            services.AddSingleton<ISqlRefactorRule, SubqueryToJoinRule>();
            services.AddSingleton<ISqlRefactorRule, ExistsToJoinRule>();
            services.AddSingleton<ISqlRefactorRule, TableVariableRefactorRule>();
            services.AddSingleton<ISqlRefactorRule, ScalarSubqueryToJoinRule>();

            services.AddSingleton<IRefactoringEngine, SqlRefactoringEngine>();
            services.AddSingleton<ApplicationOrchestrator>();

            services.AddSingleton<XelReader>();
            services.AddSingleton<TemporaryFileManager>();
            services.AddSingleton<Core.Services.AnalysisSessionCoordinator>();
            services.AddSingleton<Core.Services.BrowserLauncher>();
            services.AddSingleton<Core.Services.LogFolderActionService>();
            services.AddSingleton<Core.Services.PdfWordReportService>();
            services.AddSingleton<Core.Services.IPdfWordReportExporter>(sp =>
                sp.GetRequiredService<Core.Services.PdfWordReportService>());
            services.AddSingleton<Core.Services.DocumentOpenService>();
            services.AddSingleton<DeadlockAnalysisService>();
            services.AddSingleton<Core.Services.PlanAnalysisService>();
            services.AddSingleton<Core.Services.DeadlockDocumentController>();
            services.AddSingleton<Core.Services.PlanDocumentController>();
            services.AddSingleton<Core.Services.DocumentRefreshActionService>();
            services.AddSingleton<Core.Services.PlanComparisonController>();
            services.AddSingleton<Core.Services.PlanComparisonTreeService>();
            services.AddSingleton<Core.Services.PlanComparisonTreeViewRenderer>();
            services.AddSingleton<Core.Services.MermaidDiagramService>();
            services.AddSingleton<Core.Services.MermaidDiagramActionService>();
            services.AddSingleton<Core.Services.AnalysisReportController>();
            services.AddSingleton<Core.Services.HtmlReportActionService>();
            services.AddSingleton<Core.Services.PortableReportActionService>();
            services.AddSingleton<Core.Services.TuningSessionService>();
            services.AddSingleton<Core.Services.PlanPropertyService>();
            services.AddSingleton<Core.Services.PlanTreeService>();
            services.AddSingleton<Core.Services.PlanSelectionActionService>();
            services.AddSingleton<Core.Services.PlanOperatorTreeViewRenderer>();
            services.AddSingleton<Core.Services.SqlDiffService>();
            services.AddSingleton<Core.Services.SqlDiffDocumentRenderer>();
            services.AddSingleton<Core.Services.SqlQuickFixService>();
            services.AddSingleton<Core.Services.IFileDialogService, Core.Services.WpfFileDialogService>();
            services.AddSingleton<Core.Services.FileAssociationRegistrationService>();
            services.AddSingleton<Core.Services.IHtmlReportWriter, Core.Services.HtmlReportWriter>();
            services.AddSingleton<Core.Services.HtmlReportExportService>();
            services.AddSingleton<Core.Services.PortableReportExportService>();
            services.AddSingleton<Core.Services.AnalysisClipboardService>();
            services.AddSingleton<Core.Services.MissingIndexDeploymentScriptService>();
            services.AddSingleton<Core.Services.MissingIndexClipboardActionService>();
            services.AddSingleton<Core.Services.DeadlockSelectionDetailService>();
            services.AddSingleton<Core.Services.DeadlockGraphViewportService>();
            services.AddSingleton<Core.Services.DeadlockGraphGeometryService>();
            services.AddSingleton<Core.Services.DeadlockCanvasInteractionService>();
            services.AddSingleton<Core.Services.DeadlockNodeDragService>();
            services.AddSingleton<Core.Services.DeadlockGraphSelectionService>();
            services.AddSingleton<Core.Services.DeadlockGraphLayoutService>();
            services.AddSingleton<Core.Services.DeadlockGraphEdgeService>();
            services.AddSingleton<Core.Services.DeadlockGraphEdgeRegistryService>();
            services.AddSingleton<Core.Services.DeadlockGraphPlacementService>();
            services.AddSingleton<Core.Services.DeadlockPlaybackStateService>();
            services.AddSingleton<Core.Services.DeadlockGraphVisualStateService>();
            services.AddSingleton<Core.Services.DeadlockStepBadgeService>();
            services.AddSingleton<Core.Services.WorkspacePanelLayoutService>();
            services.AddSingleton<Core.Services.TuningSessionActionService>();
            services.AddTransient<MainWindow>();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Logger.Info("=== SqlXmlAnalyzer WPF 应用正常关闭 ===");
            if (ServiceProvider?.GetService<TemporaryFileManager>() is { } temporaryFileManager)
            {
                temporaryFileManager.Dispose();
            }
            if (ServiceProvider?.GetService<Core.Services.AnalysisSessionCoordinator>() is { } coordinator)
            {
                coordinator.Dispose();
            }
            Logger.Shutdown();
            _exceptionHandlers?.Dispose();
            base.OnExit(e);
        }

        private void ShowFriendlyError(string title, Exception ex, string? diagnostic = null)
        {
            // 这里可以弹出友好提示，同时错误已经记录到日志
            string logLocation = Logger.LogFilePath ?? Logger.GetLogDirectory();
            MessageBox.Show(
                $"{title}\n\n日志文件：\n{logLocation}\n\n错误摘要：{diagnostic ?? ex.Message}",
                "SqlXmlAnalyzer 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

    }
}
