using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SqlXmlAnalyzer.Core.Rules;

namespace SqlXmlAnalyzer.Core.Configuration;

public sealed record UnknownRuleConfiguration(string RuleId, string Location, string Json);

/// <summary>Immutable configuration, including unrecognized JSON that this version must not discard.</summary>
public sealed class RuleConfigurationDocument
{
    public const string CurrentSchemaVersion = "1";
    public const string SchemaVersionProperty = "ConfigurationSchemaVersion";
    public const int MaxBytes = 1024 * 1024;
    public const int MaxRules = 1024;
    private static readonly JsonDocumentOptions ReadOptions = new() { MaxDepth = 32 };
    private readonly IReadOnlyDictionary<string, RuleConfigurationSnapshot> _settings;
    private RuleConfigurationDocument(string json, IEnumerable<RuleConfigurationSnapshot> settings,
        IEnumerable<UnknownRuleConfiguration> unknown, IEnumerable<string> warnings, string? schemaVersion)
    {
        Json = json;
        SchemaVersion = schemaVersion;
        _settings = new ReadOnlyDictionary<string, RuleConfigurationSnapshot>(settings.ToDictionary(s => s.RuleId, StringComparer.Ordinal));
        UnknownRules = Array.AsReadOnly(unknown.ToArray()); Warnings = Array.AsReadOnly(warnings.ToArray());
        Fingerprint = ComputeFingerprint(_settings.Values);
    }
    public static RuleConfigurationDocument Defaults { get; } = Parse("{\"Rules\":[]}");
    public string Json { get; }
    /// <summary>Version declared by ConfigurationSchemaVersion. Generic schemaVersion remains a legacy extension.</summary>
    public string? SchemaVersion { get; }
    public string Fingerprint { get; }
    public IReadOnlyList<UnknownRuleConfiguration> UnknownRules { get; }
    public IReadOnlyList<string> Warnings { get; }
    public RuleConfigurationSnapshot Get(string ruleId) => _settings.TryGetValue(ruleId, out var value)
        ? value : new(ruleId, true, null);
    public RuleConfigurationRoot ToLegacy() => new() { Rules = _settings.Values.Select(s =>
        new RuleConfig { RuleId = s.RuleId, Enabled = s.Enabled, SeverityOverride = s.SeverityOverride }).ToList() };

