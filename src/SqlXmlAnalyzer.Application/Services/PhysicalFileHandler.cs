using System.IO;

namespace SqlXmlAnalyzer.Application.Services
{
    public class PhysicalFileHandler : IFileHandler
    {
        public string ReadAllText(string path)
        {
            return File.ReadAllText(path);
        }

        public void WriteAllText(string path, string contents)
        {
            File.WriteAllText(path, contents);
        }

        public bool Exists(string path)
        {
            return File.Exists(path);
        }
    }
}
