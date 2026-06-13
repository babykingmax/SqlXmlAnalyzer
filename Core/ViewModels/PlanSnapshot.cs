using System;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Mvvm;

namespace SqlXmlAnalyzer.Core.ViewModels
{
    public class PlanSnapshot : ObservableObject
    {
        public string Id { get; } = Guid.NewGuid().ToString();

        private string _title = "Snapshot";
        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string FilePath { get; set; } = string.Empty;
        public DateTime CaptureTime { get; set; } = DateTime.Now;
        public XDocument Document { get; set; } = null!;
        
        public double TotalCost { get; set; }
        public int OperatorCount { get; set; }
        public int MissingIndexCount { get; set; }
        public string StatementText { get; set; } = string.Empty;

        // Truncated version of statement text for ListView display
        public string ShortStatementText
        {
            get
            {
                if (string.IsNullOrEmpty(StatementText)) return string.Empty;
                return StatementText.Length > 60 ? StatementText.Substring(0, 57) + "..." : StatementText;
            }
        }
    }
}
