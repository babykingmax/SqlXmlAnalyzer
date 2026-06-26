using System;
using System.Windows.Input;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Mvvm;
using SqlXmlAnalyzer.Core.Services;

using System.Collections.ObjectModel;
using SqlXmlAnalyzer;

namespace SqlXmlAnalyzer.Core.ViewModels
{
    public enum WorkspaceMode
    {
        Start,
        Deadlock,
        ExecutionPlan,
        Compare
    }

    public class MainViewModel : ObservableObject
    {
        private readonly TuningSessionService _tuningSessionService;

        public ObservableCollection<DocumentTabViewModel> Tabs { get; } = new ObservableCollection<DocumentTabViewModel>();

        private DocumentTabViewModel? _selectedTab;
        public DocumentTabViewModel? SelectedTab
        {
            get => _selectedTab;
            set => SetProperty(ref _selectedTab, value);
        }

        private WorkspaceMode _workspaceMode = WorkspaceMode.Start;
        public WorkspaceMode WorkspaceMode
        {
            get => _workspaceMode;
            set => SetProperty(ref _workspaceMode, value);
        }

        private bool _isNavigationPaneVisible = true;
        public bool IsNavigationPaneVisible
        {
            get => _isNavigationPaneVisible;
            set => SetProperty(ref _isNavigationPaneVisible, value);
        }

        private bool _isPropertiesPaneVisible = true;
        public bool IsPropertiesPaneVisible
        {
            get => _isPropertiesPaneVisible;
            set => SetProperty(ref _isPropertiesPaneVisible, value);
        }

        private bool _isDiagnosticsPaneVisible = true;
        public bool IsDiagnosticsPaneVisible
        {
            get => _isDiagnosticsPaneVisible;
            set => SetProperty(ref _isDiagnosticsPaneVisible, value);
        }

        public void ActivateWorkspace(WorkspaceMode workspaceMode)
        {
            WorkspaceMode = workspaceMode;
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

        public MainViewModel(TuningSessionService? tuningSessionService = null)
        {
            _tuningSessionService = tuningSessionService ?? new TuningSessionService();
            ClearResultsCommand = new RelayCommand(_ => ClearResults());
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

            var snapshot = _tuningSessionService.CaptureSnapshot(
                CurrentPlanDoc,
                CurrentPlanFilePath,
                TuningHistory.Count + 1);
            TuningHistory.Add(snapshot);
            StatusText = $"已成功捕获当前计划版本: {snapshot.Title}";
        }

        public void SaveSession(string filePath)
        {
            try
            {
                _tuningSessionService.Save(
                    filePath,
                    TuningHistory,
                    PlanA,
                    PlanB);
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
                TuningSessionLoadResult result = _tuningSessionService.Load(filePath);

                TuningHistory.Clear();
                foreach (PlanSnapshot snapshot in result.Snapshots)
                {
                    TuningHistory.Add(snapshot);
                }

                PlanA = result.PlanA;
                PlanB = result.PlanB;
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

    }
}
