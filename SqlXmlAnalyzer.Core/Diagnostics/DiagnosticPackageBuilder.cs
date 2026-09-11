using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SqlXmlAnalyzer.Core.Configuration;
using SqlXmlAnalyzer.Core.Models;
using SqlXmlAnalyzer.Core.Reporting;
using SqlXmlAnalyzer.Core.Rules;
using SqlXmlAnalyzer.Core.Services;

namespace SqlXmlAnalyzer.Core.Diagnostics;

/// <summary>Explicit local collection only. Arbitrary configuration extensions, exception messages,
/// paths, SQL and before-redaction samples are never copied into the default package.</summary>
public sealed class DiagnosticPackageBuilder(IUnexpectedErrorReporter? unexpectedErrors = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public DiagnosticPackagePreparation Prepare(DiagnosticReport? report, RuleConfigurationDocument? activeConfiguration,
        AnalysisProgress? progress, DiagnosticPackageOptions options, CancellationToken token = default)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            Logger.Debug("IMP27.Package.Prepare started.");
            var entries = new List<DiagnosticPackageEntry>();
            void Add(string name, byte[] bytes, string privacy, bool binary = false)
            {
                token.ThrowIfCancellationRequested();
                if (bytes.LongLength > DiagnosticPackage.MaxEntryBytes ||
                    entries.Sum(e => e.Bytes) + bytes.LongLength > DiagnosticPackage.MaxPackageBytes)
                    throw new InvalidDataException("诊断包超过单项 64 MiB 或总计 128 MiB 预算；请减少所选内容。");
                entries.Add(new(name, bytes, privacy, binary));
            }
            void Json(string name, object value) => Add(name, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), "AllowlistedMetadata");

            var missing = new List<object>();
            void Missing(string code, string description) => missing.Add(new { Code = code, Description = description });
            string? Field(string name) => report?.Metadata.FirstOrDefault(f => f.Name == name)?.Value;
            var analysisRules = ReadAnalysisConfiguration(report);
            string? analysisFingerprint = analysisRules == null ? null : RuleConfigurationDocument.ComputeFingerprint(analysisRules);
            var currentRules = activeConfiguration == null ? null : RuleMetadataCatalog.RegisteredRuleIds
                .OrderBy(id => id, StringComparer.Ordinal).Select(activeConfiguration.Get).ToArray();
            if (report == null) Missing("ANALYSIS_SNAPSHOT_UNAVAILABLE", "没有可用的所选语句/事件分析快照；包仅描述本地环境与会话状态。");
            if (analysisRules == null) Missing("ANALYSIS_CONFIGURATION_UNAVAILABLE", "所选报告未记录可用的计划规则配置；当前会话配置不代表报告生成时配置。");
            if (activeConfiguration == null) Missing("ACTIVE_CONFIGURATION_UNAVAILABLE", "当前会话规则配置不可用。");
            if (activeConfiguration?.UnknownRules.Count > 0 || activeConfiguration?.Warnings.Count > 0)
                Missing("CONFIGURATION_EXTENSIONS_OMITTED", "配置中的未知规则、扩展字段及自由文本不收集；仅收集本版本执行的有效规则设置。");
            bool configurationChanged = analysisFingerprint != null && activeConfiguration != null && analysisFingerprint != activeConfiguration.Fingerprint;
            if (configurationChanged) Missing("CONFIGURATION_CHANGED", "当前会话配置已改变，所选报告仍使用旧配置；需要重新分析。");

            string? engineBuild = NumericVersion(Field("EngineBuild"));
            string? schema = NumericVersion(Field("SourceSchema"));
            string? capturedAt = DateTimeOffset.TryParseExact(Field("CapturedAt"), "O", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var captured) ? captured.ToString("O", CultureInfo.InvariantCulture) : null;
            if (engineBuild == null) Missing("ENGINE_BUILD_UNAVAILABLE", "源文件未提供可确认的引擎版本；未收集任意版本文本。");
            if (capturedAt == null) Missing("CAPTURE_TIME_UNAVAILABLE", "采集时间及偏移未提供，不能由本地包生成时间推断。");
            Missing("COLLECTION_CONTEXT_UNVERIFIED", "服务器负载、会话 SET 选项、参数、权限及完整采集过程未独立核实。");
            Missing("RAW_INPUT_OMITTED", options.IncludeRawSelection
                ? "仅包含明确选择的语句/事件 XML，不等于完整原始文件；不包含其他事件或原始 XEL。"
                : "未收集原始输入；脱敏结构不能用于执行或完整重现 SQL。");
            if (!options.IncludeReport) Missing("REPORT_OMITTED", "用户未选择报告正文。");
            if (options.LogPath == null) Missing("LOG_OMITTED", "未选择日志；错误摘要不代表完整异常历史。");
            if (options.DumpPath == null) Missing("DUMP_OMITTED", "未选择 DUMP；未知错误生成的本地转储不会自动附带。");

            var capabilities = report?.Kind == "Deadlock" ? DocumentCapabilities.DeadlockGraph : DocumentCapabilities.None;
            if (Enum.TryParse<DocumentCapabilities>(Field("Capabilities"), out var parsed) && ((int)parsed & ~127) == 0) capabilities = parsed;
            if (report?.Kind == "ExecutionPlan" && !capabilities.HasFlag(DocumentCapabilities.RuntimeCounters))
                Missing("RUNTIME_COUNTERS_UNAVAILABLE", "未采集运行时计数，不能将估算值当作实际执行数据。");
            Json("context.json", new
            {
                SchemaVersion = DiagnosticPackage.CurrentVersion, ApplicationVersion = ProductInfo.Version,
                CoreAssemblyVersion = typeof(DiagnosticPackageBuilder).Assembly.GetName().Version?.ToString(),
                RuleProtocolVersion = PlanAnalysisContext.CurrentProtocolVersion, ReportSchemaVersion = DiagnosticReport.CurrentVersion,
                BuildConfiguration = Logger.IsDebugMode ? "Debug" : "Release",
                LogLevels = Logger.IsDebugMode ? new[] { "Debug", "Warning", "Error", "Critical" } : ["Error", "Critical"],
                Runtime = RuntimeInformation.FrameworkDescription, OperatingSystemVersion = Environment.OSVersion.Version.ToString(),
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                CollectedAtUtc = DateTimeOffset.UtcNow, ReportCreatedAtUtc = report?.CreatedAt,
                Kind = report?.Kind, Scope = report?.Scope, EngineBuild = engineBuild, SourceSchema = schema, CapturedAt = capturedAt,
                Capabilities = capabilities.ToString(),
                CollectionMode = "LocalManualSelection", SnapshotBoundary = "Report snapshot and current operation may describe different requests; no correlation is inferred.",
                OperationState = (progress?.State ?? AnalysisOperationState.Idle).ToString(),
                StageTimings = progress?.Stages.Take(32).Where(s => Enum.IsDefined(s.State) && double.IsFinite(s.Milliseconds) && s.Milliseconds >= 0)
                    .Select(s => new { State = s.State.ToString(), s.Milliseconds }).ToArray(),
                AnalysisConfigurationFingerprint = analysisFingerprint, ActiveConfigurationFingerprint = activeConfiguration?.Fingerprint,
                ConfigurationChanged = configurationChanged,
                Limitations = new[] { "离线静态分析；未连接数据库验证根因或改写语义。", "未采集用户名、主机名、环境变量、原始文件路径及自由文本异常历史。", "仅说明已提供证据；没有错误摘要不等于应用从未出错。" }
            });
            Json("configuration.json", new
            {
                Policy = "Known executable settings only; unknown extensions and free text excluded.",
                Analysis = new { Available = analysisRules != null, Fingerprint = analysisFingerprint, Rules = analysisRules },
                ActiveSession = new { Available = activeConfiguration != null, Fingerprint = activeConfiguration?.Fingerprint, Rules = currentRules },
                RuleVersions = RuleMetadataCatalog.RegisteredRuleIds.OrderBy(id => id, StringComparer.Ordinal)
                    .Select(id => RuleMetadataCatalog.Get(id, ""))
                    .Select(metadata => new { metadata.RuleId, metadata.Version, metadata.DefaultSeverity }),
                ReportRuleVersions = report?.Runs.Select(r => new { r.RuleId, r.RuleVersion }).Distinct().ToArray()
            });
            Json("errors.json", new
            {
                Scope = "Selected report rule failures and current operation state only; exception text is excluded.",
                OperationFailed = progress?.State == AnalysisOperationState.Failed,
                FailedRuleCount = report?.FailedCount,
                FailedRules = report?.Runs.Where(r => r.Status == "Failed").Select(r => new { r.RuleId, r.RuleVersion, r.Location, r.Status,
                    ReasonCode = r.Fields.FirstOrDefault(f => f.Name == "ReasonCode")?.Value }).ToArray()
            });
            Json("missing-evidence.json", new
            {
                Missing = missing,
                RuleGaps = report?.Runs.Where(r => r.Status is "Skipped" or "Failed").Select(r => new { r.RuleId, r.Location, r.Status,
                    ReasonCode = r.Fields.FirstOrDefault(f => f.Name == "ReasonCode")?.Value }).ToArray(),
                MetricGaps = report?.Facts.Where(IsMetricGap).Select(f => new { f.Name, State = f.Value }).ToArray()
            });
            if (options.IncludeReport && report != null)
            {
                var redaction = new ReportRedactionService(unexpectedErrors).Preview(report, token);
                token.ThrowIfCancellationRequested();
                if (!redaction.CanExport) throw new InvalidDataException("报告脱敏未通过，诊断包未生成。" + redaction.Summary);
                Add("report.json", Utf8.GetBytes(DiagnosticReportRenderer.Json(redaction.Report!, token)), "Redacted");
                Json("redaction.json", new { redaction.PolicyVersion, redaction.Counts, redaction.Unsupported,
                    Limitations = "自由文本已替换；保留结构、数值和相对位置。处理前样例不进入包。" });
            }
            if (options.IncludeRawSelection)
            {
                if (report == null) throw new InvalidDataException("没有可附带的选中 XML 快照。");
                Add("selection.xml", Utf8.GetBytes(report.SourceXml), "RawSensitive");
            }
            if (options.LogPath != null)
                Add("application.log", ReadAttachment(options.LogPath, ".log", DiagnosticPackage.MaxLogBytes, token), "RawSensitive");
            if (options.DumpPath != null)
                Add("process.dmp", ReadAttachment(options.DumpPath, ".dmp", DiagnosticPackage.MaxEntryBytes, token), "RawSensitive", true);

            bool sensitive = entries.Any(e => e.Privacy == "RawSensitive");
            if (sensitive) Logger.Warning("IMP27.Package includes explicitly selected raw sensitive content.");
            Json("manifest.json", new
            {
                SchemaVersion = DiagnosticPackage.CurrentVersion, HashAlgorithm = "SHA-256", ContainsSensitiveContent = sensitive,
                Files = entries.Select(e => new { e.Name, e.Bytes, e.Sha256, e.Privacy, e.IsBinary }).ToArray(),
                Integrity = "SHA256SUMS covers every payload and manifest.json; it does not hash itself. Checksums detect corruption, not authenticity.",
                Transport = "LocalOnly; no upload", Limits = new { DiagnosticPackage.MaxEntryBytes, DiagnosticPackage.MaxPackageBytes, DiagnosticPackage.MaxLogBytes }
            });
            Add("SHA256SUMS", Utf8.GetBytes(string.Concat(entries.Select(e => $"{e.Sha256}  {e.Name}\n"))), "AllowlistedMetadata");
            Logger.Debug($"IMP27.Package.Prepare completed; files={entries.Count}.");
            return new(new(entries), "预览已生成；选择文件可核对全部文本、字节数和 SHA-256。保存将使用同一快照。");
        }
        catch (Exception exception)
        {
            return new(null, ExceptionPolicy.Describe(exception, "IMP27.Package.Prepare", unexpectedErrors));
        }
    }

    private static bool IsMetricGap(ReportField field) => field.Category == ""
        && field.Name.EndsWith("/State", StringComparison.Ordinal)
        && Enum.TryParse<PlanMetricState>(field.Value, out var state) && Enum.IsDefined(state)
        && state != PlanMetricState.Available
        // TryParse also accepts numbers and comma-separated names; only canonical protocol names are evidence.
        && string.Equals(field.Value, state.ToString(), StringComparison.Ordinal);

    private static string? NumericVersion(string? value) => value is { Length: <= 32 } && value.All(c => char.IsAsciiDigit(c) || c == '.')
        && Version.TryParse(value, out var version) ? version.ToString() : null;

    private static RuleConfigurationSnapshot[]? ReadAnalysisConfiguration(DiagnosticReport? report)
    {
        string? json = report?.Metadata.FirstOrDefault(f => f.Name == "Configuration")?.Value;
        if (json == null || report!.IsRedacted) return null;
        try
        {
            if (Encoding.UTF8.GetByteCount(json) > RuleConfigurationDocument.MaxBytes) throw new InvalidDataException("报告配置超过预算。");
            var values = JsonSerializer.Deserialize<Dictionary<string, RuleConfigurationSnapshot>>(json);
            if (values == null || values.Count == 0) return null;
            var known = RuleMetadataCatalog.RegisteredRuleIds.ToHashSet(StringComparer.Ordinal);
            if (values.Any(p => p.Value == null || p.Key != p.Value.RuleId || !known.Contains(p.Key)
                || p.Value.SeverityOverride is not (null or "Info" or "Warning" or "Critical")))
                throw new InvalidDataException("报告配置包含无法验证的规则设置。");
            // Do not fill absent rules with today's defaults: the report must provide its complete snapshot.
            if (values.Count != known.Count) return null;
            return values.Values.OrderBy(r => r.RuleId, StringComparer.Ordinal).ToArray();
        }
        catch (JsonException exception) { throw new InvalidDataException("报告中的规则配置不是有效 JSON。", exception); }
    }

    private static byte[] ReadAttachment(string path, string extension, long maxBytes, CancellationToken token)
    {
        try
        {
            if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("附件扩展名必须为 " + extension + "。");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long length = stream.Length;
            if (length == 0 || length > maxBytes) throw new InvalidDataException("附件为空或超过预算；日志上限 2 MiB，DUMP 上限 64 MiB。");
            var bytes = new byte[(int)length];
            for (int offset = 0; offset < bytes.Length;)
            {
                token.ThrowIfCancellationRequested();
                int read = stream.Read(bytes, offset, Math.Min(64 * 1024, bytes.Length - offset));
                if (read == 0) throw new IOException("读取附件时内容缩短，请重新选择。");
                offset += read;
            }
            if (stream.Length != length) throw new IOException("读取附件时长度变化，请重新生成预览。");
            if (extension == ".log") _ = Utf8.GetCharCount(bytes);
            if (extension == ".dmp")
            {
                using var snapshot = new MemoryStream(bytes, writable: false);
                MinidumpValidator.Validate(snapshot, token);
            }
            return bytes;
        }
        catch (DecoderFallbackException exception)
        { throw new InvalidDataException("日志不是有效的 UTF-8 文本，请选择 UTF-8 编码的 .log 文件。", exception); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        { throw new InvalidDataException("附件路径无效。", exception); }
    }
}
