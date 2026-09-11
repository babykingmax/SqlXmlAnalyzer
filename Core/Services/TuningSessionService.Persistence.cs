using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using SqlXmlAnalyzer.Core.Diagnostics;

namespace SqlXmlAnalyzer.Core.Services;

public interface ITuningSessionWriter
{
    void Write(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);
}

public sealed class TuningSessionWriter : ITuningSessionWriter
{
    public void Write(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        for (int offset = 0; offset < bytes.Length; offset += 64 * 1024)
        {
            cancellationToken.ThrowIfCancellationRequested();
            stream.Write(bytes.Span.Slice(offset, Math.Min(64 * 1024, bytes.Length - offset)));
        }
        stream.Flush(flushToDisk: true);
    }
}

public sealed partial class TuningSessionService
{
    private static string ValidateStructure(XDocument document)
    {
        var root = document.Root;
        if (root?.Name != SessionNamespace + "TuningSession")
            throw new InvalidDataException("SESSION_ROOT_INVALID: 文件不是受支持的调优会话。");
        string? version = (string?)root.Attribute("Version");
        if (version is not (null or "2.0" or CurrentVersion))
            throw new InvalidDataException("SESSION_VERSION_UNSUPPORTED: 不支持此会话版本；请保留原件并使用匹配版本的应用。");

        void Shape(XElement element, string[] attributes, string[] children)
        {
            if (element.Attributes().Any(a => !a.IsNamespaceDeclaration && (a.Name.Namespace != XNamespace.None || !attributes.Contains(a.Name.LocalName)))
                || element.Elements().Any(e => e.Name.Namespace != SessionNamespace || !children.Contains(e.Name.LocalName)))
                throw new InvalidDataException("SESSION_EXTENSION_UNSUPPORTED: 会话包含本版本不能保留的扩展；已停止读取，请保留原件。");
            if (element.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)))
                throw new InvalidDataException("SESSION_STRUCTURE_INVALID: 会话容器包含无法识别的文本。");
            foreach (string child in children)
                if (child != "Snapshot" && element.Elements(SessionNamespace + child).Skip(1).Any())
                    throw new InvalidDataException("SESSION_STRUCTURE_INVALID: 会话包含重复容器。");
        }

        Shape(root, ["Version", "Created", "Privacy", "PlanAId", "PlanBId"], ["Snapshots", "ComparisonSelection"]);
        var container = root.Element(SessionNamespace + "Snapshots");
        var snapshots = container?.Elements(SessionNamespace + "Snapshot").ToArray() ?? [];
        if (snapshots.Length > MaxSnapshots) throw new InvalidDataException("SESSION_BUDGET_EXCEEDED: 会话最多包含 1024 个快照。");
        if (container != null) Shape(container, [], ["Snapshot"]);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var snapshot in snapshots)
        {
            Shape(snapshot, ["Id", "Title", "FilePath", "CaptureTime", "TotalCost", "OperatorCount", "MissingIndexCount",
                "OriginalSourceHash", "OriginalSourceHashBound", "XmlContentHash", "XmlContentHashFormat"], ["StatementText", "PlanDoc", "ComparisonSelection"]);
            string? id = (string?)snapshot.Attribute("Id");
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                throw new InvalidDataException("SESSION_ID_INVALID: 会话快照 ID 缺失或重复。");
            var planDoc = snapshot.Element(SessionNamespace + "PlanDoc");
            if (planDoc == null || planDoc.Attributes().Any(a => !a.IsNamespaceDeclaration) || planDoc.Elements().Count() != 1
                || planDoc.Elements().Single().Name != FallbackShowplanNamespace + "ShowPlanXML"
                || planDoc.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)))
                throw new InvalidDataException("SESSION_PLAN_INVALID: 快照必须包含一个 ShowPlanXML 文档。");
            var text = snapshot.Element(SessionNamespace + "StatementText");
            if (text != null && (text.HasElements || text.Attributes().Any(a => !a.IsNamespaceDeclaration)))
                throw new InvalidDataException("SESSION_EXTENSION_UNSUPPORTED: 语句文本包含未知结构，请保留原件。");
            if (snapshot.Element(SessionNamespace + "ComparisonSelection") is { } selection) ValidateSelectionShape(selection);
        }
        foreach (string side in new[] { "PlanAId", "PlanBId" })
            if (root.Attribute(side) is { } reference && !ids.Contains(reference.Value))
                throw new InvalidDataException("SESSION_REFERENCE_INVALID: 会话比较引用没有对应快照。");
        if (root.Element(SessionNamespace + "ComparisonSelection") is { } pair)
        {
            if (pair.Attributes().Any(a => !a.IsNamespaceDeclaration)
                || pair.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))
                || pair.Elements().Any(e => e.Name != SessionNamespace + "A" && e.Name != SessionNamespace + "B")
                || pair.Elements(SessionNamespace + "A").Skip(1).Any() || pair.Elements(SessionNamespace + "B").Skip(1).Any())
                throw new InvalidDataException("PLAN_SELECTION_NOT_FOUND: 会话包含重复或未知的比较侧。");
            foreach (var selection in pair.Elements()) ValidateSelectionShape(selection);
        }
        return version ?? "unversioned";
    }

    private static void ValidateSelectionShape(XElement selection)
    {
        if (selection.HasElements || selection.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))
            || selection.Attributes().Any(a => !a.IsNamespaceDeclaration
            && (a.Name.Namespace != XNamespace.None || a.Name.LocalName is not ("Batch" or "Statement" or "QueryPlan"))))
            throw new InvalidDataException("PLAN_SELECTION_NOT_FOUND: 会话比较选择包含未知结构。");
    }

    private void SaveNewFile(string path, XDocument document, CancellationToken token)
    {
        string destination;
        try { destination = Path.GetFullPath(path); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        { throw new InvalidDataException("SESSION_PATH_INVALID: 会话保存路径无效。", exception); }
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("SESSION_DESTINATION_EXISTS: 会话仅保存为新文件；请更换名称以保留旧副本。");
        string parent = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("会话保存目录不存在。");

        // Preserve the PlanDoc representation used by the stored content hash.
        string xml = document.ToString(SaveOptions.DisableFormatting);
        if (xml.Length > new DocumentReadOptions().MaxXmlCharacters)
            throw new InvalidDataException("SESSION_BUDGET_EXCEEDED: 会话 XML 超过读取预算。");
        byte[] bytes = new UTF8Encoding(false, true).GetBytes(xml);
        if (bytes.LongLength > new DocumentReadOptions().MaxBytes)
            throw new InvalidDataException("SESSION_BUDGET_EXCEEDED: 会话超过字节读取预算。");
        _ = LoadCore(SafeXmlHelper.ParseSafe(xml));
        byte[] expectedHash = SHA256.HashData(bytes);
        string staging = Path.Combine(parent, ".tmp.session-" + Guid.NewGuid().ToString("N"));
        string temporary = Path.Combine(staging, "session.pesession");
        try
        {
            token.ThrowIfCancellationRequested();
            DiagnosticFileAccess.CreatePrivateDirectory(staging);
            _writer.Write(temporary, bytes, token);
            token.ThrowIfCancellationRequested();
            using (var stream = new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.None))
                if (stream.Length != bytes.Length || !SHA256.HashData(stream).AsSpan().SequenceEqual(expectedHash))
                    throw new InvalidDataException("SESSION_WRITE_MISMATCH: 临时会话与待保存快照不一致。");
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: false);
            }
            catch (Exception exception) { ExceptionPolicy.Describe(exception, "IMP28.Session.Cleanup", _unexpectedErrors); }
        }
    }
}
