using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Scoring;
using SqlXmlAnalyzer.Core.Services;
using SqlXmlAnalyzer.Core.Diagnostics;
using SqlXmlAnalyzer.Core.Mvvm;
using SqlXmlAnalyzer.Core.Simulation;
using SqlXmlAnalyzer.Core.Refactoring;
using System.Globalization;

namespace SqlXmlAnalyzer.ViewModels
{
    public class IndexSandboxViewModel : INotifyPropertyChanged
    {
        private MissingIndexSuggestion _suggestion;
        private XDocument? _originalPlan;
        private readonly IUnexpectedErrorReporter? _unexpectedErrors;
        private readonly SandboxInputSnapshot _inputSnapshot;
        private IndexScoreResult? _scoreAssessment;
        private CostImpactResult? _simulation;
        private bool _rowsEdited, _widthEdited, _returnedEdited;
        private string _indexName = "", _maxDop = "", _compression = "PAGE", _error = "", _outputStatus = "";
        private bool _online = true, _sortInTempDb = true;
        private CompiledIndexDdl? _compiled;
        private string _fullObject = "目标对象未解析";
        public string FullObject => _fullObject;
        public string IndexName { get => _indexName; set { _indexName = value; RefreshReview(nameof(IndexName)); } }
        public string MaxDop { get => _maxDop; set { _maxDop = value; RefreshReview(nameof(MaxDop)); } }
        public string DataCompression { get => _compression; set { _compression = value; RefreshReview(nameof(DataCompression)); } }
        public bool Online { get => _online; set { _online = value; RefreshReview(nameof(Online)); } }
        public bool SortInTempDb { get => _sortInTempDb; set { _sortInTempDb = value; RefreshReview(nameof(SortInTempDb)); } }
        public string[] CompressionOptions { get; } = ["NONE", "ROW", "PAGE"];
        public string CompiledIndexName => _compiled?.IndexName ?? "";
        public string RollbackStatement => _compiled?.Rollback ?? "";
        public string Error => _error;
        public string OutputStatus => _outputStatus;
        public bool CanExport => _compiled != null && !string.IsNullOrEmpty(_compiled.Create) && string.IsNullOrEmpty(_error);
        public string EnvironmentNotice => "环境未连接核验：请核对 SQL Server 版本/版本类型、ONLINE 支持与锁等待、压缩支持、tempdb 空间、MAXDOP 和既有索引目录。ONLINE 不保证无阻塞。脚本仅供审核，不执行建索引。";
        public void CopyScript(Action<string> clipboard)
        {
            if (!CanExport) return;
            var result = new ReviewOutputService(_unexpectedErrors).Copy(CreateIndexStatement, clipboard);
            _outputStatus = result.Message;
            OnPropertyChanged(nameof(OutputStatus));
        }

        private void RefreshReview(string? editedProperty = null)
        {
            try { RecalculateCore(editedProperty); }
            catch (Exception) { /* Recalculate has reported the incident and invalidated every script. */ }
        }
        private readonly XNamespace _ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

        public ObservableCollection<IndexColumn> KeyColumns { get; }
        public ObservableCollection<IndexColumn> IncludeColumns { get; }
        public ObservableCollection<string> AvailableColumns { get; } = new ObservableCollection<string>();

        private int _currentScore;
        public int CurrentScore
        {
            get => _currentScore;
            set
            {
                _currentScore = value;
                OnPropertyChanged();
            }
        }

        private string _createIndexStatement = "";
        public string CreateIndexStatement
        {
            get => _createIndexStatement;
            set
            {
                _createIndexStatement = value;
                OnPropertyChanged();
            }
        }

