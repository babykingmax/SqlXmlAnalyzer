namespace SqlXmlAnalyzer.Application.Services
{
    public interface IFileHandler
    {
        string ReadAllText(string path);
        void WriteAllText(string path, string contents);
        bool Exists(string path);
    }
}
