namespace SqlXmlAnalyzer.Core.Refactoring
{
    public class IndexDdlOptions
    {
        // Raw single identifier; empty uses the stable object/column fingerprint.
        public string? IndexName { get; set; }
        public bool Online { get; set; } = true;
        public string DataCompression { get; set; } = "PAGE"; // NONE, ROW, PAGE
        public bool SortInTempDb { get; set; } = true;
        public int? MaxDop { get; set; } = null;
    }
}
