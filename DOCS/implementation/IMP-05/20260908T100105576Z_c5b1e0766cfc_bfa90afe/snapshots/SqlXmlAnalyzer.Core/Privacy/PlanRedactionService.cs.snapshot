using System.Globalization;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Core.Privacy;

public sealed class PlanRedactionService
{
    public const string ShowPlanNamespace = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
    private readonly IUnexpectedErrorReporter _diagnostics;
    private readonly Func<XDocument, XDocument> _clone;
    public PlanRedactionService(IUnexpectedErrorReporter? diagnostics = null) : this(diagnostics, document => new XDocument(document)) { }
    internal PlanRedactionService(IUnexpectedErrorReporter? diagnostics, Func<XDocument, XDocument> clone)
    {
        _diagnostics = diagnostics ?? UnexpectedErrorReporter.Shared;
        _clone = clone;
    }

    public PlanRedactionResult Redact(XDocument original, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        var masked = new Dictionary<string, int>(StringComparer.Ordinal);
        var unsupported = new Dictionary<string, int>(StringComparer.Ordinal);
        void Count(Dictionary<string, int> counts, string category) => counts[category] = counts.GetValueOrDefault(category) + 1;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Logger.Debug("PlanRedaction started.");
            if (original.Root?.Name != XName.Get("ShowPlanXML", ShowPlanNamespace) || original.DocumentType != null)
            {
                Count(unsupported, "UnsupportedDocument");
                Logger.Warning("PlanRedaction blocked: UnsupportedDocument.");
                return new(PlanRedactionStatus.Blocked, null, masked, unsupported);
            }
            var copy = _clone(original);
            copy.Declaration = new XDeclaration("1.0", "utf-8", null);
            var mapping = new Dictionary<(string Kind, string Value), string>();
            foreach (var element in copy.Descendants())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (element.Name.NamespaceName != ShowPlanNamespace || !ShowPlanFieldCatalog.Elements.Contains(element.Name.LocalName))
                    Count(unsupported, "UnknownElementOrNamespace");
                foreach (var attribute in element.Attributes().ToArray())
                {
                    if (attribute.IsNamespaceDeclaration)
                    {
                        // Re-serialize with canonical namespace declarations; prefixes may contain identifiers.
                        if (attribute.Value != ShowPlanNamespace) Count(unsupported, "UnknownNamespaceDeclaration");
                        attribute.Remove();
                        continue;
                    }
                    string name = attribute.Name.LocalName;
                    if (attribute.Name.NamespaceName.Length != 0 || !ShowPlanFieldCatalog.Attributes.TryGetValue(name, out var field))
                    {
                        Count(unsupported, "UnknownAttributeOrNamespace");
                        continue;
                    }
                    if (field.Kind != ShowPlanFieldCatalog.FieldKind.Text)
                    {
                        bool valid = field.Kind switch
                        {
                            ShowPlanFieldCatalog.FieldKind.Boolean => attribute.Value is "true" or "false" or "0" or "1",
                            ShowPlanFieldCatalog.FieldKind.Number => attribute.Value.Length <= 64 &&
                                attribute.Value.All(c => char.IsAsciiDigit(c) || c is '+' or '-' or '.' or 'e' or 'E') &&
                                double.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value),
                            ShowPlanFieldCatalog.FieldKind.Enum => field.Values!.Contains(attribute.Value),
                            _ => false
                        };
                        if (!valid) Count(unsupported, "InvalidStructuralValue");
                        continue;
                    }
                    string kind = name switch
                    {
                        "Server" or "Database" or "Schema" or "Table" or "Index" or "Alias" or "Column" or
                        "Statistics" or "ProcName" or "CursorName" or "FunctionName" or "Assembly" or "Class" or "Method" => name,
                        "StatementText" or "ParameterizedText" or "RemoteQuery" or "Script" or "QueryStoreStatementHintText" => "SqlText",
                        "ParameterCompiledValue" or "ParameterRuntimeValue" => "ParameterValue",
                        "ConstValue" => "Constant",
                        "ScalarString" or "Expression" => "Expression",
                        _ => "OtherText"
                    };
                    if (kind is "SqlText" or "ParameterValue" or "Constant" or "Expression" or "OtherText")
                        attribute.Value = kind == "SqlText" ? "-- 已脱敏 SQL --" : "[MASKED_" + kind + "]";
                    else
                    {
                        string raw = attribute.Value;
                        bool quoted = raw.StartsWith('[') && raw.EndsWith(']');
                        string identity = quoted ? raw[1..^1].Replace("]]", "]", StringComparison.Ordinal) : raw;
                        var key = (kind, identity); // Ordinal comparison; do not assume database collation.
                        if (!mapping.TryGetValue(key, out string? replacement))
                            mapping[key] = replacement = $"Masked_{kind}_{mapping.Count + 1}";
                        attribute.Value = quoted ? $"[{replacement}]" : replacement;
                    }
                    Count(masked, kind);
                }
            }
            foreach (var node in copy.DescendantNodes().ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (node is XComment or XProcessingInstruction)
                {
                    Count(masked, "CommentOrProcessingInstruction"); node.Remove();
                }
                else if (node is XText text && !string.IsNullOrWhiteSpace(text.Value))
                {
                    Count(masked, "TextOrCData"); text.Value = "[MASKED_TEXT]";
                }
            }
            if (unsupported.Count > 0)
            {
                Logger.Warning($"PlanRedaction blocked: {unsupported.Values.Sum()} unsupported items.");
                return new(PlanRedactionStatus.Blocked, null, masked, unsupported);
            }
            cancellationToken.ThrowIfCancellationRequested();
            string xml = copy.ToString(SaveOptions.DisableFormatting);
            Logger.Debug($"PlanRedaction completed: {masked.Values.Sum()} masked items.");
            return new(PlanRedactionStatus.Ready, xml, masked, unsupported);
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("PlanRedaction cancelled.");
            return new(PlanRedactionStatus.Cancelled, null, masked, unsupported);
        }
        catch (Exception exception)
        {
            // No user XML, identifiers or parser error text in public/log failure messages.
            UnexpectedErrorReport? diagnostic = ExceptionPolicy.IsExpected(exception) ? null :
                ExceptionPolicy.Capture(exception, "PlanRedaction", _diagnostics);
            Logger.Error("PlanRedaction failed; no export payload was produced.");
            return new(PlanRedactionStatus.Failed, null, masked, unsupported, diagnostic);
        }
    }
}
