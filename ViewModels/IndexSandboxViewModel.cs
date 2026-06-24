using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Scoring;
using SqlXmlAnalyzer.Core.Simulation;
using SqlXmlAnalyzer.Core.Mvvm;

namespace SqlXmlAnalyzer.ViewModels
{
    public class IndexSandboxViewModel : INotifyPropertyChanged
    {
        private MissingIndexSuggestion _suggestion;
        private XDocument? _originalPlan;
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

        private int _estimatedCostReductionPercent;
        public int EstimatedCostReductionPercent
        {
            get => _estimatedCostReductionPercent;
            set
            {
                _estimatedCostReductionPercent = value;
                OnPropertyChanged();
            }
        }

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

                string normTable = (_suggestion.Table ?? "").Trim('[', ']');
                var outputCols = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var relOp in _originalPlan.Descendants(_ns + "RelOp"))
                {
                    var obj = relOp.Descendants(_ns + "Object").FirstOrDefault();
                    if (obj != null && string.Equals(obj.Attribute("Table")?.Value?.Trim('[', ']'), normTable, StringComparison.OrdinalIgnoreCase))
                    {
                        var outputList = relOp.Element(_ns + "OutputList");
                        if (outputList != null)
                        {
                            foreach (var colRef in outputList.Descendants(_ns + "ColumnReference"))
                            {
                                string colName = colRef.Attribute("Column")?.Value ?? "";
                                if (!string.IsNullOrEmpty(colName))
                                {
                                    outputCols.Add(colName.Trim('[', ']'));
                                }
                            }
                        }
                    }
                }

                if (outputCols.Count == 0) return false;

                var indexCols = KeyColumns.Concat(IncludeColumns)
                                          .Select(c => c.Name.Trim('[', ']'))
                                          .ToHashSet(StringComparer.OrdinalIgnoreCase);
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

        public IndexSandboxViewModel(MissingIndexSuggestion suggestion, XDocument? originalPlan = null)
        {
            _suggestion = suggestion;
            _originalPlan = originalPlan;
            KeyColumns = new ObservableCollection<IndexColumn>(suggestion.KeyColumns.Select(c => new IndexColumn { Name = c.Name, Usage = c.Usage }));
            IncludeColumns = new ObservableCollection<IndexColumn>(suggestion.IncludeColumns.Select(c => new IndexColumn { Name = c.Name, Usage = c.Usage }));

            _totalRows = FindTableCardinality();
            _avgRowSize = FindAvgRowSize();
            _returnedRows = FindEstimatedReturnedRows();

            RemoveKeyColumnCommand = new RelayCommand(p =>
            {
                if (p is IndexColumn c && KeyColumns.Contains(c))
                {
                    KeyColumns.Remove(c);
                    string colFormatted = c.Name.StartsWith("[") ? c.Name : $"[{c.Name}]";
                    if (!AvailableColumns.Contains(colFormatted))
                        AvailableColumns.Add(colFormatted);
                    Recalculate();
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
                    Recalculate();
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
                        Recalculate();
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
                        Recalculate();
                    }
                }
            });

            AddKeyColumnCommand = new RelayCommand(p =>
            {
                if (p is string colName && AvailableColumns.Contains(colName))
                {
                    string usage = "EQUALITY";
                    string rawName = colName.Trim('[', ']');
                    var orig = suggestion.KeyColumns.FirstOrDefault(c => string.Equals(c.Name.Trim('[', ']'), rawName, StringComparison.OrdinalIgnoreCase));
                    if (orig != null) usage = orig.Usage;

                    KeyColumns.Add(new IndexColumn { Name = colName, Usage = usage });
                    AvailableColumns.Remove(colName);
                    Recalculate();
                }
            });

            AddIncludeColumnCommand = new RelayCommand(p =>
            {
                if (p is string colName && AvailableColumns.Contains(colName))
                {
                    IncludeColumns.Add(new IndexColumn { Name = colName, Usage = "INCLUDE" });
                    AvailableColumns.Remove(colName);
                    Recalculate();
                }
            });

