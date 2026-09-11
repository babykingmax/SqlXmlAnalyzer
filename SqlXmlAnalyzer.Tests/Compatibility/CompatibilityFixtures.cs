using System.IO;
using System.Reflection;

namespace SqlXmlAnalyzer.Tests.Compatibility;

internal static class CompatibilityFixtures
{
    public static string Read(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "SqlXmlAnalyzer.Tests.TestData.Compatibility." + name)
            ?? throw new FileNotFoundException("Missing compatibility fixture: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
