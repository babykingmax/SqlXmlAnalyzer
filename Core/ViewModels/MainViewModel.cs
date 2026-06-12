using System;
using System.Windows.Input;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Mvvm;

namespace SqlXmlAnalyzer.Core.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        public XDocument? CurrentDeadlockDoc { get; set; }
        public XDocument? CurrentPlanDoc { get; set; }
        public string? CurrentDeadlockFilePath { get; set; }
        public string? CurrentPlanFilePath { get; set; }
        
        public Action<string>? ShowMessageBox { get; set; }
        private string _statusText = "就绪 - 支持拖拽 XML 文件到窗口进行分析";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private string _appTitle = "SqlXmlAnalyzer v2.0 - 智能诊断引擎";
        public string AppTitle
        {
            get => _appTitle;
            set => SetProperty(ref _appTitle, value);
        }

        private string _deadlockPatternText = "";
        public string DeadlockPatternText
        {
            get => _deadlockPatternText;
            set => SetProperty(ref _deadlockPatternText, value);
        }

        private string _planWarningsText = "";
        public string PlanWarningsText
        {
            get => _planWarningsText;
            set => SetProperty(ref _planWarningsText, value);
        }

        private string _planStatementText = "";
        public string PlanStatementText
        {
            get => _planStatementText;
            set => SetProperty(ref _planStatementText, value);
        }

        public ICommand ClearResultsCommand { get; }
        public ICommand ExportObfuscatedPlanCommand { get; }

        public MainViewModel()
        {
            ClearResultsCommand = new RelayCommand(_ => ClearResults());
            ExportObfuscatedPlanCommand = new RelayCommand(_ => ExportObfuscatedPlan(), _ => CurrentPlanDoc != null);
        }

        public void ClearResults()
        {
            CurrentDeadlockDoc = null;
            CurrentPlanDoc = null;
            DeadlockPatternText = "";
            PlanWarningsText = "";
            PlanStatementText = "";
            StatusText = "已清空分析结果";
            AppTitle = "SqlXmlAnalyzer v2.0 - 智能诊断引擎";
        }

        private void ExportObfuscatedPlan()
        {
            if (CurrentPlanDoc == null)
            {
                ShowMessageBox?.Invoke("请先打开一个执行计划文件！");
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "执行计划文件 (*.sqlplan)|*.sqlplan",
                Title = "保存脱敏后的执行计划",
                FileName = "Obfuscated_Plan.sqlplan"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    StatusText = "正在生成脱敏计划...";
                    var maskedDoc = new XDocument(CurrentPlanDoc);
                    var dict = new System.Collections.Generic.Dictionary<string, string>();
                    
                    foreach (var elem in maskedDoc.Descendants())
                    {
                        var attrsToMask = new[] { "Table", "Schema", "Database", "Column", "Index" };
                        foreach (var attr in attrsToMask)
                        {
                            var a = elem.Attribute(attr);
                            if (a != null)
                            {
                                string coreVal = a.Value.Trim('[', ']');
                                if (string.IsNullOrWhiteSpace(coreVal) || coreVal.StartsWith("@") || coreVal.StartsWith("Expr") || coreVal.Equals("dbo", StringComparison.OrdinalIgnoreCase))
                                    continue;
                                    
                                string key = $"{attr}:{coreVal.ToLower()}";
                                if (!dict.TryGetValue(key, out string? masked))
                                {
                                    masked = $"Masked_{attr}_{dict.Count + 1}";
                                    dict[key] = masked;
                                }
                                a.Value = a.Value.StartsWith("[") ? $"[{masked}]" : masked;
                            }
                        }
                        
                        var stmtAttr = elem.Attribute("StatementText");
                        if (stmtAttr != null)
                        {
                            stmtAttr.Value = "-- 本语句已被 SqlXmlAnalyzer DOM 引擎脱敏保护 (Statement Obfuscated) --";
                        }
                    }
                    
                    maskedDoc.Save(dlg.FileName);
                    ShowMessageBox?.Invoke($"脱敏后的执行计划已保存至:\n{dlg.FileName}\n\n安全提示：敏感表名和SQL语句已完全替换，但该文件仍可被 SSMS 解析！");
                    StatusText = "就绪";
                }
                catch (Exception ex)
                {
                    Logger.LogException("ExportObfuscatedPlan", ex);
                    ShowMessageBox?.Invoke($"导出时发生错误:\n{ex.Message}");
                    StatusText = "脱敏导出失败";
                }
            }
        }
    }
}
