using System.IO;
using System.Linq;
using System.Reflection;

namespace SqlXmlAnalyzer.Tests.Utilities
{
    internal static class EmbeddedResourceHelper
    {
        public static string GetResourceContent(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fullName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(resourceName));
            
            if (fullName == null) throw new FileNotFoundException($"Resource {resourceName} not found.");
            
            using var stream = assembly.GetManifestResourceStream(fullName);
            if (stream == null) throw new FileNotFoundException($"Resource stream for {fullName} could not be opened.");
            
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
