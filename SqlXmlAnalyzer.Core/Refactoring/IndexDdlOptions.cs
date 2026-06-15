namespace SqlXmlAnalyzer.Core.Refactoring
{
    public class IndexDdlOptions
    {
        public bool Online { get; set; } = true;
        public string DataCompression { get; set; } = "PAGE"; // NONE, ROW, PAGE
        public bool SortInTempDb { get; set; } = true;
        public int? MaxDop { get; set; } = null;
    }
}
