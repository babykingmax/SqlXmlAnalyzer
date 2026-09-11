using System.Text;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Core.Configuration;

public class RuleConfig
{
    public string RuleId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string? SeverityOverride { get; set; }
}
public class RuleConfigurationRoot
{
    public List<RuleConfig> Rules { get; set; } = new();
}
public sealed record RuleConfigurationLoadResult(RuleConfigurationRoot Configuration, string ResolvedPath,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> Errors, bool IsExplicitPath)
{
    public bool IsSuccess => Errors.Count == 0;
    public RuleConfigurationDocument? Document { get; init; }
}
public static class RuleConfigurationPathResolver
{
    public const string DefaultFileName = "RuleConfiguration.json";
    public static string Resolve(string? configPath = null) => string.IsNullOrWhiteSpace(configPath)
        ? Path.Combine(AppContext.BaseDirectory, DefaultFileName) : Path.GetFullPath(configPath);
}
public static class RuleConfigurationLoader
{
    public static RuleConfigurationLoadResult Load(string? configPath = null, IUnexpectedErrorReporter? unexpectedErrors = null)
    {
        bool explicitPath = !string.IsNullOrWhiteSpace(configPath);
        string path = configPath ?? "";
        try
        {
            try { path = RuleConfigurationPathResolver.Resolve(configPath); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
            { throw new InvalidDataException("Rule configuration path is invalid.", exception); }
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > RuleConfigurationDocument.MaxBytes) throw new InvalidDataException("规则配置超过 1 MiB 文件预算。");
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                int count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, RuleConfigurationDocument.MaxBytes - bytes.Length + 1));
                if (count == 0) break;
                bytes.Write(buffer, 0, count);
                if (bytes.Length > RuleConfigurationDocument.MaxBytes) throw new InvalidDataException("规则配置超过 1 MiB 文件预算。");
            }
            var document = RuleConfigurationDocument.Parse(Decode(bytes.ToArray()));
            Logger.Debug($"IMP24 configuration loaded: known={document.ToLegacy().Rules.Count}, unknown={document.UnknownRules.Count}.");
            return new(document.ToLegacy(), path, document.Warnings, [], explicitPath) { Document = document };
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            const string message = "Rule configuration file does not exist（规则配置文件不存在）。";
            if (!explicitPath)
            {
                Logger.Warning("IMP24 default configuration missing; using built-in defaults.");
                return new(new(), path, [message + " Built-in default rule settings will be used."], [], false)
                    { Document = RuleConfigurationDocument.Defaults };
            }
            Logger.Error(message);
            return new(new(), path, [], [message], true);
        }
        catch (Exception exception)
        {
            string error = ExceptionPolicy.Describe(exception, "IMP24.RuleConfiguration.Load", unexpectedErrors);
            return new(new(), path, [], error.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries), explicitPath);
        }
    }

    // StreamReader BOM detection can select a replacement-fallback decoder. Reject
    // undecodable input consistently with the editor's byte-preserving snapshot.
    private static string Decode(byte[] bytes)
    {
        ReadOnlySpan<byte> prefix = bytes;
        (Encoding Encoding, int Offset) decoder = prefix.StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 }) ? (new UTF32Encoding(false, true, true), 4)
            : prefix.StartsWith(new byte[] { 0, 0, 0xFE, 0xFF }) ? (new UTF32Encoding(true, true, true), 4)
            : prefix.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? (new UTF8Encoding(true, true), 3)
            : prefix.StartsWith(new byte[] { 0xFF, 0xFE }) ? (new UnicodeEncoding(false, true, true), 2)
            : prefix.StartsWith(new byte[] { 0xFE, 0xFF }) ? (new UnicodeEncoding(true, true, true), 2)
            : (new UTF8Encoding(false, true), 0);
        return decoder.Encoding.GetString(bytes, decoder.Offset, bytes.Length - decoder.Offset);
    }
}
