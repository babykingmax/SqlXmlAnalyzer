using System.Reflection;

namespace SqlXmlAnalyzer.Core
{
    public static class ProductInfo
    {
        public static string Version { get; } = ResolveVersion();

        private static string ResolveVersion()
        {
            Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(ProductInfo).Assembly;
            string? informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                int metadataSeparator = informationalVersion.IndexOf('+');
                return metadataSeparator >= 0
                    ? informationalVersion[..metadataSeparator]
                    : informationalVersion;
            }

            return assembly.GetName().Version?.ToString(3) ?? "unknown";
        }
    }
}
