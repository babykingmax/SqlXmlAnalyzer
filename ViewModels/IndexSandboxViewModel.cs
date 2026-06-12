using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Scoring;
using SqlXmlAnalyzer.Core.Mvvm;

namespace SqlXmlAnalyzer.ViewModels
{
    public class IndexSandboxViewModel : INotifyPropertyChanged
    {
        private MissingIndexSuggestion _suggestion;
        
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

        public string Table => _suggestion.Table;

        public ICommand RemoveKeyColumnCommand { get; }
        public ICommand RemoveIncludeColumnCommand { get; }
        public ICommand MoveKeyColumnUpCommand { get; }
        public ICommand MoveKeyColumnDownCommand { get; }

        public IndexSandboxViewModel(MissingIndexSuggestion suggestion)
        {
            _suggestion = suggestion;
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

            // In a full implementation we would pass the actual plan.
            // For now, pass nulls as CalculateScore has heuristic fallbacks.
            IndexScoringCalculator.CalculateScore(temp, null!, null!);
            
            CurrentScore = temp.Score;
            CreateIndexStatement = temp.CreateIndexStatement;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