        public string CostReductionSummary => "N/A（未提供收益预测）";
        public string SimulationNotice => AnalysisDisplayText.SandboxNotice;
        public string InputAssumptionsNotice => $"假设输入（非实测）：总行数={(_rowsEdited ? "用户编辑" : _inputSnapshot.TotalRows.Source)}；行宽={(_widthEdited ? "用户编辑" : _inputSnapshot.AverageRowSize.Source)}；返回行数={(_returnedEdited ? "用户编辑" : _inputSnapshot.ReturnedRows.Source)}。";
        public string ModelSummary => $"评分 {IndexScoreResult.Version}；成本 {CostImpactResult.Version}；输入 {SandboxInputSnapshot.ModelVersion}";
        public string ScoreBreakdown => _scoreAssessment?.Breakdown ?? "评分证据不可用。";
        public string ScoreWeights => _scoreAssessment?.Weights ?? "";
        public string ScoreSource => _scoreAssessment == null ? "N/A" : $"谓词证据来源：{_scoreAssessment.EvidenceDescription}；{_scoreAssessment.InputScope}";
        public string ImpactSummary => _suggestion.Source == IndexSuggestionSource.CapturedMissingIndex && _suggestion.CapturedImpact is { } impact
            && double.IsFinite(impact) && impact is >= 0 and <= 100 ? $"SQL Server 原始 Impact：{impact:F1}%（优化器估算，非实测收益）"
            : "SQL Server 原始 Impact：N/A（未采集；SQL 语法候选不补造 Impact）";
        public string CostEvidenceSummary => _simulation == null ? "成本证据不可用。" :
            $"范围：{_simulation.QueryPlanCount} 个 QueryPlan / {_simulation.OperatorCount} 个算子；全部自身估算成本 {_simulation.TotalOwnCost.Display("G6")}，关联访问算子成本 {_simulation.RelatedOwnCost.Display("G6")}。{_simulation.CombinationPolicy}";

        private string _costReductionDescription = "";
        public string CostReductionDescription
        {
            get => _costReductionDescription;
            set
            {
                _costReductionDescription = value;
                OnPropertyChanged();
            }
        }

        private double _totalRows;
        public double TotalRows
        {
            get => _totalRows;
            set
            {
                _totalRows = value;
                _rowsEdited = true;
                OnPropertyChanged(nameof(InputAssumptionsNotice));
                OnPropertyChanged();
                UpdateTippingPointProperties();
            }
        }

        private double _avgRowSize;
        public double AvgRowSize
        {
            get => _avgRowSize;
            set
            {
                _avgRowSize = value;
                _widthEdited = true;
                OnPropertyChanged(nameof(InputAssumptionsNotice));
                OnPropertyChanged();
                UpdateTippingPointProperties();
            }
        }

        private double _returnedRows;
        public double ReturnedRows
        {
            get => _returnedRows;
            set
            {
                _returnedRows = value;
                _returnedEdited = true;
                OnPropertyChanged(nameof(InputAssumptionsNotice));
                OnPropertyChanged();
                UpdateTippingPointProperties();
            }
        }

        public double TippingPointLow => Math.Max(10.0, Math.Round((TotalRows * AvgRowSize / 8192.0) / 4.0));
        public double TippingPointHigh => Math.Max(15.0, Math.Round((TotalRows * AvgRowSize / 8192.0) / 3.0));

        public bool IsCoveredIndex
        {
            get
            {
                if (_originalPlan == null) return false;

                var outputCols = SqlXmlAnalyzer.Core.Services.IndexTargetResolver.FindColumns(_suggestion, _originalPlan, _ns)
                    .Where(column => column.Ancestors().Any(a => a.Name == _ns + "OutputList"))
                    .Select(column => (string?)column.Attribute("Column"))
                    .OfType<string>().ToHashSet(StringComparer.Ordinal);

                if (outputCols.Count == 0) return false;

                var indexCols = KeyColumns.Concat(IncludeColumns)
                                          .Select(c => SqlObjectIdentity.DecodeIdentifier(c.Name)!)
                                          .ToHashSet(StringComparer.Ordinal);
                return outputCols.All(col => indexCols.Contains(col));
            }
        }

        private string _tippingPointStatus = "";
        public string TippingPointStatus { get => _tippingPointStatus; set { _tippingPointStatus = value; OnPropertyChanged(); } }

        private string _tippingPointStatusColor = "";
        public string TippingPointStatusColor { get => _tippingPointStatusColor; set { _tippingPointStatusColor = value; OnPropertyChanged(); } }

        private string _tippingPointDetails = "";
        public string TippingPointDetails { get => _tippingPointDetails; set { _tippingPointDetails = value; OnPropertyChanged(); } }

