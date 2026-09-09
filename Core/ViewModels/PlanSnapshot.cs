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
        public Models.PlanDocument? IdentityModel => Services.PlanIdentityAdapter.GetDocument(Document);
        public Models.PlanQueryPlanKey? SelectedQueryPlan { get; set; }

        public double? TotalCost { get; set; }
        public string? OriginalSourceHash { get; set; }
        // The byte hash belongs to this XML revision, not to arbitrary later edits of Document.
        internal string? OriginalSourceXmlHash { get; set; }
        public string XmlContentHash => ComputeXmlHash(Document);
        internal string? CapturedOriginalSourceHash => NormalizeHash(IdentityModel?.Envelope.SourceHash)
            ?? (OriginalSourceXmlHash == XmlContentHash ? NormalizeHash(OriginalSourceHash) : null);
        internal static string ComputeXmlHash(XDocument document) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(document.Root?.ToString(SaveOptions.DisableFormatting) ?? "")));
        internal static string? NormalizeHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit) ? hash.ToUpperInvariant() : null;
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