    public static string ComputeFingerprint(IEnumerable<RuleConfigurationSnapshot> settings)
    {
        var values = settings.ToDictionary(s => s.RuleId, StringComparer.Ordinal);
        string text = string.Join("\n", RuleMetadataCatalog.RegisteredRuleIds.OrderBy(id => id, StringComparer.Ordinal).Select(id =>
            values.TryGetValue(id, out var value) ? $"{id}:{value.Enabled}:{value.SeverityOverride ?? "default"}" : $"{id}:True:default"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    public static RuleConfigurationDocument Parse(string json, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (new UTF8Encoding(false, true).GetByteCount(json) > MaxBytes) throw new InvalidDataException("规则配置超过 1 MiB 预算。");
            using var document = JsonDocument.Parse(json, ReadOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("$：规则配置必须为 JSON 对象。");
            var errors = new List<string>(); var warnings = new List<string>();
            var settings = new List<RuleConfigurationSnapshot>(); var unknown = new List<UnknownRuleConfiguration>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ValidateJson(root, new List<JsonPathSegment>(32), errors, cancellationToken);
            var version = Property(root, SchemaVersionProperty, "$", errors);
            if (version.HasValue && (version.Value.ValueKind != JsonValueKind.String || version.Value.GetString() != CurrentSchemaVersion))
                throw new InvalidDataException("CONFIG_SCHEMA_UNSUPPORTED: 不支持此规则配置版本；请保留原件并使用匹配版本的应用。");
            var rules = Property(root, "Rules", "$", errors);
            if (rules.HasValue && rules.Value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null))
                errors.Add("$.Rules：必须为数组或 null。");
            if (rules?.ValueKind == JsonValueKind.Array)
            {
                if (rules.Value.GetArrayLength() > MaxRules) throw new InvalidDataException("$.Rules：超过 1024 项预算。");
                int index = 0;
                foreach (var rule in rules.Value.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string path = $"$.Rules[{index++}]";
                    if (rule.ValueKind != JsonValueKind.Object) { errors.Add(path + " cannot be null; 必须为对象。"); continue; }
                    var idNode = Property(rule, "RuleId", path, errors);
                    string id = idNode?.ValueKind == JsonValueKind.String ? idNode.Value.GetString()!.Trim() : "";
                    if (id.Length == 0 || id.Length > 256) { errors.Add(path + ".RuleId cannot be empty; 必须为 1–256 字符的字符串。"); continue; }
                    bool known = RuleMetadataCatalog.TryNormalizeRuleId(id, out string normalized, out string? warning);
                    if (!seen.Add(known ? normalized : id)) errors.Add(path + ".RuleId：Duplicate RuleId: " + id);
                    if (!known)
                    {
                        unknown.Add(new(id, path, rule.GetRawText()));
                        warnings.Add(path + ".RuleId：Unknown RuleId: " + id + "；原样保留，本版本不执行。");
                        continue;
                    }
                    if (warning != null) warnings.Add(path + ".RuleId: " + warning);
                    var enabledNode = Property(rule, "Enabled", path, errors);
                    bool enabled = true;
                    if (enabledNode.HasValue)
                    {
                        if (enabledNode.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) enabled = enabledNode.Value.GetBoolean();
                        else errors.Add(path + ".Enabled：必须为 true 或 false。");
                    }
                    var severityNode = Property(rule, "SeverityOverride", path, errors);
                    string? severity = null;
                    if (severityNode.HasValue && severityNode.Value.ValueKind != JsonValueKind.Null)
                    {
                        if (severityNode.Value.ValueKind != JsonValueKind.String) errors.Add(path + ".SeverityOverride：必须为字符串或 null。");
                        else
                        {
                            severity = severityNode.Value.GetString()?.Trim();
                            severity = string.IsNullOrEmpty(severity) ? null : severity.ToUpperInvariant() switch
                            { "INFO" => "Info", "WARNING" => "Warning", "CRITICAL" => "Critical", _ => "invalid" };
                            if (severity == "invalid") errors.Add(path + ".SeverityOverride：Allowed values are Info, Warning, Critical 或 null（默认）。");
                        }
                    }
                    settings.Add(new(normalized, enabled, severity));
                }
            }
            if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors.Take(64)));
            return new(json, settings, unknown, warnings, version?.GetString());
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"规则配置包含 invalid JSON：{exception.Path ?? "$"}，行 {(exception.LineNumber ?? 0) + 1}，字节 {(exception.BytePositionInLine ?? 0) + 1}。", exception);
        }
        catch (EncoderFallbackException exception)
        {
            throw new InvalidDataException("$：规则配置包含无效 Unicode 字符。", exception);
        }
    }

    private static JsonElement? Property(JsonElement element, string name, string path, List<string> errors)
    {
        var matches = element.EnumerateObject().Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1) errors.Add(path + "." + name + "：属性重复（忽略大小写）。");
        return matches.Length == 0 ? null : matches[0].Value;
    }
    private readonly record struct JsonPathSegment(string? Name, int Index);

    // Keep only segments during traversal: copying the full path at every child
    // multiplies a long property name by the number of descendants.
    private static void ValidateJson(JsonElement element, List<JsonPathSegment> path, List<string> errors,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (errors.Count >= 64) throw new InvalidDataException("配置错误过多；" + string.Join(Environment.NewLine, errors));
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            int index = 0;
            foreach (var property in element.EnumerateObject())
            {
                string name;
                try { name = property.Name; }
                catch (InvalidOperationException exception)
                {
                    // JsonDocument accepts escaped lone surrogates; decoding a name
                    // or a string then throws InvalidOperationException in .NET 8.
                    throw new InvalidDataException($"{FormatPath(path)}：第 {index + 1} 个属性名包含无效 Unicode。", exception);
                }
                path.Add(new(name, 0));
                if (!names.Add(name)) errors.Add(FormatPath(path) + "：存在重复 JSON 属性。");
                ValidateJson(property.Value, path, errors, cancellationToken);
                path.RemoveAt(path.Count - 1);
                index++;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var item in element.EnumerateArray())
            {
                path.Add(new(null, index++));
                ValidateJson(item, path, errors, cancellationToken);
                path.RemoveAt(path.Count - 1);
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            // Validate unknown values too so a later edit cannot fail while
            // serializing a configuration that was previously accepted.
            try { _ = element.GetString(); }
            catch (InvalidOperationException exception)
            {
                throw new InvalidDataException(FormatPath(path) + "：字符串包含无效 Unicode。", exception);
            }
        }
    }

    private static string FormatPath(List<JsonPathSegment> path)
    {
        var text = new StringBuilder("$");
        foreach (var segment in path)
        {
            if (segment.Name is not { } name) text.Append('[').Append(segment.Index).Append(']');
            else
            {
                string label = name.Length <= 64 ? name : name[..64] + "…";
                if (label.Length > 0 && label.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) text.Append('.').Append(label);
                else text.Append('[').Append(JsonSerializer.Serialize(label)).Append(']');
            }
            if (text.Length > 1024) return text.ToString(0, 1024) + "…";
        }
        return text.ToString();
    }

    public RuleConfigurationDocument WithSettings(IEnumerable<RuleConfigurationSnapshot> changes)
    {
        var requested = changes.ToDictionary(s => s.RuleId, StringComparer.Ordinal);
        if (requested.Keys.Any(id => !RuleMetadataCatalog.RegisteredRuleIds.Contains(id)))
            throw new InvalidDataException("未知规则项为只读，不可由本版本编辑。");
        foreach (string id in requested.Keys.Where(id => requested[id] == Get(id)).ToArray()) requested.Remove(id);
        if (requested.Count == 0) return this;
        var root = JsonNode.Parse(Json, documentOptions: ReadOptions)!.AsObject();
        static string Key(JsonObject obj, string name) => obj.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) ?? name;
        string rulesKey = Key(root, "Rules");
        if (root[rulesKey] is not JsonArray rules) { rules = new JsonArray(); root[rulesKey] = rules; }
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in rules)
        {
            var row = node!.AsObject();
            string id = row[Key(row, "RuleId")]!.GetValue<string>().Trim();
            if (!RuleMetadataCatalog.TryNormalizeRuleId(id, out string normalized, out _) || !requested.TryGetValue(normalized, out var setting)) continue;
            present.Add(normalized);
            row[Key(row, "Enabled")] = setting.Enabled;
            row[Key(row, "SeverityOverride")] = setting.SeverityOverride;
        }
        foreach (var setting in requested.Values.Where(s => !present.Contains(s.RuleId) && (!s.Enabled || s.SeverityOverride != null)))
            rules.Add(new JsonObject { ["RuleId"] = setting.RuleId, ["Enabled"] = setting.Enabled, ["SeverityOverride"] = setting.SeverityOverride });
        return Parse(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Prepares a versioned copy; the caller must save to a new path to retain the original.</summary>
    public RuleConfigurationDocument WithCurrentSchema()
    {
        if (SchemaVersion == CurrentSchemaVersion) return this;
        // Json is immutable and Parse has already validated a single root object.
        // Insert only the new member: serializing its existing members can expand
        // whitespace/escaping and change the representation of unknown values.
        int insertion = Json.IndexOf('{') + 1;
        int firstMember = insertion;
        while (Json[firstMember] is ' ' or '\t' or '\r' or '\n') firstMember++;
        string member = JsonSerializer.Serialize(SchemaVersionProperty) + ":" + JsonSerializer.Serialize(CurrentSchemaVersion)
            + (Json[firstMember] == '}' ? "" : ",");
        var utf8 = new UTF8Encoding(false, true);
        if ((long)utf8.GetByteCount(Json) + utf8.GetByteCount(member) > MaxBytes)
            throw new InvalidDataException("CONFIG_MIGRATION_BUDGET_EXCEEDED: 添加版本字段后的规则配置超过 1 MiB 预算；原配置未更改。");
        var converted = Parse(Json.Insert(insertion, member));
        Logger.Debug("IMP28 configuration versioned copy prepared; executable settings unchanged.");
        return converted;
    }
}