        public string Table => _suggestion.Table;

        public ICommand RemoveKeyColumnCommand { get; }
        public ICommand RemoveIncludeColumnCommand { get; }
        public ICommand MoveKeyColumnUpCommand { get; }
        public ICommand MoveKeyColumnDownCommand { get; }
        public ICommand AddKeyColumnCommand { get; }
        public ICommand AddIncludeColumnCommand { get; }

        public IndexSandboxViewModel(MissingIndexSuggestion suggestion, XDocument? originalPlan = null,
            IUnexpectedErrorReporter? unexpectedErrors = null)
        {
            ArgumentNullException.ThrowIfNull(suggestion);
            _suggestion = suggestion;
            _originalPlan = originalPlan;
            _unexpectedErrors = unexpectedErrors;
            KeyColumns = new ObservableCollection<IndexColumn>(suggestion.KeyColumns.Select(c => new IndexColumn { Name = c.Name, Usage = c.Usage }));
            IncludeColumns = new ObservableCollection<IndexColumn>(suggestion.IncludeColumns.Select(c => new IndexColumn { Name = c.Name, Usage = c.Usage }));

            _inputSnapshot = SandboxInputSnapshot.Capture(suggestion, originalPlan, _ns);
            _totalRows = _inputSnapshot.TotalRows.Value;
            _avgRowSize = _inputSnapshot.AverageRowSize.Value;
            _returnedRows = _inputSnapshot.ReturnedRows.Value;

            RemoveKeyColumnCommand = new RelayCommand(p =>
            {
                if (p is IndexColumn c && KeyColumns.Contains(c))
                {
                    KeyColumns.Remove(c);
                    string colFormatted = c.Name.StartsWith("[") ? c.Name : $"[{c.Name}]";
                    if (!AvailableColumns.Contains(colFormatted))
                        AvailableColumns.Add(colFormatted);
                    RefreshReview();
                }
            });
            RemoveIncludeColumnCommand = new RelayCommand(p =>
            {
                if (p is IndexColumn c && IncludeColumns.Contains(c))
                {
                    IncludeColumns.Remove(c);
                    string colFormatted = c.Name.StartsWith("[") ? c.Name : $"[{c.Name}]";
                    if (!AvailableColumns.Contains(colFormatted))
                        AvailableColumns.Add(colFormatted);
                    RefreshReview();
                }
            });
            MoveKeyColumnUpCommand = new RelayCommand(p =>
            {
                if (p is IndexColumn c)
                {
                    int idx = KeyColumns.IndexOf(c);
                    if (idx > 0)
                    {
                        KeyColumns.Move(idx, idx - 1);
                        RefreshReview();
                    }
                }
            });
            MoveKeyColumnDownCommand = new RelayCommand(p =>
            {
                if (p is IndexColumn c)
                {
                    int idx = KeyColumns.IndexOf(c);
                    if (idx >= 0 && idx < KeyColumns.Count - 1)
                    {
                        KeyColumns.Move(idx, idx + 1);
                        RefreshReview();
                    }
                }
            });

            AddKeyColumnCommand = new RelayCommand(p =>
            {
                if (p is string colName && AvailableColumns.Contains(colName))
                {
                    string usage = "EQUALITY";
                    string rawName = SqlObjectIdentity.DecodeIdentifier(colName)!;
                    var orig = suggestion.KeyColumns.FirstOrDefault(c => string.Equals(SqlObjectIdentity.DecodeIdentifier(c.Name)!, rawName, StringComparison.Ordinal));
                    if (orig != null) usage = orig.Usage;

                    KeyColumns.Add(new IndexColumn { Name = colName, Usage = usage });
                    AvailableColumns.Remove(colName);
                    RefreshReview();
                }
            });

            AddIncludeColumnCommand = new RelayCommand(p =>
            {
                if (p is string colName && AvailableColumns.Contains(colName))
                {
                    IncludeColumns.Add(new IndexColumn { Name = colName, Usage = "INCLUDE" });
                    AvailableColumns.Remove(colName);
                    RefreshReview();
                }
            });

            LoadAvailableColumns();
            RefreshReview();
        }

