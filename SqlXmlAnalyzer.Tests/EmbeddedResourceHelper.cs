using System.IO;
using System.Reflection;

namespace SqlXmlAnalyzer.Tests
{
    public static class EmbeddedResourceHelper
    {
        public static string GetResourceContent(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            // Try to find the exact resource name, assuming default namespace SqlXmlAnalyzer.Tests
            var fullResourceName = $"SqlXmlAnalyzer.Tests.Resources.{resourceName}";
            
            using Stream? stream = assembly.GetManifestResourceStream(fullResourceName);
            if (stream == null)
            {
                // Fallback: search for it
                foreach (var name in assembly.GetManifestResourceNames())
                {
                    if (name.EndsWith(resourceName))
                    {
                        using Stream? fallbackStream = assembly.GetManifestResourceStream(name);
                        if (fallbackStream != null)
                        {
                            using StreamReader reader = new StreamReader(fallbackStream);
                            return reader.ReadToEnd();
                        }
                    }
                }
                throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
            }
            using StreamReader sr = new StreamReader(stream);
            return sr.ReadToEnd();
        }
    }
}
