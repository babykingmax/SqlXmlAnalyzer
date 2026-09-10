using System.Text;
using System.Xml;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Core.Services;

public enum InputStatus
{
    Success,
    Unrecognized,
    Invalid,
    Unsupported,
    MalformedXml,
    ReadError,
    UnexpectedError,
    Partial,
    TooLarge,
    Cancelled
}

/// <summary>A selected, standalone deadlock copy. Index is one-based in source order.</summary>
public sealed record DeadlockInput(int Index, string? Timestamp, XDocument Document)
{
    public int PayloadIndex { get; init; } = 1;
    public SourceLocation? Location { get; init; }
    public DateTimeOffset? CapturedAt { get; init; }
    public XElement? OriginalElement { get; init; }
    public IReadOnlyDictionary<string, string> CaptureFields { get; init; } = new Dictionary<string, string>();
    public string DisplayName => $"死锁事件 {Index}" + (PayloadIndex > 1 ? $" / 子事件 {PayloadIndex}" : "") +
        (Timestamp == null ? "" : $" · {Timestamp}");
}

public sealed record InputRecognitionResult(
    InputStatus Status,
    AnalysisDocumentKind Kind,
    XDocument? Document,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status == InputStatus.Success && Kind != AnalysisDocumentKind.Unknown;
    public bool HasUsableContent => (Status is InputStatus.Success or InputStatus.Partial) && Kind != AnalysisDocumentKind.Unknown;
    public IReadOnlyList<DeadlockInput> Deadlocks { get; init; } = Array.Empty<DeadlockInput>();
    public IReadOnlyList<InputDiagnostic> Diagnostics { get; init; } = Array.Empty<InputDiagnostic>();
    public DocumentEnvelope? Envelope { get; init; }
    public DocumentCapabilities Capabilities { get; init; }
    public ReadOnlyMemory<byte> SourceSnapshot { get; init; }
    public IReadOnlyList<XelInputRecord> XelRecords { get; init; } = Array.Empty<XelInputRecord>();

    public InputRecognitionResult ForExecutionPlan() => !IsSuccess || Kind == AnalysisDocumentKind.ExecutionPlanXml
        ? this
        : InputRecognitionService.Failure(InputStatus.Unsupported, Kind, Document,
            "INPUT_EXPECTED_PLAN", "此入口需要 SQL Server 执行计划；输入是死锁或 XEL 文档。") with
            { Envelope = Envelope, Capabilities = Capabilities, Deadlocks = Deadlocks };
}

/// <summary>Minimum content recognition, not full XSD validation or a completeness guarantee.</summary>
public sealed class InputRecognitionService : IDiagnosticDocumentReader
{
    public const string ShowPlanNamespace = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
    internal const string ReadErrorMessage = "无法读取输入文件，请检查路径、权限、文件占用和编码。";
    private readonly Func<string, XDocument>? _loadXml;
    private readonly Func<string, XDocument>? _parseXml;
    private readonly Func<string, Stream> _openRead;
    private readonly DocumentReadOptions _options;
    private readonly IUnexpectedErrorReporter _unexpectedErrors;

