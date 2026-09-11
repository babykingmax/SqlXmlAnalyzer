namespace SqlXmlAnalyzer.Core.Configuration;

/// <summary>Each analysis captures one immutable configuration; applying a draft never mutates a running analysis.</summary>
public sealed class RuleConfigurationSession
{
    private RuleConfigurationDocument? _current;
    private readonly string? _initialError;
    public RuleConfigurationSession(RuleConfigurationDocument? initial = null)
    {
        if (initial != null) { _current = initial; return; }
        var loaded = RuleConfigurationLoader.Load();
        _current = loaded.Document;
        _initialError = string.Join(Environment.NewLine, loaded.Errors);
    }
    public RuleConfigurationDocument? Current => Volatile.Read(ref _current);
    public RuleConfigurationDocument Capture() => Current
        ?? throw new InvalidDataException("当前规则配置无效，请先打开规则配置界面修复或应用默认值。" + Environment.NewLine + _initialError);
    public bool Apply(RuleConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var previous = Interlocked.Exchange(ref _current, document);
        return previous?.Fingerprint != document.Fingerprint;
    }
}
