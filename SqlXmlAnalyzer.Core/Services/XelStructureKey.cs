using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace SqlXmlAnalyzer.Core.Services;

/// <summary>Canonical resource incidence graph; budget exhaustion preserves the event in its own group.</summary>
internal static class XelStructureKey
{
    private const int MaxLabelCharacters = 1024 * 1024;
    private const int MaxLabelAttributes = 16_384;
    private const long MaxCanonicalWork = 2_000_000;

    internal static string Hash(string value)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendText(hash, value);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    // Length prefixes avoid ambiguous components, nested escaping and input-sized byte arrays.
    internal static string HashParts(IEnumerable<string> values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (string value in values)
        {
            BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
            hash.AppendData(length);
            AppendText(hash, value);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendText(IncrementalHash hash, string value)
    {
        Span<byte> buffer = stackalloc byte[4096];
        var encoder = Encoding.UTF8.GetEncoder();
        ReadOnlySpan<char> remaining = value.AsSpan();
        do
        {
            encoder.Convert(remaining, buffer, flush: true, out int chars, out int bytes, out bool complete);
            hash.AppendData(buffer[..bytes]);
            remaining = remaining[chars..];
            if (complete) break;
        } while (true);
    }

    private static string Attribute(XElement element, string name) => (string?)element.Attribute(name) ?? "";
    private static IEnumerable<XElement> Children(XElement element, string name) => element.Elements().Where(e => e.Name.LocalName == name);
    private static IEnumerable<string> Attributes(XElement element) => element.Attributes()
        .Where(a => a.Name.LocalName != "id").OrderBy(a => a.Name.ToString(), StringComparer.Ordinal)
        .SelectMany(a => new[] { a.Name.ToString(), a.Value });

    public static (string Key, string Description) Create(DeadlockInput input, string uniqueKey, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        (string, string) Separate(string reason) => ("isolated:" + uniqueKey, "单独保留：" + reason);
        var root = input.Document.Root;
        if (root == null) return Separate("没有死锁根节点");
        var processes = Children(root, "process-list").SelectMany(e => e.Elements()).Take(129).ToArray();
        var resources = Children(root, "resource-list").SelectMany(e => e.Elements()).Take(513).ToArray();
        var victimNodes = Children(root, "victim-list").SelectMany(e => e.Elements()).Take(129).ToArray();
        if (processes.Length > 128 || resources.Length > 512 || victimNodes.Length > 128)
            return Separate("结构超过聚合预算");

        // Check before hashing/sorting/copying labels. SQL text is not a structural label.
        long labelCharacters = 0;
        int labelAttributes = 0;
        foreach (var element in processes.Concat(victimNodes).Concat(resources.SelectMany(r => r.DescendantsAndSelf())))
        {
            token.ThrowIfCancellationRequested();
            labelCharacters += element.Name.LocalName.Length + element.Name.NamespaceName.Length;
            foreach (var attribute in element.Attributes())
            {
                labelCharacters += attribute.Name.LocalName.Length + attribute.Name.NamespaceName.Length + attribute.Value.Length;
                if (++labelAttributes > MaxLabelAttributes || labelCharacters > MaxLabelCharacters)
                    return Separate("结构标签超过聚合预算（1 Mi 字符 / 16,384 属性）");
            }
            if (labelCharacters > MaxLabelCharacters) return Separate("结构标签超过聚合预算（1 Mi 字符）");
        }

        var ids = processes.Select(e => Attribute(e, "id")).ToArray();
        if (processes.Length == 0 || resources.Length == 0 || ids.Any(string.IsNullOrEmpty) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            return Separate("进程或资源身份不完整");
        var processIndices = ids.Select((id, i) => (id, i)).ToDictionary(p => p.id, p => p.i, StringComparer.Ordinal);
        var victims = victimNodes.Select(e => Attribute(e, "id")).ToHashSet(StringComparer.Ordinal);
        if (victims.Any(id => !processIndices.ContainsKey(id))) return Separate("受害者引用缺失");
        if (resources.Any(r => r.Name.LocalName is not ("keylock" or "pagelock" or "ridlock" or "objectlock" or "applicationlock" or "hobtlock") ||
            r.Attribute("dbid") == null || !r.Attributes().Any(a => a.Name.LocalName is "objectname" or "objid" or "hobtid" or "associatedObjectId" or "fileid" or "hash")))
            return Separate("资源类型或数据库/对象身份不足以安全聚合");

        var resourceLabels = resources.Select(r => HashParts(new[] { r.Name.ToString() }.Concat(Attributes(r)))).ToArray();
        var links = new List<(int Resource, int Process, string Label)>();
        for (int r = 0; r < resources.Length; r++)
        {
            token.ThrowIfCancellationRequested();
            if (resources[r].Elements().Any(e => e.Name.LocalName is not ("owner-list" or "waiter-list")))
                return Separate("资源包含未支持的依赖结构");
            foreach (var list in resources[r].Elements())
            foreach (var link in list.Elements())
            {
                if (!processIndices.TryGetValue(Attribute(link, "id"), out int process) || link.HasElements ||
                    link.Name.LocalName != (list.Name.LocalName == "owner-list" ? "owner" : "waiter"))
                    return Separate("资源依赖引用不完整");
                if (links.Count >= 4096) return Separate("依赖数超过精确聚合预算");
                links.Add((r, process, HashParts(new[] { list.Name.LocalName }.Concat(Attributes(link)))));
            }
        }
        var byResource = links.ToLookup(l => l.Resource);
        if (resources.Select((_, i) => i).Any(i => !byResource.Contains(i))) return Separate("资源没有可解释的依赖");
        var byProcess = links.ToLookup(l => l.Process);
        var labels = processes.Select((p, i) => HashParts(new[] { victims.Contains(ids[i]) ? "victim" : "survivor",
            Attribute(p, "currentdb"), Attribute(p, "currentdbname"), Attribute(p, "isolationlevel") }.Concat(byProcess[i]
                .Select(l => resourceLabels[l.Resource] + l.Label).OrderBy(s => s, StringComparer.Ordinal)))).ToArray();
        var partitions = Enumerable.Range(0, ids.Length).GroupBy(i => labels[i], StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.ToArray()).ToArray();
        long permutations = 1;
        foreach (var partition in partitions)
            for (int i = 2; i <= partition.Length; i++)
                if ((permutations *= i) > 720) return Separate("对称结构超过精确聚合预算（720）");
        int candidateLength = processes.Length + 2 * resources.Length + 2 * links.Count;
        if (permutations * candidateLength > MaxCanonicalWork)
            return Separate("拓扑比较超过精确聚合预算（2,000,000 整数项）");

        // Intern fixed-size labels once and reuse integer buffers across all permutations.
        // Compare the complete graph, including parallel edges and distinct resource nodes.
        var dictionary = labels.Concat(resourceLabels).Concat(links.Select(l => l.Label))
            .Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var labelIds = dictionary.Select((label, i) => (label, i)).ToDictionary(p => p.label, p => p.i, StringComparer.Ordinal);
        var graph = resources.Select((_, r) => new CanonicalResource(labelIds[resourceLabels[r]],
            byResource[r].Select(l => (labelIds[l.Label], l.Process)).ToArray())).ToArray();
        var mapping = new int[processes.Length];
        var order = new List<int>();
        var candidate = new int[candidateLength];
        int[]? best = null;
        void Visit(int partition)
        {
            token.ThrowIfCancellationRequested();
            if (partition == partitions.Length)
            {
                for (int i = 0; i < order.Count; i++) mapping[order[i]] = i;
                foreach (var resource in graph) resource.Map(mapping);
                Array.Sort(graph, CanonicalResource.Compare);
                int cursor = 0;
                foreach (int index in order) candidate[cursor++] = labelIds[labels[index]];
                foreach (var resource in graph)
                {
                    candidate[cursor++] = resource.Label;
                    candidate[cursor++] = resource.Edges.Length;
                    foreach (long edge in resource.Edges)
                    {
                        candidate[cursor++] = (int)(edge >> 32);
                        candidate[cursor++] = (int)edge;
                    }
                }
                if (best == null) best = (int[])candidate.Clone();
                else if (candidate.AsSpan().SequenceCompareTo(best) < 0) candidate.CopyTo(best, 0);
                return;
            }
            var values = partitions[partition];
            void Permute(int offset)
            {
                token.ThrowIfCancellationRequested();
                if (offset == values.Length)
                {
                    order.AddRange(values); Visit(partition + 1); order.RemoveRange(order.Count - values.Length, values.Length); return;
                }
                for (int i = offset; i < values.Length; i++)
                {
                    (values[offset], values[i]) = (values[i], values[offset]); Permute(offset + 1);
                    (values[offset], values[i]) = (values[i], values[offset]);
                }
            }
            Permute(0);
        }
        Visit(0);
        string types = string.Join("、", resources.Select(r => r.Name.LocalName).Distinct().OrderBy(s => s, StringComparer.Ordinal));
        string key = HashParts(dictionary.Prepend("structure-v2").Append("topology")
            .Concat(new[] { processes.Length, resources.Length, links.Count }.Concat(best!).Select(i => i.ToString(CultureInfo.InvariantCulture))));
        return ("structure-v2:" + key, $"{processes.Length} 进程 / {resources.Length} 资源 / {links.Count} 持有或等待关系；{types}；按数据库、资源属性、锁模式、隔离级别、受害者及依赖拓扑精确匹配");
    }

    private sealed class CanonicalResource(int label, (int Label, int Process)[] links)
    {
        public int Label { get; } = label;
        public long[] Edges { get; } = new long[links.Length];
        public void Map(int[] mapping)
        {
            for (int i = 0; i < links.Length; i++) Edges[i] = ((long)links[i].Label << 32) | (uint)mapping[links[i].Process];
            Array.Sort(Edges);
        }
        public static int Compare(CanonicalResource a, CanonicalResource b)
        {
            int result = a.Label.CompareTo(b.Label);
            if (result == 0) result = a.Edges.Length.CompareTo(b.Edges.Length);
            return result == 0 ? a.Edges.AsSpan().SequenceCompareTo(b.Edges) : result;
        }
    }
}
