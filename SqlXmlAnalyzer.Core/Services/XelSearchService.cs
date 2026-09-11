using System.Globalization;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services;

public interface IXelSearchService
{
    Task<XelSearchCatalog> LoadAsync(IReadOnlyList<XelSearchSource> existing, IReadOnlyList<string> paths, CancellationToken token);
    XelSearchResult Search(XelSearchCatalog catalog, XelSearchFilter filter, CancellationToken token);
}

public sealed class XelSearchService : IXelSearchService
{
    private readonly IDiagnosticDocumentReader _reader;
    private readonly XelSearchOptions _options;
    public XelSearchService(IDiagnosticDocumentReader? reader = null, XelSearchOptions? options = null)
    {
        _reader = reader ?? new InputRecognitionService();
        _options = options ?? new();
    }

    public async Task<XelSearchCatalog> LoadAsync(IReadOnlyList<XelSearchSource> existing,
        IReadOnlyList<string> paths, CancellationToken token)
    {
        Logger.Debug($"IMP23 load requested: existing={existing.Count}, selected={paths.Count}.");
        var sources = existing.ToList();
        ValidateBudgets(sources, token);
        foreach (var source in sources) source.ValidateSnapshot(token);
        foreach (string path in paths)
        {
            token.ThrowIfCancellationRequested();
            string fullPath = Path.GetFullPath(path);
            if (sources.Any(s => string.Equals(Path.GetFullPath(s.Path), fullPath, StringComparison.OrdinalIgnoreCase))) continue;
            if (sources.Count >= _options.MaxFiles) throw new DocumentBudgetExceededException("XEL 集合文件数");
            if (!string.Equals(Path.GetExtension(path), ".xel", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("多文件导入只接受 .xel 文件。");
            long remainingBytes = _options.MaxBytes - sources.Sum(s => s.Input.Envelope?.SourceBytes ?? s.Input.SourceSnapshot.Length);
            if (remainingBytes <= 0) throw new DocumentBudgetExceededException("XEL 集合字节数");
            long remainingCharacters = _options.MaxXmlCharacters - sources.Sum(s => SourceCharacters(s, token));
            if (remainingCharacters <= 0) throw new DocumentBudgetExceededException("XEL 集合字符数");
            int remainingEvents = _options.MaxEvents - sources.Sum(s => Math.Max(s.Input.XelRecords.Count, s.Input.Deadlocks.Count));
            if (remainingEvents <= 0) throw new DocumentBudgetExceededException("XEL 集合事件数");
            var input = await _reader.ReadFileAsync(fullPath,
                new DocumentReadOptions { MaxBytes = Math.Min(remainingBytes, 64L * 1024 * 1024),
                    MaxXmlCharacters = Math.Min(remainingCharacters, 32L * 1024 * 1024), MaxXelEvents = remainingEvents }, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (input.Status == InputStatus.Cancelled) throw new OperationCanceledException(token);
            if (input.Kind != AnalysisDocumentKind.XelDeadlockTrace || !CanCatalog(input))
                throw new InvalidDataException("XEL 导入未提交；" + InputReadPresentation.Describe(input));
            sources.Add(new(fullPath, input));
            ValidateBudgets(sources, token);
        }
        return Build(sources, token);
    }

    public XelSearchCatalog Build(IReadOnlyList<XelSearchSource> sources, CancellationToken token = default)
    {
        ValidateBudgets(sources, token);
        var events = new List<XelSearchEvent>();
        var notices = new List<string>();
        int skipped = 0;
        long characters = 0;
        for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var source = sources[sourceIndex];
            token.ThrowIfCancellationRequested();
            if (!CanCatalog(source.Input))
                throw new InvalidDataException("检索源必须为已识别的死锁输入或成功读取的无死锁 XEL。");
            source.ValidateSnapshot(token);
            int unhandled = source.Input.XelRecords.Count == 0 ? 0 : source.Input.XelRecords.Count - source.Input.Deadlocks.Select(e => e.Index).Distinct().Count();
            skipped += unhandled;
            if (source.Input.Status == InputStatus.Partial || source.Input.ErrorCode == "INPUT_XEL_NO_DEADLOCK")
                notices.Add($"文件 {sourceIndex + 1}：{InputReadPresentation.Describe(source.Input)}");
            foreach (var item in source.Input.Deadlocks)
            {
                token.ThrowIfCancellationRequested();
                string xml = item.Document.ToString(SaveOptions.DisableFormatting);
                characters += xml.Length;
                if (characters > _options.MaxXmlCharacters) throw new DocumentBudgetExceededException("XEL 集合 XML 字符数");
                string hash = XelStructureKey.Hash(xml);
                var structure = XelStructureKey.Create(item, $"{sourceIndex}/{item.Index}/{item.PayloadIndex}", token);
                var record = item.Index > 0 && item.Index <= source.Input.XelRecords.Count ? source.Input.XelRecords[item.Index - 1] : null;
                var metadata = (record?.Fields ?? item.CaptureFields).Concat(record?.Actions ?? new Dictionary<string, string>()).ToArray();
                string origin = XelStructureKey.HashParts(XelSearchSource.Metadata(metadata.Where(p =>
                    p.Key is "server_instance_name" or "server_name" or "database_name")));
                if (!structure.Key.StartsWith("isolated:", StringComparison.Ordinal))
                    structure = (structure.Key + ":" + origin, structure.Description + "；采集到的实例/数据库名称也须一致");
                var databaseNames = new HashSet<string>(new[] { "dbid", "currentdb", "currentdbname", "databaseid", "database_id", "database_name", "databasename" }, StringComparer.OrdinalIgnoreCase);
                var objectNames = new HashSet<string>(new[] { "objectname", "object_name", "object_id", "objid", "indexname", "associatedObjectId", "hobtid" }, StringComparer.OrdinalIgnoreCase);
                string Values(HashSet<string> names) => string.Join(" | ", item.Document.Descendants().Attributes().Where(a => names.Contains(a.Name.LocalName))
                    .Select(a => a.Value).Concat(metadata.Where(p => names.Contains(p.Key)).Select(p => p.Value))
                    .Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal));
                // Keep raw XML and unknown capture fields/actions searchable without writing them to logs.
                string searchText = xml + "\n" + record?.Name + "\n" + string.Join("\n", metadata.Where(p => p.Key != "xml_report").Select(p => p.Key + "=" + p.Value));
                characters += searchText.Length - xml.Length;
                if (characters > _options.MaxXmlCharacters) throw new DocumentBudgetExceededException("XEL 集合检索字符数");
                string captureHash = XelStructureKey.HashParts(new[] { record?.Name ?? "",
                    XelStructureKey.HashParts(XelSearchSource.Metadata(record?.Fields ?? item.CaptureFields)),
                    XelStructureKey.HashParts(XelSearchSource.Metadata(record?.Actions ?? new Dictionary<string, string>())) });
                string duplicateKey = item.CapturedAt.HasValue ? $"{item.CapturedAt.Value.UtcTicks}:{hash}:{captureHash}"
                    : $"unknown:{sourceIndex}:{item.Index}:{item.PayloadIndex}";
                string capture = record == null ? "" : string.Join("\n", record.Fields.Where(p => p.Key != "xml_report")
                    .Select(p => "字段 " + p.Key + " = " + p.Value).Concat(record.Actions.Select(p => "动作 " + p.Key + " = " + p.Value)));
                events.Add(new(source, item, hash, structure.Key, structure.Description, Values(databaseNames), Values(objectNames), searchText, duplicateKey)
                {
                    XmlPreview = xml.Length > 20_000 ? xml[..20_000] + "\n[预览截断；完整事件仍保留，可回到原始事件查看证据]" : xml,
                    CapturePreview = capture.Length > 4000 ? capture[..4000] + "\n[采集信息预览截断]" : capture,
                    OriginalXmlHash = item.OriginalElement == null ? null : XelStructureKey.Hash(item.OriginalElement.ToString(SaveOptions.DisableFormatting))
                });
            }
        }
        foreach (var source in sources) source.ValidateSnapshot(token);
        var sorted = events.OrderBy(e => e.Event.CapturedAt?.UtcTicks ?? long.MaxValue)
            .ThenBy(e => e.SourcePath, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.SourceHash, StringComparer.Ordinal)
            .ThenBy(e => e.Event.Index).ThenBy(e => e.Event.PayloadIndex).ToArray();
        Logger.Debug($"IMP23 catalog built: files={sources.Count}, events={events.Count}, skipped={skipped}.");
        if (skipped > 0 || notices.Count > 0) Logger.Warning($"IMP23 partial scope: skipped={skipped}, affectedFiles={notices.Count}.");
        return new(sources.ToArray(), sorted, skipped, string.Join(Environment.NewLine, notices));
    }

    private static bool CanCatalog(InputRecognitionResult input) =>
        (input.HasUsableContent && input.Kind is AnalysisDocumentKind.XelDeadlockTrace or AnalysisDocumentKind.DeadlockXml) ||
        (input.Kind == AnalysisDocumentKind.XelDeadlockTrace && input.Status == InputStatus.Unsupported &&
            input.ErrorCode == "INPUT_XEL_NO_DEADLOCK" && input.Deadlocks.Count == 0 &&
            input.XelRecords.All(r => r.Name != "xml_deadlock_report"));

    private void ValidateBudgets(IReadOnlyList<XelSearchSource> sources, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_options.MaxFiles <= 0 || _options.MaxEvents <= 0 || _options.MaxBytes <= 0 || _options.MaxXmlCharacters <= 0)
            throw new InvalidDataException("XEL 检索预算必须为正数。");
        if (sources.Count > _options.MaxFiles) throw new DocumentBudgetExceededException("XEL 集合文件数");
        if (sources.Sum(s => (long)Math.Max(s.Input.XelRecords.Count, s.Input.Deadlocks.Count)) > _options.MaxEvents)
            throw new DocumentBudgetExceededException("XEL 集合事件数（含未处理记录）");
        if (sources.Sum(s => s.Input.Envelope?.SourceBytes ?? s.Input.SourceSnapshot.Length) > _options.MaxBytes)
            throw new DocumentBudgetExceededException("XEL 集合字节数");
        if (sources.Sum(s => SourceCharacters(s, token)) > _options.MaxXmlCharacters)
            throw new DocumentBudgetExceededException("XEL 集合采集字符数");
    }