        public void Recalculate() => RecalculateCore(null);

        private void RecalculateCore(string? editedProperty)
        {
            try
            {
                if (editedProperty != null) OnPropertyChanged(editedProperty);
                _fullObject = "目标对象未解析";
                _fullObject = IndexDdlCompiler.ResolveTarget(_suggestion).DisplayName;
                var temp = new MissingIndexSuggestion
                {
                    Schema = _suggestion.Schema,
                    Server = _suggestion.Server,
                    Database = _suggestion.Database,
                    ObjectIdentity = _suggestion.ObjectIdentity,
                    Location = _suggestion.Location,
                    Table = _suggestion.Table,
                    Impact = _suggestion.Impact,
                    Source = _suggestion.Source,
                    CapturedImpact = _suggestion.CapturedImpact,
                    KeyColumns = KeyColumns.ToList(),
                    IncludeColumns = IncludeColumns.ToList()
                };

                var score = IndexScoringCalculator.Evaluate(temp, _originalPlan, _ns, _unexpectedErrors);
                var simulation = CostImpactSimulator.Simulate(_originalPlan, temp, _ns, _unexpectedErrors);
                int? maxDop = null;
                if (!string.IsNullOrWhiteSpace(_maxDop))
                {
                    if (!int.TryParse(_maxDop, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                        throw new System.IO.InvalidDataException("MAXDOP 必须为 0–64 的整数，留空使用服务器默认设置。");
                    maxDop = parsed;
                }
                var compiled = IndexDdlCompiler.Compile(temp, new IndexDdlOptions
                {
                    IndexName = _indexName, Online = _online, DataCompression = _compression,
                    SortInTempDb = _sortInTempDb, MaxDop = maxDop
                }, unexpectedErrors: _unexpectedErrors);
                _compiled = compiled;
                _error = compiled.Create.Length == 0 ? "至少选择一个键列才能生成脚本。" : "";
                _outputStatus = "选项或列发生变化后，请重新核对并复制脚本。";
                _scoreAssessment = score;
                _simulation = simulation;
                CurrentScore = score.Score;
                CreateIndexStatement = compiled.Create;
                CostReductionDescription = "成本模型未校准，已暂停数值收益预测；" + simulation.Description;
                OnPropertyChanged(nameof(ScoreBreakdown));
                OnPropertyChanged(nameof(ScoreWeights));
                OnPropertyChanged(nameof(ScoreSource));
                OnPropertyChanged(nameof(CostEvidenceSummary));
                NotifyReview();

                UpdateTippingPointProperties();
                Logger.Debug("IMP-08: 索引沙盒展示已刷新；数值收益预测未启用。");
            }
            catch (Exception ex)
            {
                _error = ExceptionPolicy.Describe(ex, "IndexSandboxViewModel.Recalculate", _unexpectedErrors);
                _compiled = null;
                _outputStatus = "审核失败；旧脚本已清除。";
                _costReductionDescription = "评估失败，未生成收益预测。";
                _createIndexStatement = string.Empty;
                _currentScore = 0;
                _scoreAssessment = null;
                _simulation = null;
                _tippingPointStatus = "N/A（审核失败）";
                _tippingPointStatusColor = "#757575";
                _tippingPointDetails = "审核失败，假设输入尚未重新评估。";
                PublishFailureState(editedProperty);
                throw;
            }
        }

        private void PublishFailureState(string? editedProperty)
        {
            // Multicast invocation stops at the first exception. Deliver the invalidated state
            // independently so one broken subscriber cannot leave other WPF bindings stale.
            string?[] properties = [nameof(CreateIndexStatement), nameof(RollbackStatement), nameof(CompiledIndexName),
                nameof(CanExport), nameof(Error), nameof(OutputStatus), nameof(FullObject),
                nameof(CostReductionDescription), nameof(CurrentScore), nameof(ScoreBreakdown), nameof(ScoreWeights),
                nameof(ScoreSource), nameof(CostEvidenceSummary), nameof(TippingPointStatus),
                nameof(TippingPointStatusColor), nameof(TippingPointDetails), editedProperty];
            foreach (PropertyChangedEventHandler subscriber in PropertyChanged?.GetInvocationList() ?? [])
            {
                try
                {
                    foreach (string? property in properties)
                        if (property != null) subscriber(this, new PropertyChangedEventArgs(property));
                }
                catch (Exception notificationError)
                {
                    // Stop retrying this subscriber during this failure broadcast; continue with the others.
                    ExceptionPolicy.Describe(notificationError, "IndexSandboxViewModel.FailureNotification", _unexpectedErrors);
                }
            }
        }

        private void NotifyReview()
        {
            OnPropertyChanged(nameof(FullObject));
            OnPropertyChanged(nameof(CompiledIndexName)); OnPropertyChanged(nameof(RollbackStatement));
            OnPropertyChanged(nameof(CanExport)); OnPropertyChanged(nameof(Error)); OnPropertyChanged(nameof(OutputStatus));
        }

        private void LoadAvailableColumns()
        {
            AvailableColumns.Clear();
            if (_originalPlan == null) return;

            // Use the captured QueryPlan and exact object identity.
            var allCols = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

            foreach (var colRef in SqlXmlAnalyzer.Core.Services.IndexTargetResolver.FindColumns(_suggestion, _originalPlan, _ns))
            {
                string table = colRef.Attribute("Table")?.Value ?? "";
                string column = colRef.Attribute("Column")?.Value ?? "";
                if (!string.IsNullOrEmpty(column))
                {
                    allCols.Add(column);
                }
            }

            var currentCols = KeyColumns.Concat(IncludeColumns)
                                        .Select(c => SqlObjectIdentity.DecodeIdentifier(c.Name)!)
                                        .ToHashSet(StringComparer.Ordinal);

            foreach (var col in allCols.OrderBy(c => c))
            {
                if (!currentCols.Contains(col))
                {
                    AvailableColumns.Add(SqlObjectIdentity.Quote(col));
                }
            }
        }

        private void UpdateTippingPointProperties()
        {
            OnPropertyChanged(nameof(TippingPointLow));
            OnPropertyChanged(nameof(TippingPointHigh));
            OnPropertyChanged(nameof(IsCoveredIndex));

            if (!double.IsFinite(TotalRows) || TotalRows <= 0 || !double.IsFinite(AvgRowSize) || AvgRowSize <= 0
                || !double.IsFinite(ReturnedRows) || ReturnedRows < 0 || !double.IsFinite(TippingPointLow) || !double.IsFinite(TippingPointHigh))
            {
                TippingPointStatus = "N/A（假设输入无效）";
                TippingPointStatusColor = "#757575";
                TippingPointDetails = "请输入有限且有效的假设数值；当前不能进行参考区间比较。";
                Logger.Warning("IMP-08: 沙盒假设输入无效，参考区间保持未知。");
            }
            else if (IsCoveredIndex)
            {
                TippingPointStatus = "假设：候选列覆盖（待验证）";
                TippingPointStatusColor = "#757575";
                TippingPointDetails = "按当前列匹配结果，候选索引可能覆盖查询输出列；仍需核对对象和谓词。覆盖并不保证 Seek，也不能排除 Scan 或其他访问路径。";
            }
            else
            {
                double low = TippingPointLow;
                double high = TippingPointHigh;
                double ret = ReturnedRows;

                if (ret > high)
                {
                    TippingPointStatus = "假设：高于参考区间";
                    TippingPointStatusColor = "#757575";
                    TippingPointDetails = "当前假设返回行数高于工具启发式区间，提示核查回表代价；不能据此判断优化器选择 Scan 或已发生性能退化。";
                }
                else if (ret >= low)
                {
                    TippingPointStatus = "假设：处于参考区间";
                    TippingPointStatusColor = "#757575";
                    TippingPointDetails = "当前假设返回行数处于工具启发式区间；实际访问路径取决于参数、统计信息、谓词与成本，不能据此确定 Seek/Scan。";
                }
                else
                {
                    TippingPointStatus = "假设：低于参考区间";
                    TippingPointStatusColor = "#757575";
                    TippingPointDetails = "当前假设返回行数低于工具启发式区间；不能据此保证 Seek、回表次数或性能收益，请检查实际计划。";
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
