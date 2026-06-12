using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.ViewModels
{
    public class DeadlockTabViewModel : DocumentTabViewModel
    {
        public XDocument Document { get; }

        public DeadlockTabViewModel(string title, string filePath, XDocument document)
        {
            Title = title;
            FilePath = filePath;
            Document = document;
        }
    }
}