    private static long SourceCharacters(XelSearchSource source, CancellationToken token)
    {
        long characters = 0;
        if (source.Input.XelRecords.Count > 0)
        {
            foreach (var record in source.Input.XelRecords)
            {
                token.ThrowIfCancellationRequested();
                characters += record.Fields.Concat(record.Actions).Sum(p => (long)p.Key.Length + p.Value.Length);
            }
        }
        else foreach (var item in source.Input.Deadlocks)
        {
            token.ThrowIfCancellationRequested();
            characters += item.Document.ToString(SaveOptions.DisableFormatting).Length;
        }
        return characters;
    }

    public XelSearchResult Search(XelSearchCatalog catalog, XelSearchFilter filter, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (filter.From > filter.To) throw new InvalidDataException("开始时间不能晚于结束时间。");
        if (filter.DisplayOffset < TimeSpan.FromHours(-14) || filter.DisplayOffset > TimeSpan.FromHours(14) || filter.DisplayOffset.Ticks % TimeSpan.TicksPerMinute != 0)
            throw new InvalidDataException("显示时区必须是 -14:00 到 +14:00 之间的整分钟偏移。");
        var rows = new List<XelSearchRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int unknownTimes = 0;
        static bool Contains(string value, string query) => value.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
        foreach (var item in catalog.Events)
        {
            token.ThrowIfCancellationRequested();
            bool duplicate = !seen.Add(item.DuplicateKey);
            var time = item.Event.CapturedAt;
            if (!time.HasValue) unknownTimes++;
            if ((filter.From.HasValue || filter.To.HasValue) && !time.HasValue) continue;
            if (time < filter.From || time > filter.To || !Contains(item.Databases, filter.Database) ||
                !Contains(item.Objects, filter.Object) || !Contains(item.SearchText, filter.Text)) continue;
            string display = "未采集带时区时间";
            if (time.HasValue)
            {
                long ticks = time.Value.UtcTicks + filter.DisplayOffset.Ticks;
                display = ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks ? "转换超出日期范围（见原始时间）"
                    : time.Value.ToOffset(filter.DisplayOffset).ToString("O", CultureInfo.InvariantCulture);
            }
            rows.Add(new(item, display, duplicate));
        }
        var groups = rows.GroupBy(row => row.Entry.StructureKey, StringComparer.Ordinal)
            .Select(g => new XelSearchGroup(g.Key, g.First().Entry.StructureDescription, g.ToArray()))
            .OrderByDescending(g => g.Count).ThenBy(g => g.Key, StringComparer.Ordinal).ToArray();
        token.ThrowIfCancellationRequested();
        string summary = $"已载入 {catalog.Sources.Count} 文件；匹配 {rows.Count} / {catalog.Events.Count} 个有效死锁；聚合 {groups.Length} 组；未处理 XEL 记录 {catalog.SkippedRecords}。"
            + $"未知时区时间 {unknownTimes}（指定时间范围时排除）；重复副本全部保留，当前匹配 {rows.Count(r => r.IsDuplicate)} 个。";
        Logger.Debug($"IMP23 search completed: matched={rows.Count}, groups={groups.Length}.");
        return new(rows, groups, summary);
    }
}
