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

        public string Table => _suggestion.Table;

        public ICommand RemoveKeyColumnCommand { get; }
        public ICommand RemoveIncludeColumnCommand { get; }
        public ICommand MoveKeyColumnUpCommand { get; }
        public ICommand MoveKeyColumnDownCommand { get; }

        public IndexSandboxViewModel(MissingIndexSuggestion suggestion, XDocument? originalPlan = null)
        {
            _suggestion = suggestion;
            _originalPlan = originalPlan;
            KeyColumns = new ObservableCollection<IndexColumn>(suggestion.KeyColumns.Select(c => new IndexColumn { Name = c.Name, Usage = c.Usage }));
            IncludeColumns = new ObservableCollection<IndexColumn>(suggestion.IncludeColumns.Select(c => new IndexColumn { Name = c.Name, Usage = c.Usage }));

            RemoveKeyColumnCommand = new RelayCommand(p => 
            {
                if (p is IndexColumn c && KeyColumns.Contains(c)) KeyColumns.Remove(c);
                Recalculate();
            });
            RemoveIncludeColumnCommand = new RelayCommand(p => 
            {
                if (p is IndexColumn c && IncludeColumns.Contains(c)) IncludeColumns.Remove(c);
                Recalculate();
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
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
