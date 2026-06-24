using System;
using System.Windows.Input;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Mvvm;
using SqlXmlAnalyzer.Core.Services;

using System.Collections.ObjectModel;
using SqlXmlAnalyzer;

namespace SqlXmlAnalyzer.Core.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        public ObservableCollection<DocumentTabViewModel> Tabs { get; } = new ObservableCollection<DocumentTabViewModel>();

        private DocumentTabViewModel? _selectedTab;
        public DocumentTabViewModel? SelectedTab
        {
            get => _selectedTab;
            set => SetProperty(ref _selectedTab, value);
        }

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

        public ObservableCollection<SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion> MissingIndexes { get; } = new ObservableCollection<SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion>();
        public ICommand OpenSandboxCommand { get; set; }

        public ICommand ClearResultsCommand { get; }
        public ICommand ExportObfuscatedPlanCommand { get; }

        // --- 调优历史与 A/B 并排对比属性与命令 ---
        public ObservableCollection<PlanSnapshot> TuningHistory { get; } = new ObservableCollection<PlanSnapshot>();

        private PlanSnapshot? _planA;
        public PlanSnapshot? PlanA
        {
            get => _planA;
            set
            {
                if (SetProperty(ref _planA, value))
                {
                    OnPropertyChanged(nameof(CompareVisible));
                    OnPropertyChanged(nameof(CostDeltaText));
                    OnPropertyChanged(nameof(CostDeltaColor));
                }
            }
        }

        private PlanSnapshot? _planB;
        public PlanSnapshot? PlanB
        {
            get => _planB;
            set
            {
                if (SetProperty(ref _planB, value))
                {
                    OnPropertyChanged(nameof(CompareVisible));
                    OnPropertyChanged(nameof(CostDeltaText));
                    OnPropertyChanged(nameof(CostDeltaColor));
                }
            }
        }

        public bool CompareVisible => PlanA != null && PlanB != null;

        public string CostDeltaText
        {
            get
            {
                if (PlanA == null || PlanB == null) return string.Empty;
                if (Math.Abs(PlanA.TotalCost) < 1e-9)
                {
                    return "基准计划 A 成本为 0，无法计算百分比变化";
                }
                double delta = (PlanB.TotalCost - PlanA.TotalCost) / PlanA.TotalCost;
                if (delta < -1e-9)
                {
                    return $"▼ 预计成本优化: {Math.Abs(delta) * 100.0:F2}%";
                }
                else if (delta > 1e-9)
                {
                    return $"▲ 预计成本增加: {delta * 100.0:F2}%";
                }
                else
                {
                    return "● 预计两计划成本完全一致";
                }
            }
        }

        public string CostDeltaColor
        {
            get
            {
                if (PlanA == null || PlanB == null) return "#757575";
                double delta = PlanB.TotalCost - PlanA.TotalCost;
                if (delta < -1e-9)
                {
                    return "#2E7D32"; // 翠绿：优化成功
                }
                else if (delta > 1e-9)
                {
                    return "#D32F2F"; // 红色：成本上升
                }
                else
                {
                    return "#0066CC"; // 蓝色：持平
                }
            }
        }

        public ICommand CaptureCurrentPlanCommand { get; }
        public ICommand ClearHistoryCommand { get; }
        public ICommand RemoveSnapshotCommand { get; }
        public ICommand SetAsPlanACommand { get; }
        public ICommand SetAsPlanBCommand { get; }

        public MainViewModel()
        {
            ClearResultsCommand = new RelayCommand(_ => ClearResults());
            ExportObfuscatedPlanCommand = new RelayCommand(_ => ExportObfuscatedPlan(), _ => CurrentPlanDoc != null);
            OpenSandboxCommand = new RelayCommand(p =>
            {
                if (p is SqlXmlAnalyzer.Core.Models.MissingIndexSuggestion suggestion)
                {
                    var vm = new SqlXmlAnalyzer.ViewModels.IndexSandboxViewModel(suggestion, CurrentPlanDoc);
                    var win = new SqlXmlAnalyzer.Views.IndexSandboxWindow { DataContext = vm };
                    win.ShowDialog();
                }
            });

            CaptureCurrentPlanCommand = new RelayCommand(_ => CaptureCurrentPlan(), _ => CurrentPlanDoc != null);
            ClearHistoryCommand = new RelayCommand(_ =>
            {
                TuningHistory.Clear();
                PlanA = null;
                PlanB = null;
                StatusText = "已清空调优历史记录";
            });
            RemoveSnapshotCommand = new RelayCommand(p =>
            {
                if (p is PlanSnapshot s)
                {
                    TuningHistory.Remove(s);
                    if (PlanA == s) PlanA = null;
                    if (PlanB == s) PlanB = null;
                    StatusText = $"已移除历史版本: {s.Title}";
                }
            });
            SetAsPlanACommand = new RelayCommand(p =>
            {
                if (p is PlanSnapshot s)
                {
                    PlanA = s;
                    StatusText = $"已设置 {s.Title} 为 [计划 A]";
                }
            });
            SetAsPlanBCommand = new RelayCommand(p =>
            {
                if (p is PlanSnapshot s)
                {
                    PlanB = s;
                    StatusText = $"已设置 {s.Title} 为 [计划 B]";
                }
            });
        }

        public void CaptureCurrentPlan()
        {
            if (CurrentPlanDoc == null) return;

            XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
            double cost = 0.0;
            var rootRelOp = CurrentPlanDoc.Descendants(ns + "RelOp").FirstOrDefault();
            if (rootRelOp != null)
            {
                string costStr = rootRelOp.Attribute("EstimatedTotalSubtreeCost")?.Value ?? "0";
                double.TryParse(costStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out cost);
            }

            int opCount = CurrentPlanDoc.Descendants(ns + "RelOp").Count();
            int miCount = CurrentPlanDoc.Descendants(ns + "MissingIndex").Count();
            string stmt = CurrentPlanDoc.Descendants(ns + "StmtSimple").FirstOrDefault()?.Attribute("StatementText")?.Value ?? "未能提取 SQL 语句";

            var snapshot = new PlanSnapshot
            {
                Title = $"计划版本 #{TuningHistory.Count + 1} - " + System.IO.Path.GetFileName(CurrentPlanFilePath ?? "未命名"),
                FilePath = CurrentPlanFilePath ?? string.Empty,
                CaptureTime = DateTime.Now,
                Document = new XDocument(CurrentPlanDoc),
                TotalCost = cost,
                OperatorCount = opCount,
                MissingIndexCount = miCount,
                StatementText = stmt
            };
            TuningHistory.Add(snapshot);
            StatusText = $"已成功捕获当前计划版本: {snapshot.Title}";
        }

        public void SaveSession(string filePath)
        {
            try
            {
                XNamespace sessionNs = "http://schemas.sqlxmlanalyzer.com/session";
                var root = new XElement(sessionNs + "TuningSession",
                    new XAttribute("Version", "2.0"),
                    new XAttribute("Created", DateTime.Now.ToString("o")),
                    new XElement(sessionNs + "Snapshots",
                        System.Linq.Enumerable.Select(TuningHistory, s => new XElement(sessionNs + "Snapshot",
                            new XAttribute("Id", s.Id),
                            new XAttribute("Title", s.Title),
                            new XAttribute("FilePath", s.FilePath),
                            new XAttribute("CaptureTime", s.CaptureTime.ToString("o")),
                            new XAttribute("TotalCost", s.TotalCost.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                            new XAttribute("OperatorCount", s.OperatorCount),
                            new XAttribute("MissingIndexCount", s.MissingIndexCount),
                            new XElement(sessionNs + "StatementText", s.StatementText),
                            new XElement(sessionNs + "PlanDoc", s.Document.Root)
                        ))
                    )
                );
                if (PlanA != null) root.Add(new XAttribute("PlanAId", PlanA.Id));
                if (PlanB != null) root.Add(new XAttribute("PlanBId", PlanB.Id));

                var doc = new XDocument(root);
                doc.Save(filePath);
                StatusText = $"调优会话已保存至: {System.IO.Path.GetFileName(filePath)}";
            }
            catch (Exception ex)
            {
                Logger.LogException("SaveSession", ex);
                ShowMessageBox?.Invoke($"保存会话失败: {ex.Message}");
            }
        }

        public void LoadSession(string filePath)
        {
            try
            {
                var doc = SafeXmlHelper.LoadSafe(filePath);
                if (doc.Root == null) return;

                XNamespace sessionNs = "http://schemas.sqlxmlanalyzer.com/session";
                var snapshotsElem = doc.Root.Element(sessionNs + "Snapshots");
                if (snapshotsElem == null) return;

                TuningHistory.Clear();
                PlanA = null;
                PlanB = null;

                var snapshotsMap = new System.Collections.Generic.Dictionary<string, PlanSnapshot>();

                foreach (var sElem in snapshotsElem.Elements(sessionNs + "Snapshot"))
                {
                    string id = sElem.Attribute("Id")?.Value ?? Guid.NewGuid().ToString();
                    string title = sElem.Attribute("Title")?.Value ?? "Snapshot";
                    string origPath = sElem.Attribute("FilePath")?.Value ?? string.Empty;
                    DateTime.TryParse(sElem.Attribute("CaptureTime")?.Value, out DateTime captureTime);
                    double.TryParse(sElem.Attribute("TotalCost")?.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double cost);
                    int.TryParse(sElem.Attribute("OperatorCount")?.Value, out int opCount);
                    int.TryParse(sElem.Attribute("MissingIndexCount")?.Value, out int miCount);
                    string stmt = sElem.Element(sessionNs + "StatementText")?.Value ?? string.Empty;

                    var planDocElem = sElem.Element(sessionNs + "PlanDoc")?.Elements().FirstOrDefault();
                    XDocument planDoc = planDocElem != null ? new XDocument(new XElement(planDocElem)) : new XDocument();

                    var snapshot = new PlanSnapshot
                    {
                        Title = title,
                        FilePath = origPath,
                        CaptureTime = captureTime == default ? DateTime.Now : captureTime,
                        Document = planDoc,
                        TotalCost = cost,
                        OperatorCount = opCount,
                        MissingIndexCount = miCount,
                        StatementText = stmt
                    };

                    TuningHistory.Add(snapshot);
                    snapshotsMap[id] = snapshot;
                }

                string planAId = doc.Root.Attribute("PlanAId")?.Value ?? string.Empty;
                string planBId = doc.Root.Attribute("PlanBId")?.Value ?? string.Empty;

                if (!string.IsNullOrEmpty(planAId) && snapshotsMap.TryGetValue(planAId, out var pa)) PlanA = pa;
                if (!string.IsNullOrEmpty(planBId) && snapshotsMap.TryGetValue(planBId, out var pb)) PlanB = pb;

                StatusText = $"已成功载入调优会话，包含 {TuningHistory.Count} 个计划版本";
            }
            catch (Exception ex)
            {
                Logger.LogException("LoadSession", ex);
                ShowMessageBox?.Invoke($"加载会话失败: {ex.Message}");
            }
        }

        public void ClearResults()
        {
            CurrentDeadlockDoc = null;
            CurrentPlanDoc = null;
            DeadlockPatternText = "";
            PlanWarningsText = "";
            PlanStatementText = "";
            MissingIndexes.Clear();
            TuningHistory.Clear();
            PlanA = null;
            PlanB = null;
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
                    var maskedDoc = PlanObfuscatorService.ObfuscatePlan(CurrentPlanDoc);
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
