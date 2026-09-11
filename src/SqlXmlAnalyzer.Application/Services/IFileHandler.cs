namespace SqlXmlAnalyzer.Application.Services
{
    public interface IFileHandler
    {
        /// <summary>Opens the original bytes for XML-aware decoding. The caller owns the stream.</summary>
        System.IO.Stream OpenRead(string path);
        string ReadAllText(string path);
        void WriteAllText(string path, string contents);
        bool Exists(string path);
    }
}