    public InputRecognitionService(IUnexpectedErrorReporter? unexpectedErrors = null,
        Func<string, XDocument>? loadXml = null, Func<string, XDocument>? parseXml = null,
        Func<string, Stream>? openRead = null, DocumentReadOptions? options = null)
    {
        _unexpectedErrors = unexpectedErrors ?? UnexpectedErrorReporter.Shared;
        _openRead = openRead ?? (path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
        _loadXml = loadXml;
        _parseXml = parseXml;
        _options = options ?? new DocumentReadOptions();
        _options.Validate();
    }

    public InputRecognitionResult Load(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path))
            return Failure(InputStatus.ReadError, AnalysisDocumentKind.Unknown, null, "INPUT_READ_ERROR", ReadErrorMessage);
        if (Path.GetExtension(path).Equals(".xel", StringComparison.OrdinalIgnoreCase))
            return new XelReader().ReadDocumentAsync(path, _options, cancellationToken, _unexpectedErrors).GetAwaiter().GetResult();
        byte[]? snapshot = null;
        var result = Read(() =>
        {
            if (_loadXml != null) return _loadXml(path);
            using Stream stream = _openRead(path);
            snapshot = SafeXmlHelper.ReadSnapshot(stream, _options, cancellationToken);
            return SafeXmlHelper.LoadSnapshot(snapshot, _options, cancellationToken);
        }, cancellationToken);
        return WithSource(result, snapshot, path);
    }

    public InputRecognitionResult Parse(string? xml, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return string.IsNullOrWhiteSpace(xml)
            ? Failure(InputStatus.MalformedXml, AnalysisDocumentKind.Unknown, null,
                "INPUT_MALFORMED_XML", "XML 内容为空、格式错误或包含禁止的 DTD。")
            : WithSource(Read(() => _parseXml != null ? _parseXml(xml) : SafeXmlHelper.ParseSafe(xml, _options, cancellationToken),
                cancellationToken), null, null);
    }

    public async Task<InputRecognitionResult> ReadFileAsync(string path, DocumentReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (options == null) return await Task.Run(() => Load(path, cancellationToken), CancellationToken.None).ConfigureAwait(false);
            return await new InputRecognitionService(_unexpectedErrors, _loadXml, _parseXml, _openRead, options)
                .ReadFileAsync(path, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Cancelled(); }
    }

    public async Task<InputRecognitionResult> ReadAsync(Stream stream, DocumentReadOptions? options = null,
        CancellationToken cancellationToken = default, string? sourceName = null)
    {
        options ??= _options;
        options.Validate();
        if (string.Equals(Path.GetExtension(sourceName), ".xel", StringComparison.OrdinalIgnoreCase))
            return await new XelReader().ReadDocumentAsync(stream, options, cancellationToken, sourceName, _unexpectedErrors).ConfigureAwait(false);
        try
        {
            using var snapshot = new MemoryStream();
            byte[] buffer = new byte[8192];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = (int)Math.Min(buffer.Length, options.MaxBytes - snapshot.Length + 1);
                int read = await stream.ReadAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (snapshot.Length + read > options.MaxBytes) throw new DocumentBudgetExceededException("Bytes");
                snapshot.Write(buffer, 0, read);
            }
            byte[] bytes = snapshot.ToArray();
            var result = Read(() => SafeXmlHelper.LoadSnapshot(bytes, options, cancellationToken), cancellationToken);
            return WithSource(result, bytes, sourceName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Cancelled(); }
        catch (Exception exception)
        {
            // Cancellation can race a stream failure. Do not let a second cancellation
            // check escape from this catch block instead of returning the async contract.
            if (cancellationToken.IsCancellationRequested) return Cancelled();
            return Read(() => throw exception, CancellationToken.None);
        }
    }

    internal static InputRecognitionResult Cancelled()
    {
        Logger.Warning("InputRecognition: 读取已取消。");
        return new(InputStatus.Cancelled, AnalysisDocumentKind.Unknown, null, "INPUT_CANCELLED", "读取已取消，未提交文档。");
    }

    internal static InputRecognitionResult WithSource(InputRecognitionResult result, byte[]? bytes, string? sourceName)
    {
        string? hash = bytes == null ? null : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        XElement? root = result.Document?.Root;
        var capabilities = result.Capabilities;
        if (root != null) capabilities |= DocumentCapabilities.PreservedSource;
        var envelope = new DocumentEnvelope(hash ?? Guid.NewGuid().ToString("N"), hash, sourceName, result.Kind,
            result.IsSuccess && result.Deadlocks.Count == 1 ? result.Deadlocks[0].CapturedAt : null,
            result.Kind == AnalysisDocumentKind.ExecutionPlanXml ? (string?)root?.Attribute("Build") : null,
            result.Kind == AnalysisDocumentKind.ExecutionPlanXml ? (string?)root?.Attribute("Version") : null,
            bytes?.LongLength);
        if (result.IsSuccess && result.Kind == AnalysisDocumentKind.ExecutionPlanXml && result.Document != null)
        {
            // Re-recognition of the same unchanged XML must retain its captured identity.
            // A new byte snapshot can establish provenance; XML alone cannot replace it.
            if (bytes == null && PlanIdentityAdapter.GetRegisteredEnvelope(result.Document) is { } registered)
                envelope = sourceName == null ? registered : registered with { SourceName = sourceName };
            PlanIdentityAdapter.SetEnvelope(result.Document, envelope);
        }
        return result with
        {
            Capabilities = capabilities,
            SourceSnapshot = bytes == null ? result.SourceSnapshot : bytes,
            Envelope = envelope
        };
    }

    private InputRecognitionResult Read(Func<XDocument> read, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Logger.Debug("InputRecognition: 开始安全读取 XML。");
            XDocument document = read();
            cancellationToken.ThrowIfCancellationRequested();
            var result = Recognize(document, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Logger.Warning("InputRecognition: 读取已取消。");
            throw;
        }
        catch (DocumentBudgetExceededException exception)
        {
            return Failure(InputStatus.TooLarge, AnalysisDocumentKind.Unknown, null,
                "INPUT_TOO_LARGE", exception.Message);
        }
        catch (XmlException)
        {
            // Parser messages may contain input text. Keep public diagnostics and logs content-free.
            return Failure(InputStatus.MalformedXml, AnalysisDocumentKind.Unknown, null,
                "INPUT_MALFORMED_XML", "XML 内容为空、格式错误或包含禁止的 DTD。");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Text.DecoderFallbackException)
        {
            return Failure(InputStatus.ReadError, AnalysisDocumentKind.Unknown, null,
                "INPUT_READ_ERROR", ReadErrorMessage);
        }
        catch (Exception exception)
        {
            UnexpectedErrorReport incident = ExceptionPolicy.Capture(exception, "InputRecognition.Read", _unexpectedErrors);
            return Failure(InputStatus.UnexpectedError, AnalysisDocumentKind.Unknown, null,
                "INPUT_UNEXPECTED_ERROR", $"输入识别发生未知错误；{incident.Summary}");
        }
    }

    public static InputRecognitionResult Recognize(XDocument? document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = RecognizeCore(document, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return WithSource(result, null, null);
    }

    private static InputRecognitionResult RecognizeCore(XDocument? document, CancellationToken cancellationToken)
    {
        XElement? root = document?.Root;
        if (root == null)
            return Failure(InputStatus.Invalid, AnalysisDocumentKind.Unknown, document,
                "INPUT_MISSING_ROOT", "XML 缺少根节点。");

        cancellationToken.ThrowIfCancellationRequested();

        if (root.Name.LocalName == "ShowPlanXML")
        {
            if (root.Name.NamespaceName != ShowPlanNamespace)
                return Failure(InputStatus.Unsupported, AnalysisDocumentKind.Unknown, document,
                    "INPUT_PLAN_NAMESPACE", "不支持此 ShowPlanXML 命名空间；需要 SQL Server 标准 ShowPlan 命名空间。");

            XNamespace ns = ShowPlanNamespace;
            var sequences = root.Elements(ns + "BatchSequence").ToList();
            var batches = sequences.Elements(ns + "Batch").ToList();
            // These are the statement choices in Microsoft's ShowPlan StmtBlockType.
            // QueryPlan and RelOp are optional (e.g. CREATE TABLE); do not require them.
            if (sequences.Count != 1 || root.Elements().Any(e => e.Name != ns + "BatchSequence") ||
                sequences[0].Elements().Any(e => e.Name != ns + "Batch") ||
                batches.Count == 0 || batches.Any(batch => !batch.Elements(ns + "Statements").Any() ||
                    batch.Elements().Any(e => e.Name != ns + "Statements")) ||
                HasInvalidPlanContents(root, ns, cancellationToken))
                return Failure(InputStatus.Invalid, AnalysisDocumentKind.Unknown, document,
                    "INPUT_PLAN_STRUCTURE", "执行计划结构或命名空间无效；需要有效的 BatchSequence / Batch / Statements 语句结构。");

            Logger.Debug("InputRecognition: 已识别 SQL Server 执行计划。");
            var capabilities = PlanCapabilityService.Get(root, cancellationToken);
            return new(InputStatus.Success, AnalysisDocumentKind.ExecutionPlanXml, document) { Capabilities = capabilities };
        }

        var nodes = new List<(XElement Element, int Index)>();
        var diagnostics = new List<InputDiagnostic>();
        var source = new DocumentSource(cancellationToken);
        int ordinal = 0;
        bool supported = CollectDeadlocks(root, nodes, diagnostics, source, ref ordinal, cancellationToken);
        if (!supported)
            return Failure(InputStatus.Unrecognized, AnalysisDocumentKind.Unknown, document,
                "INPUT_UNRELATED_XML", "无法识别此 XML；需要 SQL Server ShowPlanXML、deadlock、deadlock-list 或 XE 死锁包装。");
        if (nodes.Count == 0)
            return Failure(InputStatus.Invalid, AnalysisDocumentKind.Unknown, document,
                "INPUT_DEADLOCK_STRUCTURE", "死锁包装为空或含不支持的记录，未找到可读取的 deadlock 节点。") with
                { Diagnostics = diagnostics.Count == 0 ? new[] { new InputDiagnostic("INPUT_DEADLOCK_STRUCTURE", "死锁包装为空。", source.Location(root)) } : diagnostics.AsReadOnly() };

        var events = new List<DeadlockInput>();
        foreach ((XElement node, int index) in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            XNamespace ns = node.Name.Namespace;
            XElement? processes = node.Element(ns + "process-list");
            XElement? resources = node.Element(ns + "resource-list");
            string? error = processes == null ? "死锁 XML 缺少必需的 process-list 节点。"
                : resources == null ? "死锁 XML 缺少必需的 resource-list 节点。"
                : !processes.Elements(ns + "process").Any(p => !string.IsNullOrWhiteSpace((string?)p.Attribute("id")))
                    ? "process-list 中没有包含有效 id 的 process。"
                : !resources.Elements().Any() ? "resource-list 中没有有效的锁资源。"
                : node.Descendants().Any(e => e.Name.Namespace != ns)
                    ? "死锁记录内部包含不一致的 XML 命名空间。" : null;
            if (error != null)
            {
                diagnostics.Add(new("INPUT_DEADLOCK_STRUCTURE", $"未处理记录 {index}：{error}", source.Location(node, index)));
                continue;
            }

            var copy = new XElement(node);
            // Existing graph/timeline consumers use unqualified names. Normalize the copy only.
            foreach (XElement element in copy.DescendantsAndSelf())
            {
                cancellationToken.ThrowIfCancellationRequested();
                element.Name = element.Name.LocalName;
                element.Attributes().Where(a => a.IsNamespaceDeclaration).Remove();
            }
            string? timestamp = (string?)node.Ancestors().FirstOrDefault(e => e.Name.LocalName == "event")?.Attribute("timestamp");
            DateTimeOffset? capturedAt = DocumentSource.Timestamp(timestamp);
            // Raw timestamp and capture wrapper are retained even when no offset can be proved.
            events.Add(new(index, timestamp, new XDocument(copy))
            {
                CapturedAt = capturedAt,
                Location = source.Location(node, index),
                // Preserve the source node and its line information for evidence navigation.
                // Graph consumers continue to use the separate normalized Document above.
                OriginalElement = node
            });
        }
        if (events.Count == 0)
            return Failure(InputStatus.Invalid, AnalysisDocumentKind.Unknown, document,
                "INPUT_DEADLOCK_STRUCTURE", "没有有效死锁事件；" + string.Join("；", diagnostics.Select(d => d.Message))) with { Diagnostics = diagnostics.AsReadOnly() };
        diagnostics.Sort((left, right) => Nullable.Compare(left.Location?.EventIndex, right.Location?.EventIndex));
        bool partial = diagnostics.Count > 0;
        if (partial) Logger.Warning($"INPUT_PARTIAL: 已读取 {events.Count} 个事件，未处理 {diagnostics.Count} 条记录。");
        Logger.Debug($"InputRecognition: 已识别 {events.Count} 个死锁事件。");
        return new(partial ? InputStatus.Partial : InputStatus.Success, AnalysisDocumentKind.DeadlockXml, document,
            partial ? "INPUT_PARTIAL" : null, partial ? $"部分读取：有效事件 {events.Count}；未处理 {diagnostics.Count} 条记录，位置与原因见 Diagnostics。" : null)
        {
            Deadlocks = events.AsReadOnly(),
            Diagnostics = diagnostics.AsReadOnly(),
            Capabilities = DocumentCapabilities.DeadlockGraph |
                (events.Count > 1 ? DocumentCapabilities.MultipleEvents : DocumentCapabilities.None) |
                (events.Any(e => e.CapturedAt != null) ? DocumentCapabilities.OffsetTimestamps : DocumentCapabilities.None)
        };
    }

    private static bool HasInvalidPlanContents(XElement root, XNamespace ns, CancellationToken cancellationToken)
    {
        var pending = new Stack<XElement>();
        pending.Push(root);
        while (pending.TryPop(out XElement? element))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (element.Name.Namespace != ns) return true;
            // Microsoft's InternalInfoType explicitly allows arbitrary elements/namespaces.
            if (element.Name.LocalName == "InternalInfo") continue;
            if (element.Name.LocalName == "Statements" && element.Elements().Any(statement =>
                statement.Name.LocalName is not ("StmtSimple" or "StmtCond" or "StmtCursor" or "StmtReceive" or "StmtUseDb")))
                return true;
            foreach (XElement child in element.Elements()) pending.Push(child);
        }
        return false;
    }

    private static bool CollectDeadlocks(XElement root, List<(XElement Element, int Index)> nodes,
        List<InputDiagnostic> diagnostics, DocumentSource source, ref int ordinal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (root.Name.LocalName)
        {
            case "deadlock":
                nodes.Add((root, ++ordinal));
                return true;
            case "deadlock-list":
                if (!root.HasElements)
                {
                    diagnostics.Add(new("INPUT_DEADLOCK_STRUCTURE", $"未处理记录 {++ordinal}：死锁包装为空。",
                        source.Location(root, ordinal)));
                    return true;
                }
                foreach (var child in root.Elements())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (child.Name == root.Name.Namespace + "deadlock") nodes.Add((child, ++ordinal));
                    else Skip(child, diagnostics, source, ++ordinal);
                }
                return true;
            case "event":
                if ((string?)root.Attribute("name") != "xml_deadlock_report")
                {
                    Skip(root, diagnostics, source, ++ordinal);
                    return true;
                }
                var values = root.Elements(root.Name.Namespace + "data")
                    .Where(e => (string?)e.Attribute("name") == "xml_report")
                    .Elements(root.Name.Namespace + "value").ToList();
                if (values.Count != 1 || values[0].Elements().Count() != 1)
                {
                    Skip(root, diagnostics, source, ++ordinal);
                    return true;
                }
                XElement payload = values[0].Elements().Single();
                if (payload.Name.LocalName is not ("deadlock" or "deadlock-list"))
                {
                    Skip(payload, diagnostics, source, ++ordinal);
                    return true;
                }
                return CollectDeadlocks(payload, nodes, diagnostics, source, ref ordinal, cancellationToken);
            case "events":
            case "RingBufferTarget":
                foreach (XElement child in root.Elements())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (child.Name != root.Name.Namespace + "event") Skip(child, diagnostics, source, ++ordinal);
                    else CollectDeadlocks(child, nodes, diagnostics, source, ref ordinal, cancellationToken);
                }
                return true;
            default:
                return false;
        }
    }

    private static void Skip(XElement node, List<InputDiagnostic> diagnostics, DocumentSource source, int index) =>
        diagnostics.Add(new("INPUT_SKIPPED_RECORD", $"未处理记录 {index}：不是受支持的死锁记录或包装结构。",
            source.Location(node, index)));

    internal static InputRecognitionResult Failure(InputStatus status, AnalysisDocumentKind kind,
        XDocument? document, string code, string message)
    {
        Logger.Error($"{code}: {message}");
        return new(status, kind, document, code, message) { Diagnostics = new[] { new InputDiagnostic(code, message) } };
    }
}
