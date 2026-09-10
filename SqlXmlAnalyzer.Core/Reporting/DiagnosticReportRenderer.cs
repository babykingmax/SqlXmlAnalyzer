using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SqlXmlAnalyzer.Core.Reporting;

public static class DiagnosticReportRenderer
{
    public static string Json(DiagnosticReport report, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var bytes = new ReportByteBuffer(DiagnosticReportLimits.MaxEncodedLength, token);
        using (var writer = new Utf8JsonWriter(bytes, new JsonWriterOptions { Indented = true })) JsonSerializer.Serialize(writer, report);
        return Encoding.UTF8.GetString(bytes.WrittenMemory.Span);
    }
    public static string Text(DiagnosticReport report, CancellationToken token = default)
    {
        using var text = new ReportTextWriter(DiagnosticReportLimits.MaxContentCharacters, token);
        text.WriteLine(report.PrivacyNotice);
        text.WriteLine($"DiagnosticReport {report.SchemaVersion} | SqlXmlAnalyzer {report.ProductVersion}");
        text.WriteLine($"Kind: {report.Kind} | Scope: {report.Scope} | CreatedAt: {report.CreatedAt:O}");
        text.WriteLine($"Facts: {report.FactCount} | Issues: {report.IssueCount} | Hit: {report.HitCount} | NoHit: {report.NoHitCount} | Skipped: {report.SkippedCount} | Failed: {report.FailedCount}");
        text.WriteLine(report.Capabilities);
        void Fields(IEnumerable<ReportField> fields)
        {
            foreach (var field in fields) { text.Write(field.Name); text.Write(": "); text.WriteLine(field.Value); }
        }
        text.WriteLine("=== Metadata ==="); Fields(report.Metadata);
        text.WriteLine("=== Facts ==="); Fields(report.Facts);
        foreach (var section in new[] { ("Diagnostics", report.Issues), ("Rule Runs", report.Runs) })
        {
            text.WriteLine("=== " + section.Item1 + " ===");
            foreach (var item in section.Item2)
            {
                text.WriteLine($"{item.Id} | {item.RuleId} / {item.RuleVersion} | {item.Location} | {item.Status} | {item.Severity} | {item.Confidence}");
                Fields(item.Fields);
            }
        }
        text.WriteLine("=== Graph nodes / edges (complete) ===");
        foreach (var node in report.Nodes) { text.Write(node.Id); text.Write(": "); text.WriteLine(node.Label); }
        foreach (var edge in report.Edges) { text.Write(edge.From); text.Write(" -> "); text.WriteLine(edge.To); }
        text.WriteLine("=== Source XML (selection) ==="); text.WriteLine(report.SourceXml);
        return text.ToString();
    }

    public static string Html(DiagnosticReport report, CancellationToken token = default)
    {
        using var html = new ReportTextWriter(DiagnosticReportLimits.MaxEncodedLength, token);
        html.Write("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">"
        + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
        + "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; script-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src 'none'; connect-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'\">"
        + "<title>DiagnosticReport</title><style>body{font:14px sans-serif;margin:24px;color:#172b4d;background:white}pre{white-space:pre-wrap;overflow-wrap:anywhere}svg{max-width:100%;height:auto}h1{font-size:24px}</style></head><body><h1>诊断报告 / ");
        WebUtility.HtmlEncode(report.Scope, html);
        html.Write("</h1>"); html.Write(Svg(report, token)); html.Write("<pre id=\"report-text\">");
        WebUtility.HtmlEncode(Text(report, token), html); html.Write("</pre></body></html>");
        return html.ToString();
    }

    public static string Svg(DiagnosticReport report, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var nodes = report.Nodes.Take(80).ToArray();
        var indices = nodes.Select((n, i) => (n.Id, i)).ToDictionary(p => p.Id, p => p.i, StringComparer.Ordinal);
        int height = Math.Max(64, nodes.Length * 70 + 48);
        using var svg = new ReportTextWriter(DiagnosticReportLimits.MaxEncodedLength, token);
        svg.Write($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 900 {height}\" role=\"img\" aria-label=\"静态关系图\"><desc>{WebUtility.HtmlEncode(report.PrivacyNotice)}</desc><rect width=\"900\" height=\"{height}\" fill=\"white\"/><text x=\"8\" y=\"20\" font-size=\"14\" fill=\"#172b4d\">{(report.IsRedacted ? "已脱敏静态图" : "未脱敏图形，仅供本地诊断")} / {WebUtility.HtmlEncode(report.Scope)}</text><g transform=\"translate(0,32)\">");
        foreach (var edge in report.Edges)
        {
            token.ThrowIfCancellationRequested();
            if (indices.TryGetValue(edge.From, out int from) && indices.TryGetValue(edge.To, out int to))
                svg.Write($"<path d=\"M 760 {from * 70 + 38} L 800 {from * 70 + 38} L 800 {to * 70 + 38} L 765 {to * 70 + 38}\" fill=\"none\" stroke=\"#667788\"/><circle cx=\"765\" cy=\"{to * 70 + 38}\" r=\"4\" fill=\"#667788\"/>");
        }
        for (int i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i]; int length = Math.Min(78, node.Label.Length);
            if (length < node.Label.Length && char.IsHighSurrogate(node.Label[length - 1])) length--;
            string label = length < node.Label.Length ? node.Label[..length] + "…" : node.Label;
            svg.Write($"<rect x=\"8\" y=\"{i * 70 + 8}\" width=\"750\" height=\"54\" rx=\"5\" fill=\"#edf5ff\" stroke=\"#557799\"/><text x=\"18\" y=\"{i * 70 + 28}\" font-size=\"12\" fill=\"#172b4d\">{WebUtility.HtmlEncode(node.Id)}</text><text x=\"18\" y=\"{i * 70 + 48}\" font-size=\"12\" fill=\"#172b4d\">{WebUtility.HtmlEncode(label)}</text>");
        }
        svg.Write("</g></svg>"); return svg.ToString();
    }
}
