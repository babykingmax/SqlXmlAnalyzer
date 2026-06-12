using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.ViewModels
{
    public class PlanTabViewModel : DocumentTabViewModel
    {
        public XDocument Document { get; }

        public PlanTabViewModel(string title, string filePath, XDocument document)
        {
            Title = title;
            FilePath = filePath;
            Document = document;
        }
    }
}