            LoadAvailableColumns();
            Recalculate();
        }

        public void Recalculate()
        {
            var temp = new MissingIndexSuggestion
            {
                Schema = _suggestion.Schema,
                Table = _suggestion.Table,
                Impact = _suggestion.Impact,
                KeyColumns = KeyColumns.ToList(),
                IncludeColumns = IncludeColumns.ToList()
            };

            IndexScoringCalculator.CalculateScore(temp, _originalPlan!, _ns);

            CurrentScore = temp.Score;
            CreateIndexStatement = temp.CreateIndexStatement;

            var costResult = CostImpactSimulator.Simulate(_originalPlan, temp, _ns);
            EstimatedCostReductionPercent = costResult.ReductionPercent;
            CostReductionDescription = costResult.Description;

            UpdateTippingPointProperties();
        }

        private void LoadAvailableColumns()
        {
            AvailableColumns.Clear();
            if (_originalPlan == null) return;

            string normTable = (_suggestion.Table ?? "").Trim('[', ']');
            var allCols = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var colRef in _originalPlan.Descendants(_ns + "ColumnReference"))
            {
                string table = colRef.Attribute("Table")?.Value ?? "";
                string column = colRef.Attribute("Column")?.Value ?? "";
                if (string.Equals(table.Trim('[', ']'), normTable, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(column))
                {
                    allCols.Add(column.Trim('[', ']'));
                }
            }

            var currentCols = KeyColumns.Concat(IncludeColumns)
                                        .Select(c => c.Name.Trim('[', ']'))
                                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var col in allCols.OrderBy(c => c))
            {
                if (!currentCols.Contains(col))
                {
                    AvailableColumns.Add($"[{col}]");
                }
            }
        }

        private double FindTableCardinality()
        {
            if (_originalPlan == null) return 100000;

            string normTable = (_suggestion.Table ?? "").Trim('[', ']');
            foreach (var relOp in _originalPlan.Descendants(_ns + "RelOp"))
            {
                var obj = relOp.Descendants(_ns + "Object").FirstOrDefault();
                if (obj != null && string.Equals(obj.Attribute("Table")?.Value?.Trim('[', ']'), normTable, StringComparison.OrdinalIgnoreCase))
                {
                    string? cardStr = relOp.Attribute("TableCardinality")?.Value;
                    if (!string.IsNullOrEmpty(cardStr) && double.TryParse(cardStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double card))
                    {
                        if (card > 0) return card;
                    }
                }
            }
            return 100000;
        }

        private double FindAvgRowSize()
        {
            if (_originalPlan == null) return 200;

            string normTable = (_suggestion.Table ?? "").Trim('[', ']');
            foreach (var relOp in _originalPlan.Descendants(_ns + "RelOp"))
            {
                var obj = relOp.Descendants(_ns + "Object").FirstOrDefault();
                if (obj != null && string.Equals(obj.Attribute("Table")?.Value?.Trim('[', ']'), normTable, StringComparison.OrdinalIgnoreCase))
                {
                    string? rowSizeStr = relOp.Attribute("AvgRowSize")?.Value;
                    if (!string.IsNullOrEmpty(rowSizeStr) && double.TryParse(rowSizeStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double rowSize))
                    {
                        if (rowSize > 0) return rowSize;
                    }
                }
            }
            return 200;
        }

        private double FindEstimatedReturnedRows()
        {
            if (_originalPlan == null) return 1000;

            string normTable = (_suggestion.Table ?? "").Trim('[', ']');
            foreach (var relOp in _originalPlan.Descendants(_ns + "RelOp"))
            {
                var obj = relOp.Descendants(_ns + "Object").FirstOrDefault();
                if (obj != null && string.Equals(obj.Attribute("Table")?.Value?.Trim('[', ']'), normTable, StringComparison.OrdinalIgnoreCase))
                {
                    string? rowsStr = relOp.Attribute("EstimateRows")?.Value;
                    if (!string.IsNullOrEmpty(rowsStr) && double.TryParse(rowsStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double rows))
                    {
                        if (rows > 0) return rows;
                    }
                }
            }
            return 1000;
        }

        private void UpdateTippingPointProperties()
        {
            OnPropertyChanged(nameof(TippingPointLow));
            OnPropertyChanged(nameof(TippingPointHigh));
            OnPropertyChanged(nameof(IsCoveredIndex));

            if (IsCoveredIndex)
            {
                TippingPointStatus = "🟢 安全 (覆盖索引)";
                TippingPointStatusColor = "#2E7D32";
                TippingPointDetails = "提示: 当前索引已包含该表在查询中所需的所有列。由于不需要进行 Key Lookup (回表) 操作，因此不存在 Tipping Point 导致退化的风险。优化器将始终选择 Seek。";
            }
            else
            {
                double low = TippingPointLow;
                double high = TippingPointHigh;
                double ret = ReturnedRows;

                if (ret > high)
                {
                    TippingPointStatus = "🔴 已触发退化";
                    TippingPointStatusColor = "#D32F2F";
                    TippingPointDetails = $"警告: 查询预计返回 {ret:N0} 行，已超过 Tipping Point 临界线（{high:N0} 行）。SQL Server 将放弃索引 Seek，退化为全表扫描 (Scan)。建议将缺失的输出列加入包含列中，升级为覆盖索引。";
                }
                else if (ret >= low)
                {
                    TippingPointStatus = "🟡 处于临界区";
                    TippingPointStatusColor = "#EF6C00";
                    TippingPointDetails = $"注意: 查询预计返回 {ret:N0} 行，处于 Tipping Point 临界区间 ({low:N0} ~ {high:N0} 行)。取决于参数和统计信息，随时可能退化为全表扫描。建议考虑转为覆盖索引。";
                }
                else
                {
                    TippingPointStatus = "🟢 安全 (行数正常)";
                    TippingPointStatusColor = "#2E7D32";
                    TippingPointDetails = $"正常: 查询预计返回 {ret:N0} 行，低于 Tipping Point 阈值 ({low:N0} 行)。优化器会选择 Index Seek 并进行 {ret:N0} 次 Key Lookup。";
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
